using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Wnioski Leo na żądanie (przycisk „Wygeneruj”): ankieta dla trenerów, spotkania, oceny aplikacji.
/// Kod liczy zestawienia (procenty, mediany, kategorie), AI pisze z nich punkty; bez AI — wnioski z reguł.
/// Do AI nie trafiają e-maile, telefony ani imiona — tylko liczby i wolne odpowiedzi bez danych kontaktowych.
/// Ostatni wynik zapisujemy w ustawieniach, żeby był widoczny po odświeżeniu strony.
/// </summary>
public partial class LeoInsights(IDbContextFactory<PortalDbContext> dbFactory, AssistantService assistant, SiteSettingsService settings)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public const string Survey = "survey";
    public const string Meetings = "meetings";
    public const string Feedback = "feedback";

    private static string SettingKey(string kind) => kind switch
    {
        Survey => K.LeoInsightSurvey,
        Meetings => K.LeoInsightMeetings,
        _ => K.LeoInsightFeedback,
    };

    public async Task<AssistantService.Insight?> LoadAsync(string kind)
    {
        var json = await settings.GetAsync(SettingKey(kind));
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<AssistantService.Insight>(json); }
        catch (JsonException) { return null; }
    }

    private async Task<AssistantService.Insight> SaveAsync(string kind, AssistantService.Insight insight)
    {
        await settings.SetAsync(SettingKey(kind), JsonSerializer.Serialize(insight));
        return insight;
    }

    // ── Ankieta dla trenerów ──

    public async Task<AssistantService.Insight> SurveyAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.TrainerSurveyResponses.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToListAsync();
        if (all.Count == 0) return await SaveAsync(Survey, Empty("Brak odpowiedzi w ankiecie — najpierw roześlij link (zakładka „Jak rozesłać”)."));

        var n = all.Count;
        var questions = TrainerSurvey.Questions.Select(q => new
        {
            pytanie = q.Text,
            wielokrotny_wybor = q.Multi,
            odpowiedzi = q.Options
                .Select(o => (Option: o, Count: all.Count(r => (TrainerSurvey.Get(r, q.Key) ?? "").Split(TrainerSurvey.Separator).Contains(o))))
                .OrderByDescending(x => x.Count)
                .ToDictionary(x => x.Option, x => $"{Pct(x.Count, n)}% ({x.Count})"),
        }).ToList();
        var wishes = all.Where(r => !string.IsNullOrWhiteSpace(r.Wish)).Select(r => Scrub(r.Wish!, 300)).Take(40).ToList();
        var sources = all.Where(r => r.Source is not null).GroupBy(r => r.Source!).OrderByDescending(g => g.Count()).Take(8)
            .ToDictionary(g => g.Key, g => g.Count());
        var data = new { liczba_odpowiedzi = n, pytania = questions, czego_brakuje = wishes, zrodla = sources };

        var lines = new List<string>();
        if (n < 15) lines.Add($"- Na razie tylko {n} {Plural(n, "odpowiedź", "odpowiedzi", "odpowiedzi")} — traktuj wyniki jako wskazówkę, nie pewnik.");
        foreach (var q in TrainerSurvey.Questions)
        {
            var top = q.Options.Select(o => (o, c: all.Count(r => (TrainerSurvey.Get(r, q.Key) ?? "").Split(TrainerSurvey.Separator).Contains(o))))
                .OrderByDescending(x => x.c).Take(q.Multi ? 2 : 1).Where(x => x.c > 0).ToList();
            if (top.Count > 0)
                lines.Add($"- {q.Text} Najczęściej: {string.Join(", ", top.Select(t => $"„{t.o}” ({Pct(t.c, n)}%)"))}.");
        }
        if (wishes.Count > 0) lines.Add($"- {wishes.Count} {Plural(wishes.Count, "osoba napisała", "osoby napisały", "osób napisało")}, czego brakuje — przeczytaj je poniżej.");
        lines.Add("Co zrobić:");
        lines.Add("- Pokaż w demo rozwiązanie najczęściej wskazywanego problemu jako pierwsze.");
        lines.Add("- Porównaj rozsądną cenę z ankiety z cennikiem planów.");

        return await SaveAsync(Survey, await assistant.AnalyzeAsync("ankieta",
            "Podsumuj wyniki ankiety wśród trenerów personalnych: jak pracują, co ich boli, ile płacą i ile zapłaciliby. Wskaż, co z tego wynika dla aplikacji dla trenerów (funkcje do wyeksponowania, cena, komunikacja) i które grupy (źródła) odpowiadają najchętniej.",
            data, string.Join('\n', lines)));
    }

    // ── Spotkania z trenerami ──

    public async Task<AssistantService.Insight> MeetingsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.TrainerMeetings.AsNoTracking().OrderByDescending(m => m.CreatedAt).ToListAsync();
        if (all.Count == 0) return await SaveAsync(Meetings, Empty("Brak notatek ze spotkań — dodaj pierwsze w zakładce „Nowe spotkanie”."));

        var n = all.Count;
        var committed = all.Count(m => m.Committed);
        var checkedRecall = all.Where(m => m.NameRecalled is not null).ToList();
        var recalled = checkedRecall.Count(m => m.NameRecalled == true);
        var picks = all.Where(m => !string.IsNullOrWhiteSpace(m.NamePick)).GroupBy(m => m.NamePick!.Trim())
            .OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => $"{Pct(g.Count(), n)}% ({g.Count()} z {n})");
        var booking = all.SelectMany(m => (m.BookingToday ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .GroupBy(b => b).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => $"{Pct(g.Count(), n)}% ({g.Count()} z {n})");
        var clients = all.Where(m => m.Clients is not null).Select(m => m.Clients!.Value).ToList();
        var noShows = all.Where(m => m.NoShowsPerMonth is not null).Select(m => m.NoShowsPerMonth!.Value).ToList();
        var prices = new Dictionary<string, string>();
        foreach (var (label, values) in new[]
        {
            ("za tanio, by uwierzyć", all.Select(m => m.PriceTooCheap)),
            ("okazja", all.Select(m => m.PriceBargain)),
            ("drogo, ale rozważy", all.Select(m => m.PriceExpensive)),
            ("za drogo", all.Select(m => m.PriceTooExpensive)),
        })
        {
            var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
            if (list.Count > 0) prices[label] = $"mediana {Median(list)} zł ({list.Count} odp.)";
        }
        var data = new
        {
            liczba_spotkan = n,
            zgoda_na_start = $"{Pct(committed, n)}% ({committed})",
            test_pamieci_nazwy = checkedRecall.Count > 0 ? $"pamięta {Pct(recalled, checkedRecall.Count)}% ({recalled} z {checkedRecall.Count} sprawdzonych)" : "jeszcze nie sprawdzano",
            wybrana_nazwa = picks,
            jak_zapisuja_dzis = booking,
            klienci_mediana = clients.Count > 0 ? Median(clients).ToString() : "brak danych",
            nieobecnosci_miesiecznie_mediana = noShows.Count > 0 ? Median(noShows).ToString() : "brak danych",
            ceny_zl_miesiecznie = prices,
            bolaczki = all.Where(m => !string.IsNullOrWhiteSpace(m.Pains)).Select(m => Scrub(m.Pains!, 300)).Take(30).ToList(),
            obserwacje_z_demo = all.Where(m => !string.IsNullOrWhiteSpace(m.DemoNotes)).Select(m => Scrub(m.DemoNotes!, 300)).Take(30).ToList(),
            notatki = all.Where(m => !string.IsNullOrWhiteSpace(m.Notes)).Select(m => Scrub(m.Notes!, 300)).Take(20).ToList(),
        };

        var lines = new List<string>();
        if (n < 8) lines.Add($"- Na razie {n} {Plural(n, "spotkanie", "spotkania", "spotkań")} — za mało na pewne wnioski, ale kierunek już widać.");
        lines.Add($"- Zgoda na start z kodem: {Pct(committed, n)}% ({committed} z {n}).");
        if (checkedRecall.Count > 0) lines.Add($"- Nazwę następnego dnia pamięta {Pct(recalled, checkedRecall.Count)}% sprawdzonych ({recalled} z {checkedRecall.Count}).");
        if (picks.Count > 0) lines.Add($"- Najczęściej wybierana nazwa: „{picks.First().Key}” — {picks.First().Value}.");
        if (booking.Count > 0) lines.Add($"- Dziś zapisują głównie przez: {string.Join(", ", booking.Take(2).Select(b => $"{b.Key} — {b.Value}"))}.");
        if (prices.Count > 0) lines.Add($"- Ceny miesięcznie: {string.Join("; ", prices.Select(p => $"{p.Key} — {p.Value}"))}.");
        lines.Add("Co zrobić:");
        if (checkedRecall.Count < n) lines.Add($"- Sprawdź pamięć nazwy u {n - checkedRecall.Count} {Plural(n - checkedRecall.Count, "osoby", "osób", "osób")} (SMS następnego dnia).");
        lines.Add("- Ustaw cenę planu między medianą „okazja” a „drogo, ale rozważy”.");

        return await SaveAsync(Meetings, await assistant.AnalyzeAsync("spotkania",
            "Podsumuj notatki ze spotkań z trenerami personalnymi: test nazwy (zapamiętanie, wybór), jak dziś zapisują klientów, bolączki, obserwacje z demo kreatora i odpowiedzi o cenę (pytania Van Westendorpa). Wskaż, co poprawić w produkcie i w rozmowie oraz jaka cena wygląda rozsądnie.",
            data, string.Join('\n', lines)));
    }

    // ── Oceny aplikacji ──

    public async Task<AssistantService.Insight> FeedbackAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var all = await db.AppFeedbacks.AsNoTracking().OrderByDescending(f => f.CreatedAt).Take(120).ToListAsync();
        if (all.Count == 0) return await SaveAsync(Feedback, Empty("Brak ocen aplikacji — trenerzy zobaczą prośbę o ocenę na swoim pulpicie."));

        var n = all.Count;
        var withText = all.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
        var categorized = withText.Select(f => new { ocena = f.Rating, kategoria = AssistantService.Categorize(f.Text!), tresc = Scrub(f.Text!, 400) }).Take(60).ToList();
        var categories = categorized.GroupBy(c => c.kategoria).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
        var avg = all.Average(f => f.Rating);
        var data = new
        {
            liczba_ocen = n,
            srednia = avg.ToString("0.0", Pl),
            rozklad = Enumerable.Range(1, 5).Reverse().ToDictionary(s => $"{s} gwiazdek", s => all.Count(f => f.Rating == s)),
            kategorie_wstepne = categories,
            opinie = categorized,
        };

        var lines = new List<string>
        {
            $"- Średnia ocena {avg.ToString("0.0", Pl)} z {n} {Plural(n, "oceny", "ocen", "ocen")}; z komentarzem: {withText.Count}.",
        };
        foreach (var (cat, count) in categories.Take(4)) lines.Add($"- {Capitalize(cat)}: {count}.");
        var low = all.Count(f => f.Rating <= 2);
        if (low > 0) lines.Add($"- Niskich ocen (1–2): {low} — odpowiedz na nie w pierwszej kolejności.");
        lines.Add("Co zrobić:");
        lines.Add("- Odpowiedz na opinie z kontaktem — przycisk „Szkic odpowiedzi” przy każdej.");
        if (categories.ContainsKey("błąd")) lines.Add("- Przejrzyj zgłoszone błędy i daj znać autorom, gdy będą poprawione.");

        return await SaveAsync(Feedback, await assistant.AnalyzeAsync("opinie",
            "Pogrupuj oceny aplikacji od trenerów w tematy (błędy, prośby o funkcje, ceny, łatwość użycia, pochwały). Wstępne kategorie z reguł mogą być błędne — popraw je, jeśli treść wskazuje inaczej. Wskaż najczęstsze tematy, najpilniejsze problemy i co zrobić najpierw.",
            data, string.Join('\n', lines)));
    }

    // ── Pomocnicze ──

    private static AssistantService.Insight Empty(string text) => new(text, false, null, [], DateTime.UtcNow);

    private static int Pct(int part, int total) => total == 0 ? 0 : (int)Math.Round(part * 100.0 / total);

    private static int Median(List<int> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    /// <summary>Usuwa e-maile i numery telefonów z wolnych odpowiedzi i skraca tekst.</summary>
    public static string Scrub(string text, int max)
    {
        var t = Email().Replace(text, "[e-mail]");
        t = Phone().Replace(t, "[telefon]");
        t = t.Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Pl) + s[1..];

    private static string Plural(int n, string one, string few, string many) => LeoService.Plural(n, one, few, many);

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")] private static partial Regex Email();
    [GeneratedRegex(@"(?<!\d)(?:\+?48[ -]?)?\d{3}[ -]?\d{3}[ -]?\d{3}(?!\d)")] private static partial Regex Phone();
}
