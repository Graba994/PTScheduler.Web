using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Szeroka analiza Leo („Leo — analiza” w Panelu i raport tygodniowy): ostrzeżenia z nietypowych zmian,
/// segmenty trenerów (rosną, blisko limitu, uśpieni), pieniądze zagrożone odejściem, głos klientów
/// (o co proszą trenerzy) i „Zapytaj Leo”. Wszystko liczą reguły z dziennych odczytów metryk —
/// same sumy z aplikacji trenerów, bez danych ich klientów. AI najwyżej opisuje gotowe liczby;
/// do AI trafiają tylko nazwy studiów i liczby (bez imion, e-maili i telefonów właścicieli).
/// </summary>
public class LeoAnalytics(IDbContextFactory<PortalDbContext> dbFactory, LeoService leo, AssistantService assistant, AiService ai,
    SiteSettingsService settings)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Jedno studio (aplikacja trenera) z odczytami: teraz, tydzień i 30 dni temu. Puste pola — brak danych (starsza aplikacja).</summary>
    public sealed record Studio(
        int Id, string Name, string Slug, string OwnerEmail, string PlanId, string PlanName, int ClientLimit, decimal MonthlyValue, bool Paying,
        int Clients, int? Clients7Ago, int? Clients30Ago, int? ActiveClients30,
        int? Sessions7, int? Sessions7Prev, int? Cancelled7, int? NoShow7, int? Upcoming7,
        decimal? Revenue30, decimal? Revenue30Prev, int? Workouts30, int? Messages30, int? Plans,
        DateTime? LastActivityAt, DateTime? ProvisionedAt, int RiskScore, List<string> RiskReasons);

    public sealed record Warning(Studio Studio, string Kind, int Severity, string Title, string Detail, string Action);

    public const string Growing = "rosnie";
    public const string NearLimit = "limit";
    public const string Dormant = "uspieni";

    public sealed record SegmentItem(Studio Studio, string Note);
    public sealed record Segment(string Key, string Title, string Hint, List<SegmentItem> Items);

    public sealed record MoneyItem(Studio Studio, int Score, decimal Monthly, decimal Expected, List<string> Reasons);
    public sealed record Money(List<MoneyItem> Items, decimal MonthlyAtRisk, decimal ExpectedMonthly, decimal YearlyAtRisk);

    public sealed record VoiceTopic(string Key, string Label, int Mentions, Dictionary<string, int> Sources, List<string> Quotes);
    public sealed record Voice(List<VoiceTopic> Topics, int Texts, int Unmatched, List<string> UnmatchedQuotes);

    public sealed record Overview(DateTime At, List<Studio> Studios, List<Warning> Warnings, List<Segment> Segments, Money Money, Voice Voice)
    {
        public bool HasActivityData => Studios.Any(s => s.Sessions7 is not null);
    }

    // ── Dane ──

    public async Task<Overview> BuildAsync()
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        await using var db = await dbFactory.CreateDbContextAsync();
        var tenants = await db.Tenants.AsNoTracking().Include(t => t.Plan).Where(t => t.Status == TenantStatus.Active).ToListAsync();
        var plans = await db.Plans.AsNoTracking().ToListAsync();
        var from = today.AddDays(-40);
        var snapshots = (await db.TenantMetricSnapshots.AsNoTracking().Where(x => x.Date >= from).ToListAsync())
            .GroupBy(x => x.TenantId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Date).ToList());
        var addons = await MonthlyAddonsAsync(db);
        var risks = (await leo.GetRisksAsync()).ToDictionary(r => r.TenantId);

        var studios = tenants.Select(t =>
        {
            var list = snapshots.GetValueOrDefault(t.Id) ?? [];
            var latest = list.LastOrDefault();
            var week = Back(list, 7);
            var month = Back(list, 30);
            var plan = t.Plan ?? plans.FirstOrDefault(p => p.Id == t.PlanId);
            var price = plan is null ? 0m
                : t.BillingInterval == "yearly" && PlanPricing.Yearly(t, plan) is > 0 and var y ? Math.Round(y / 12m, 2) : PlanPricing.Monthly(t, plan);
            var risk = risks.GetValueOrDefault(t.Id);
            int? sessions7 = latest?.Sessions7, sessions7Prev = week?.Sessions7;
            // Starsza aplikacja (bez okien 7-dniowych): przybyło wizyt w kalendarzu z różnicy liczników.
            if (sessions7 is null && latest is not null && week is not null)
            {
                sessions7 = Math.Max(0, latest.SessionsTotal - week.SessionsTotal);
                var twoWeeks = Back(list, 14);
                sessions7Prev = twoWeeks is null ? null : Math.Max(0, week.SessionsTotal - twoWeeks.SessionsTotal);
            }
            return new Studio(t.Id, Display(t.CompanyName, t.Slug), t.Slug, t.OwnerEmail, t.PlanId, plan?.Name ?? t.PlanId, ClientLimit(plan),
                price + addons.GetValueOrDefault(t.Id), t.FirstPaidAt is not null || t.BillingStatus == "active",
                latest?.Clients ?? t.MetricsClients ?? 0, week?.Clients, month?.Clients, latest?.ActiveClients30,
                sessions7, sessions7Prev, latest?.Cancelled7, latest?.NoShow7, latest?.Upcoming7,
                latest?.Revenue30, month?.Revenue30, latest?.Workouts30, latest?.Messages30, latest?.Plans,
                t.LastActivityAt, t.ProvisionedAt, risk?.Score ?? 0, risk?.Reasons ?? []);
        }).ToList();

        var warnings = Warnings(studios);
        var segments = Segments(studios, plans, now);
        var money = MoneyAtRisk(studios);
        var voice = await VoiceAsync(db);
        return new(now, studios, warnings, segments, money, voice);
    }

    /// <summary>Najnowszy odczyt sprzed co najmniej <paramref name="days"/> dni (z zapasem 5 dni — dalej to już nie „tydzień temu”).</summary>
    public static TenantMetricSnapshot? Back(IReadOnlyList<TenantMetricSnapshot> list, int days)
    {
        if (list.Count < 2) return null;
        var latest = list[^1].Date;
        return list.LastOrDefault(x => x.Date <= latest.AddDays(-days) && x.Date >= latest.AddDays(-days - 5));
    }

    /// <summary>Miesięczne dodatki (SMS, wideo…) — cena indywidualna albo katalogowa × ilość.</summary>
    private static async Task<Dictionary<int, decimal>> MonthlyAddonsAsync(PortalDbContext db)
    {
        var rows = await db.TenantAddons.AsNoTracking().Where(a => a.Status == TenantAddonStatus.Active && a.ServiceItem!.PriceType == "monthly")
            .Select(a => new
            {
                a.TenantId, a.Quantity,
                Price = db.TenantServicePrices.Where(p => p.TenantId == a.TenantId && p.ServiceItemId == a.ServiceItemId)
                    .Select(p => (decimal?)p.CustomPrice).FirstOrDefault() ?? a.ServiceItem!.DefaultPrice,
            }).ToListAsync();
        return rows.GroupBy(r => r.TenantId).ToDictionary(g => g.Key, g => g.Sum(r => r.Price * Math.Max(1, r.Quantity)));
    }

    // ── Ostrzeżenia ──

    /// <summary>Nietypowe zmiany u trenera: spadek wizyt, fala nieobecności lub odwołań, pusty kalendarz, odpływ klientów, spadek przychodu.</summary>
    public static List<Warning> Warnings(IReadOnlyList<Studio> studios)
    {
        var list = new List<Warning>();
        foreach (var s in studios)
        {
            if (s.Sessions7Prev is >= 5 and var prev && s.Sessions7 is { } now7 && now7 * 2 <= prev)
            {
                var drop = (int)Math.Round((prev - now7) * 100.0 / prev);
                list.Add(new(s, "spadek_wizyt", now7 * 4 <= prev ? 3 : 2, $"Mniej wizyt o {drop}%",
                    $"Ostatnie 7 dni: {now7} {Plural(now7, "wizyta", "wizyty", "wizyt")}, tydzień wcześniej: {prev}.",
                    "Zapytaj, co się zmieniło — urlop, sezon, a może coś przestało działać."));
            }

            var held = (s.Sessions7 ?? 0) + (s.NoShow7 ?? 0) + (s.Cancelled7 ?? 0);
            if (s.NoShow7 is >= 3 and var ns && ns * 5 >= held)
                list.Add(new(s, "nieobecnosci", ns * 10 >= held * 3 ? 3 : 2, "Dużo nieobecności",
                    $"{ns} {Plural(ns, "nieobecność", "nieobecności", "nieobecności")} na {held} {Plural(held, "wizytę", "wizyty", "wizyt")} w tydzień ({Pct(ns, held)}%).",
                    "Podsuń przypomnienia push/SMS przed wizytą i zasady odwołań z opłatą za późne odwołanie."));

            if (s.Cancelled7 is >= 4 and var c && c * 10 >= held * 3)
                list.Add(new(s, "odwolania", c * 2 >= held ? 3 : 2, "Fala odwołań",
                    $"{c} {Plural(c, "odwołanie", "odwołania", "odwołań")} na {held} {Plural(held, "wizytę", "wizyty", "wizyt")} w tydzień ({Pct(c, held)}%).",
                    "Pokaż politykę odwołań (termin i opłata) — często wystarczy ją włączyć w ustawieniach."));

            if (s.Upcoming7 == 0 && s.Clients >= 3 && (s.Sessions7 > 0 || s.ActiveClients30 > 0))
                list.Add(new(s, "pusty_kalendarz", 2, "Pusty kalendarz na najbliższy tydzień",
                    $"Brak zaplanowanych wizyt na 7 dni, a w ostatnim miesiącu {s.ActiveClients30 ?? 0} {Plural(s.ActiveClients30 ?? 0, "klient trenował", "klientów trenowało", "klientów trenowało")}.",
                    "Zapytaj, czy grafik prowadzi gdzie indziej — i pokaż serie wizyt („tak samo za tydzień”)."));

            if (s.Clients30Ago is { } before && before - s.Clients >= 3 && (before - s.Clients) * 100 >= before * 15)
                list.Add(new(s, "odplyw_klientow", 2, "Ubyło klientów",
                    $"{before} → {s.Clients} {Plural(s.Clients, "klient", "klientów", "klientów")} w 30 dni.",
                    "Zapytaj o powód i podsuń automatyzację odzyskiwania klientów."));

            if (s.Revenue30Prev is >= 500 and var rp && s.Revenue30 is { } r && r * 2 <= rp)
                list.Add(new(s, "spadek_przychodu", 2, "Przychód z aplikacji spadł o połowę",
                    $"Ostatnie 30 dni: {Zl(r)}, miesiąc wcześniej: {Zl(rp)}.",
                    "Sprawdź z trenerem płatności online — może klienci płacą teraz poza aplikacją."));
        }
        return list.OrderByDescending(w => w.Severity).ThenBy(w => w.Studio.Name, StringComparer.Create(Pl, true)).ToList();
    }

    // ── Segmenty ──

    public static List<Segment> Segments(IReadOnlyList<Studio> studios, IReadOnlyList<Plan> plans, DateTime now)
    {
        var growing = new List<SegmentItem>();
        var limit = new List<SegmentItem>();
        var dormant = new List<SegmentItem>();
        foreach (var s in studios)
        {
            if (s.Clients30Ago is { } before && s.Clients - before >= 3 && s.Clients * 5 >= before * 6)
                growing.Add(new(s, $"+{s.Clients - before} {Plural(s.Clients - before, "klient", "klientów", "klientów")} w 30 dni ({before} → {s.Clients})"));
            else if (s.Sessions7Prev is >= 3 and var prev && s.Sessions7 is { } now7 && now7 - prev >= 3 && now7 * 2 >= prev * 3)
                growing.Add(new(s, $"Wizyty w tydzień: {prev} → {now7}"));

            if (s.ClientLimit > 0 && s.Clients * 5 >= s.ClientLimit * 4)
            {
                var next = NextPlan(plans, s);
                limit.Add(new(s, $"{s.Clients} z {s.ClientLimit} klientów" + (s.Clients >= s.ClientLimit ? " — limit osiągnięty" : "")
                    + (next is null ? "" : $" · wyższy plan: {next.Name}")));
            }

            var settled = s.ProvisionedAt is { } p && (now - p).TotalDays >= 7;
            var idleDays = s.LastActivityAt is { } last ? (int)(now - last).TotalDays : (int?)null;
            if (settled && ((s.Sessions7 == 0 && s.Upcoming7 is null or 0) || idleDays >= 14))
                dormant.Add(new(s, idleDays >= 14 ? $"Bez logowania od {idleDays} dni" : "Brak wizyt w ostatnim i najbliższym tygodniu"));
        }
        return
        [
            new(Growing, "Rosną", "Więcej klientów albo wizyt niż miesiąc temu — dobry moment na pochwałę i prośbę o polecenie.", growing),
            new(NearLimit, "Blisko limitu klientów", "Mają co najmniej 80% limitu planu — zaproponuj wyższy plan, zanim limit zablokuje nowych klientów.", limit),
            new(Dormant, "Uśpieni", "Nie logują się albo nie mają wizyt — krótka wiadomość z jedną praktyczną wskazówką.", dormant),
        ];
    }

    /// <summary>Limit klientów planu; 0 — bez limitu (w planach zapisywany jako 9999+ albo int.MaxValue).</summary>
    public static int ClientLimit(Plan? p) => p is null || p.MaxClients <= 0 || p.MaxClients >= 9999 ? 0 : p.MaxClients;

    /// <summary>Najtańszy aktywny plan z większym limitem klientów niż obecny.</summary>
    public static Plan? NextPlan(IReadOnlyList<Plan> plans, Studio s) =>
        plans.Where(p => p.IsActive && p.Id != s.PlanId && (ClientLimit(p) == 0 || ClientLimit(p) > s.ClientLimit))
            .OrderBy(p => p.MonthlyPrice).ThenBy(p => p.SortOrder).FirstOrDefault();

    // ── Pieniądze zagrożone ──

    /// <summary>Ryzyko odejścia × wartość abonamentu (plan + dodatki miesięcznie). „Oczekiwana strata” = wartość × punkty/100.</summary>
    public static Money MoneyAtRisk(IReadOnlyList<Studio> studios)
    {
        var items = studios.Where(s => s.RiskScore >= LeoService.RiskMedium && s.MonthlyValue > 0)
            .Select(s => new MoneyItem(s, s.RiskScore, s.MonthlyValue, Math.Round(s.MonthlyValue * s.RiskScore / 100m, 2), s.RiskReasons))
            .OrderByDescending(i => i.Expected).ThenByDescending(i => i.Score).ToList();
        var monthly = items.Sum(i => i.Monthly);
        return new(items, monthly, items.Sum(i => i.Expected), monthly * 12);
    }

    // ── Głos klientów ──

    /// <summary>Tematy próśb i bolączek trenerów — rozpoznawane po początkach słów (bez odmiany).</summary>
    public static readonly (string Key, string Label, string[] Words)[] Topics =
    [
        ("platnosci", "Płatności online i karnety", ["płatn", "platn", "przelew", "blik", "karnet", "pakiet", "przedpłat", "zaliczk"]),
        ("faktury", "Faktury i rozliczenia", ["faktur", "ksef", "paragon", "rozlicz", "księgow"]),
        ("kalendarz", "Kalendarz i grafik", ["kalendarz", "grafik", "google", "termin", "rezerwac", "zapis"]),
        ("przypomnienia", "Przypomnienia i nieobecności", ["przypomn", "sms", "nieobecn", "nie przychodz", "odwoł", "odwol", "spóźni"]),
        ("plany", "Plany treningowe i ćwiczenia", ["plan", "ćwicze", "cwicze", "trening", "seri", "progres"]),
        ("dieta", "Dieta i jadłospisy", ["diet", "jadłosp", "jadlosp", "kalori", "posił", "żywien", "zywien", "makro"]),
        ("postepy", "Postępy, pomiary i zdjęcia", ["postęp", "postep", "pomiar", "waga", "zdjęci", "sylwetk", "wynik"]),
        ("mobilna", "Aplikacja na telefon", ["na telefon", "w telefonie", "mobil", "android", "iphone", "ios", "smartfon"]),
        ("kontakt", "Komunikacja z klientem", ["czat", "chat", "wiadomo", "whatsapp", "messenger", "kontakt"]),
        ("grupowe", "Zajęcia grupowe i duety", ["grup", "zajęcia", "zajecia", "duet", "w parze"]),
        ("raporty", "Raporty i statystyki", ["raport", "statyst", "przychod", "przychód", "zarob", "analiz"]),
        ("marketing", "Strona, marketing i polecenia", ["stron", "marketing", "instagram", "facebook", "polec", "opini", "reklam"]),
        ("cena", "Cena i plany abonamentu", ["cena", "cenn", "drogo", "tanio", "taniej", "abonament", "koszt"]),
        ("latwosc", "Prostota i szybkość obsługi", ["prost", "łatw", "latw", "intuic", "szybk", "wolno", "zawiesz", "błąd", "blad", "nie dział"]),
    ];

    public static readonly Dictionary<string, string> SourceLabels = new()
    {
        ["ankieta"] = "ankieta", ["spotkanie"] = "spotkania", ["ocena"] = "oceny", ["zgloszenie"] = "zgłoszenia",
    };

    /// <summary>Ranking tematów z wolnych tekstów (źródło, tekst). Cytaty bez e-maili i telefonów.</summary>
    public static Voice RankVoice(IReadOnlyList<(string Source, string Text)> texts)
    {
        var hits = Topics.ToDictionary(t => t.Key, _ => new List<(string Source, string Text)>());
        var unmatched = new List<string>();
        foreach (var (source, raw) in texts)
        {
            var text = raw.ToLower(Pl);
            var matched = false;
            foreach (var topic in Topics)
            {
                if (!topic.Words.Any(w => text.Contains(w, StringComparison.Ordinal))) continue;
                hits[topic.Key].Add((source, raw));
                matched = true;
            }
            if (!matched) unmatched.Add(LeoInsights.Scrub(raw, 160));
        }
        var topics = Topics.Select(t => new VoiceTopic(t.Key, t.Label, hits[t.Key].Count,
                hits[t.Key].GroupBy(h => h.Source).ToDictionary(g => g.Key, g => g.Count()),
                hits[t.Key].Select(h => LeoInsights.Scrub(h.Text, 160)).Distinct().Take(3).ToList()))
            .Where(t => t.Mentions > 0)
            .OrderByDescending(t => t.Mentions).ThenByDescending(t => t.Sources.Count).ToList();
        return new(topics, texts.Count, unmatched.Count, unmatched.Take(5).ToList());
    }

    private static async Task<Voice> VoiceAsync(PortalDbContext db)
    {
        var since = DateTime.UtcNow.AddDays(-180);
        var texts = new List<(string, string)>();
        texts.AddRange((await db.TrainerSurveyResponses.AsNoTracking().Where(r => r.CreatedAt >= since && r.Wish != null && r.Wish != "")
            .Select(r => r.Wish!).ToListAsync()).Select(t => ("ankieta", t)));
        foreach (var m in await db.TrainerMeetings.AsNoTracking().Where(m => m.CreatedAt >= since)
                     .Select(m => new { m.Pains, m.DemoNotes, m.Notes }).ToListAsync())
            foreach (var t in new[] { m.Pains, m.DemoNotes, m.Notes }.Where(t => !string.IsNullOrWhiteSpace(t)))
                texts.Add(("spotkanie", t!));
        texts.AddRange((await db.AppFeedbacks.AsNoTracking().Where(f => f.CreatedAt >= since && f.Text != null && f.Text != "")
            .Select(f => f.Text!).ToListAsync()).Select(t => ("ocena", t)));
        texts.AddRange((await db.ServiceOrders.AsNoTracking().Where(o => o.CreatedAt >= since && o.Notes != null && o.Notes != "")
            .Select(o => o.Notes!).ToListAsync()).Select(t => ("zgloszenie", t)));
        return RankVoice(texts);
    }

    public async Task<AssistantService.Insight?> LoadVoiceInsightAsync()
    {
        var json = await settings.GetAsync(K.LeoInsightVoice);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AssistantService.Insight>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Wnioski z rankingu tematów (AI na kliknięcie; bez AI — z reguł). Zapisywane, żeby było je widać po odświeżeniu.</summary>
    public async Task<AssistantService.Insight> VoiceInsightAsync(Voice voice)
    {
        AssistantService.Insight insight;
        if (voice.Texts == 0)
            insight = new("Brak wypowiedzi trenerów z ostatniego półrocza — ankieta, notatki ze spotkań, oceny i zgłoszenia są puste.", false, null, [], DateTime.UtcNow);
        else
        {
            var lines = new List<string>();
            if (voice.Texts < 10) lines.Add($"- Na razie tylko {voice.Texts} {Plural(voice.Texts, "wypowiedź", "wypowiedzi", "wypowiedzi")} — traktuj ranking jako wskazówkę.");
            foreach (var t in voice.Topics.Take(4))
                lines.Add($"- {t.Label}: {t.Mentions} {Plural(t.Mentions, "wzmianka", "wzmianki", "wzmianek")} ({string.Join(", ", t.Sources.Select(x => $"{SourceLabels.GetValueOrDefault(x.Key, x.Key)} {x.Value}"))}).");
            lines.Add("Co zrobić:");
            if (voice.Topics.FirstOrDefault() is { } top) lines.Add($"- Sprawdź, czy temat „{top.Label}” da się rozwiązać istniejącą funkcją — wtedy pokaż ją w e-mailu i w demo.");
            lines.Add("- Odezwij się do osób z ocen i zgłoszeń, gdy funkcja będzie gotowa.");
            var data = new
            {
                liczba_wypowiedzi = voice.Texts,
                tematy = voice.Topics.Select(t => new { temat = t.Label, wzmianki = t.Mentions, zrodla = t.Sources, przyklady = t.Quotes }),
                bez_tematu = voice.Unmatched,
                przyklady_bez_tematu = voice.UnmatchedQuotes,
            };
            insight = await assistant.AnalyzeAsync("glos_klientow",
                "To ranking próśb i bolączek trenerów personalnych korzystających z aplikacji (ankieta, notatki ze spotkań, oceny aplikacji, zgłoszenia). Wskaż, o co proszą najczęściej, co z tego zrobić najpierw (największy efekt) i co może być nieporozumieniem do wyjaśnienia w komunikacji.",
                data, string.Join('\n', lines));
        }
        await settings.SetAsync(K.LeoInsightVoice, JsonSerializer.Serialize(insight));
        return insight;
    }

    // ── Zapytaj Leo ──

    public sealed record Answer(string Text, bool FromAi, string? Error, List<string> Unverified);

    /// <summary>Gotowe pytania — odpowiedź z reguł, bez AI i bez kosztów.</summary>
    public static readonly (string Key, string Label)[] Presets =
    [
        ("tydzien", "Jak minął tydzień?"),
        ("rosnie", "Kto rośnie najszybciej?"),
        ("odejda", "Kto może odejść?"),
        ("pieniadze", "Ile pieniędzy jest zagrożone?"),
        ("limit", "Komu zaproponować wyższy plan?"),
        ("uspieni", "Kto przestał korzystać?"),
        ("ostrzezenia", "Co niepokojącego w tym tygodniu?"),
        ("glos", "O co najczęściej proszą trenerzy?"),
    ];

    public static Answer AnswerPreset(string key, Overview o)
    {
        var s = o.Studios;
        var lines = new List<string>();
        switch (key)
        {
            case "tydzien":
            {
                var withData = s.Where(x => x.Sessions7 is not null).ToList();
                var sessions = withData.Sum(x => x.Sessions7 ?? 0);
                var prev = withData.Where(x => x.Sessions7Prev is not null).Sum(x => x.Sessions7Prev ?? 0);
                var clients = s.Sum(x => x.Clients);
                var clientsWeek = s.Where(x => x.Clients7Ago is not null).Sum(x => x.Clients - x.Clients7Ago!.Value);
                lines.Add($"Aktywne studia: {s.Count}, klientów u trenerów razem: {clients}" + (clientsWeek != 0 ? $" ({(clientsWeek > 0 ? "+" : "")}{clientsWeek} w tydzień)." : "."));
                if (withData.Count > 0) lines.Add($"Wizyty w ostatnich 7 dniach: {sessions}" + (prev > 0 ? $" (tydzień wcześniej {prev}, {Change(sessions, prev)})." : "."));
                lines.Add($"Ostrzeżenia: {o.Warnings.Count}, zagrożone przychody: {Zl(o.Money.MonthlyAtRisk)} miesięcznie.");
                if (o.Segments.First(x => x.Key == Growing).Items.Count is > 0 and var g) lines.Add($"Rośnie {g} {Plural(g, "studio", "studia", "studiów")} — warto pochwalić i poprosić o polecenie.");
                break;
            }
            case "rosnie":
            {
                var items = o.Segments.First(x => x.Key == Growing).Items;
                if (items.Count == 0) lines.Add("Nikt wyraźnie nie urósł w ostatnim miesiącu (próg: +3 klientów i +20% albo +50% wizyt w tydzień).");
                else lines.AddRange(items.Take(6).Select(i => $"- {i.Studio.Name}: {i.Note}"));
                break;
            }
            case "odejda":
            {
                var risky = s.Where(x => x.RiskScore >= LeoService.RiskMedium).OrderByDescending(x => x.RiskScore).Take(6).ToList();
                if (risky.Count == 0) lines.Add("Nikt nie ma podwyższonego ryzyka odejścia.");
                else lines.AddRange(risky.Select(x => $"- {x.Name}: {x.RiskScore} pkt — {string.Join("; ", x.RiskReasons.Take(2))}"));
                break;
            }
            case "pieniadze":
            {
                var m = o.Money;
                if (m.Items.Count == 0) lines.Add("Żadne płacące studio nie ma podwyższonego ryzyka — nic nie jest zagrożone.");
                else
                {
                    lines.Add($"Zagrożone abonamenty: {Zl(m.MonthlyAtRisk)} miesięcznie ({Zl(m.YearlyAtRisk)} rocznie), oczekiwana strata z uwzględnieniem ryzyka: {Zl(m.ExpectedMonthly)} miesięcznie.");
                    lines.AddRange(m.Items.Take(5).Select(i => $"- {i.Studio.Name}: {Zl(i.Monthly)}/mies. × {i.Score}% = {Zl(i.Expected)}"));
                }
                break;
            }
            case "limit":
            {
                var items = o.Segments.First(x => x.Key == NearLimit).Items;
                if (items.Count == 0) lines.Add("Nikt nie zbliża się do limitu klientów w swoim planie.");
                else lines.AddRange(items.Take(6).Select(i => $"- {i.Studio.Name}: {i.Note}"));
                break;
            }
            case "uspieni":
            {
                var items = o.Segments.First(x => x.Key == Dormant).Items;
                if (items.Count == 0) lines.Add("Wszyscy aktywnie korzystają z aplikacji.");
                else lines.AddRange(items.Take(6).Select(i => $"- {i.Studio.Name}: {i.Note}"));
                break;
            }
            case "ostrzezenia":
                if (o.Warnings.Count == 0) lines.Add("Brak niepokojących zmian w tym tygodniu.");
                else lines.AddRange(o.Warnings.Take(6).Select(w => $"- {w.Studio.Name}: {w.Title}. {w.Detail}"));
                break;
            case "glos":
                if (o.Voice.Topics.Count == 0) lines.Add("Brak wypowiedzi trenerów do analizy (ankieta, spotkania, oceny, zgłoszenia).");
                else lines.AddRange(o.Voice.Topics.Take(5).Select(t => $"- {t.Label}: {t.Mentions} {Plural(t.Mentions, "wzmianka", "wzmianki", "wzmianek")}"));
                break;
            default:
                lines.Add("Nie znam tego pytania.");
                break;
        }
        return new(string.Join('\n', lines), false, null, []);
    }

    /// <summary>Bez AI: dopasuj pytanie do gotowego po słowach kluczowych.</summary>
    public static string? MatchPreset(string question)
    {
        var q = question.ToLower(Pl);
        (string Key, string[] Words)[] map =
        [
            ("pieniadze", ["pieniąd", "pieniad", "zagrożon", "zagrozon", "strac", "mrr", "przychód", "przychod"]),
            ("odejda", ["odejd", "odejś", "odejs", "rezygn", "ryzyk", "churn"]),
            ("limit", ["limit", "wyższy plan", "wyzszy plan", "upgrade", "droższ"]),
            ("rosnie", ["rośn", "rosn", "rozw", "wzrost", "najlepi"]),
            ("uspieni", ["uśpi", "uspi", "nie korzyst", "nieaktyw", "przestał", "przestal"]),
            ("ostrzezenia", ["ostrzeż", "ostrzez", "niepokoj", "anomal", "problem", "spad"]),
            ("glos", ["prosz", "chcą", "chca", "funkcj", "brakuje", "życz", "zycz", "opini"]),
            ("tydzien", ["tydzień", "tydzien", "tygodn", "podsum", "ogólnie", "ogolnie"]),
        ];
        return map.FirstOrDefault(m => m.Words.Any(w => q.Contains(w, StringComparison.Ordinal))).Key;
    }

    /// <summary>Pytanie własnymi słowami: AI odpowiada na podstawie paczki danych (nazwy studiów i liczby). Bez AI — najbliższe gotowe pytanie.</summary>
    public async Task<Answer> AskAsync(string question, Overview o)
    {
        question = question.Trim();
        if (question.Length > 400) question = question[..400];
        var preset = MatchPreset(question);
        var cfg = await assistant.GetLeoConfigAsync();
        if (!cfg.Ai)
            return preset is null
                ? new("Bez AI odpowiadam na gotowe pytania — wybierz jedno z listy powyżej.", false, null, [])
                : AnswerPreset(preset, o) with { Error = $"Bez AI — odpowiedź na pytanie „{Presets.First(p => p.Key == preset).Label}”." };

        var json = JsonSerializer.Serialize(Pack(o), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var name = cfg.Name;
        var system = (await assistant.GetLeoPromptAsync()).Replace("{NAZWA}", name) + """


            Teraz odpowiadasz administratorowi platformy na pytanie o dane (zasada o 2 zdaniach tu nie obowiązuje):
            - Odpowiadaj wyłącznie na podstawie danych JSON. Liczby przepisuj z danych, nie licz szacunków od siebie.
            - 2–6 zdań albo krótka lista od „- ”. Zacznij od bezpośredniej odpowiedzi.
            - Gdy danych brakuje (pole null albo brak pola), napisz wprost, czego nie wiadomo.
            - Nie wymyślaj nazw studiów spoza danych. Bez emoji, bez nagłówków markdown i pogrubień.
            - Słowniczek pól: klienci (teraz), klienci_30_dni_temu, aktywni_30 (klienci z treningiem w 30 dni), wizyty_7 (ostatni tydzień),
              wizyty_7_wczesniej (tydzień przed), odwolane_7, nieobecnosci_7, zaplanowane_7 (najbliższy tydzień), przychod_30_zl (wpłaty klientów w aplikacji),
              treningi_30 (zapisane treningi), wiadomosci_30, plany_treningowe, abonament_zl (miesięcznie dla platformy), ryzyko (0–100 pkt odejścia).
            """;
        var user = $"""
            Pytanie: {question}
            Dane (JSON): {json}
            """;
        var result = await ai.CompleteAsync("leo-pytanie", null, system, user, maxTokens: 600);
        if (result.Text is null)
            return preset is null
                ? new("Nie udało się zapytać AI — wybierz gotowe pytanie z listy.", false, result.Error, [])
                : AnswerPreset(preset, o) with { Error = result.Error };
        var text = string.Join('\n', result.Text.Replace("\r", "").Split('\n').Select(l => l.Trim().Replace("**", "")).Where(l => l.Length > 0 && !l.StartsWith('#')));
        if (text.Length > 2000) text = text[..2000] + "…";
        return new(text, true, null, AssistantService.UnverifiedNumbers(text, json));
    }

    /// <summary>Paczka danych dla AI: nazwy studiów i liczby — bez imion, e-maili, telefonów i adresów właścicieli.</summary>
    public static object Pack(Overview o) => new
    {
        studia = o.Studios.OrderByDescending(s => s.Clients).Take(40).Select(s => new
        {
            nazwa = s.Name, plan = s.PlanName, abonament_zl = s.MonthlyValue, placi = s.Paying,
            klienci = s.Clients, limit_klientow = s.ClientLimit == 0 ? (int?)null : s.ClientLimit, klienci_30_dni_temu = s.Clients30Ago,
            aktywni_30 = s.ActiveClients30, wizyty_7 = s.Sessions7, wizyty_7_wczesniej = s.Sessions7Prev, odwolane_7 = s.Cancelled7,
            nieobecnosci_7 = s.NoShow7, zaplanowane_7 = s.Upcoming7, przychod_30_zl = s.Revenue30, treningi_30 = s.Workouts30,
            wiadomosci_30 = s.Messages30, plany_treningowe = s.Plans, ryzyko = s.RiskScore, powody_ryzyka = s.RiskReasons.Take(3),
        }),
        ostrzezenia = o.Warnings.Take(15).Select(w => new { studio = w.Studio.Name, w.Title, w.Detail }),
        segmenty = o.Segments.ToDictionary(s => s.Title, s => s.Items.Select(i => $"{i.Studio.Name}: {i.Note}").Take(10)),
        pieniadze_zagrozone = new { miesiecznie_zl = o.Money.MonthlyAtRisk, oczekiwana_strata_zl = o.Money.ExpectedMonthly, rocznie_zl = o.Money.YearlyAtRisk },
        glos_klientow = o.Voice.Topics.Take(8).Select(t => new { temat = t.Label, wzmianki = t.Mentions }),
    };

    // ── Szkice e-maili (administrator zawsze czyta przed wysłaniem) ──

    /// <summary>Jedna wiadomość do studia, nawet gdy ostrzeżeń jest kilka.</summary>
    public Task<AssistantService.Draft> WarningDraftAsync(IReadOnlyList<Warning> warnings)
    {
        var facts = new Dictionary<string, string>
        {
            ["co_widac"] = string.Join(" ", warnings.Take(4).Select(w => $"{w.Title}. {w.Detail}")),
            ["sugestia"] = warnings[0].Action,
        };
        var seen = string.Join("\n", warnings.Take(4).Select(w => $"- {Lower(w.Title)} ({Lower(w.Detail.TrimEnd('.'))})"));
        var body = $"""
            Cześć!

            Zaglądamy, czy wszystko gra — w ostatnich dniach w aplikacji widać zmiany:
            {seen}

            Może to po prostu urlop albo sezon, ale jeśli coś przeszkadza w pracy, chętnie pomożemy. {warnings[0].Action}
            Wolisz pogadać przez telefon? Daj znać, zadzwonimy.
            """;
        return assistant.DraftReplyAsync("ostrzezenie",
            "W aplikacji trenera widać nietypowe zmiany aktywności. Napisz życzliwą, krótką wiadomość: zapytaj, czy wszystko w porządku, i zaproponuj jedną konkretną pomoc z sugestii. Nie strasz, nie pisz o „monitorowaniu”.",
            facts, "Czy wszystko w porządku?", body);
    }

    public Task<AssistantService.Draft> UpgradeDraftAsync(Studio s, Plan? next)
    {
        var facts = new Dictionary<string, string>
        {
            ["klienci"] = s.Clients.ToString(), ["limit_klientow"] = s.ClientLimit.ToString(), ["obecny_plan"] = s.PlanName,
        };
        if (next is not null)
        {
            facts["wyzszy_plan"] = next.Name;
            facts["limit_w_wyzszym_planie"] = ClientLimit(next) == 0 ? "bez limitu" : next.MaxClients.ToString();
            facts["cena_wyzszego_planu_zl"] = next.MonthlyPrice.ToString("0.##", Pl);
        }
        var nextText = next is null ? "wyższy plan" : $"plan {next.Name} ({(ClientLimit(next) == 0 ? "bez limitu klientów" : $"do {next.MaxClients} klientów")}, {Zl(next.MonthlyPrice)} miesięcznie)";
        var body = $"""
            Cześć!

            Gratulacje — masz już {s.Clients} z {s.ClientLimit} klientów w planie {s.PlanName}. Świetnie Ci idzie! 🎉
            Żeby limit nie zatrzymał nowych zapisów, zerknij na {nextText}. Zmiana zajmuje chwilę i nic nie ginie.

            Masz pytania? Odpisz, pomożemy.
            """;
        return assistant.DraftReplyAsync("wyzszy_plan",
            "Trener zbliża się do limitu klientów w swoim planie. Napisz krótką, życzliwą wiadomość: pogratuluj rozwoju i zaproponuj wyższy plan z faktów, bez nacisku. Nie obiecuj rabatów.",
            facts, "Zbliżasz się do limitu klientów", body);
    }

    public Task<AssistantService.Draft> SegmentDraftAsync(Segment segment, SegmentItem item) => segment.Key switch
    {
        Growing => assistant.DraftReplyAsync("rosnie",
            "Trenerowi dobrze idzie (więcej klientów lub wizyt). Napisz krótką wiadomość z gratulacjami i delikatną prośbą o polecenie platformy innym trenerom (jest program poleceń). Bez nacisku.",
            new() { ["co_widac"] = item.Note }, "Gratulacje — świetny miesiąc!",
            $"""
            Cześć!

            Widzimy, że u Ciebie sporo się dzieje: {Lower(item.Note)}. Gratulacje! 💪
            Znasz innych trenerów, którym aplikacja by pomogła? Z programem poleceń oboje dostajecie miesiąc gratis — link znajdziesz w aplikacji.
            """),
        _ => assistant.DraftReplyAsync("uspiony",
            "Trener przestał korzystać z aplikacji (brak logowań lub wizyt). Napisz krótką, życzliwą wiadomość z jedną praktyczną wskazówką i propozycją pomocy. Bez wyrzutów.",
            new() { ["co_widac"] = item.Note }, "Jak możemy pomóc?",
            $"""
            Cześć!

            Dawno Cię u nas nie było — wszystko gra? Jeśli coś przeszkadza albo czegoś brakuje, napisz śmiało.
            Mały trik: wizyty powtarzalne („tak samo za tydzień”) układają grafik na tygodnie do przodu jednym kliknięciem.

            Chcesz, żebyśmy pokazali to w 10 minut przez telefon? Daj znać.
            """),
    };

    // ── Pomocnicze ──

    private static string Display(string company, string slug) => string.IsNullOrWhiteSpace(company) ? slug : company.Trim();
    public static string Zl(decimal v) => v.ToString("#,0.##", Pl) + " zł";
    private static int Pct(int part, int total) => total == 0 ? 0 : (int)Math.Round(part * 100.0 / total);
    private static string Lower(string s) => s.Length == 0 ? s : char.ToLower(s[0], Pl) + s[1..];
    private static string Change(int now, int prev) => prev == 0 ? "" : now >= prev ? $"+{Pct(now - prev, prev)}%" : $"−{Pct(prev - now, prev)}%";
    private static string Plural(int n, string one, string few, string many) => LeoService.Plural(n, one, few, many);
}
