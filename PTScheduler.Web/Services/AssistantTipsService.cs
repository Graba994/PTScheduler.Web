using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

/// <summary>
/// Podpowiedzi asystenta-maskotki dla trenera — liczone regułami z danych aplikacji (bez AI):
/// rezerwacje do akceptacji, nieopłacone treningi, kończące się pakiety, faktury na koniec miesiąca,
/// kolejne 10 treningów klienta, klienci, którzy dawno nie byli, rekordowy tydzień i plan na dziś.
/// AI (przez Portal) najwyżej przeredaguje tekst zapasowy; liczby zawsze pochodzą stąd.
/// </summary>
public class AssistantTipsService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock, RetentionService retention,
    IEmailSettingsService emailSettings, TrainerProposalsService proposals, IBusinessReportService reports)
{
    /// <summary>Przychód 30 dni vs poprzednie 30 — raport jest ciężki, więc trzymamy wynik godzinę.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, decimal Now, decimal Before)> RevenueCache = new();

    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <param name="Key">Klucz do zamykania („nie pokazuj dziś”); przy <paramref name="Once"/> zamknięcie jest na stałe.</param>
    /// <param name="Placeholders">Znaczniki podmieniane po stronie aplikacji (imiona klientów nie trafiają do AI).</param>
    /// <param name="Quiet">Cicha propozycja: nie otwiera dymka sama, nie liczy się w kropce, można ją wyłączyć na stałe.</param>
    public sealed record Tip(string Key, string Kind, string Mood, int Priority, string Fallback,
        Dictionary<string, string> Facts, string? ActionLabel, string? ActionHref, bool Once = false,
        Dictionary<string, string>? Placeholders = null, bool Quiet = false);

    /// <summary>Najwyżej tyle cichych propozycji dziennie — reszta poczeka (asystent ma podpowiadać, nie zasypywać).</summary>
    public const int QuietPerDay = 2;

    public async Task<List<Tip>> GetTipsAsync(string userId, bool studioWide)
    {
        var now = clock.LocalNow;
        var today = now.Date;
        var tips = new List<Tip>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var sessions = db.Sessions.AsNoTracking().Where(s => studioWide || s.TrainerUserId == userId);
        var clients = db.Clients.AsNoTracking().Where(c => studioWide || c.TrainerUserId == userId);

        // Najbliższy własny trening w ciągu godziny — karta „przed treningiem”.
        var soon = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.Status == SessionStatus.Scheduled && s.StartTime > now && s.StartTime <= now.AddMinutes(60))
            .OrderBy(s => s.StartTime)
            .Select(s => new { s.Id, s.ClientId, s.StartTime, s.Client.FirstName, s.Client.LastName })
            .FirstOrDefaultAsync();
        if (soon is not null)
        {
            var minutes = Math.Max(1, (int)Math.Round((soon.StartTime - now).TotalMinutes));
            tips.Add(new($"before-{soon.Id}", "przed_treningiem", "hello", 100,
                $"Za {minutes} min: [klient]. Zerknij na profil — plan na dziś, ostatni trening i uwagi.",
                new() { ["klient"] = "[klient]", ["minuty"] = minutes.ToString() }, "Karta", $"/clients/{soon.ClientId}?trening={soon.Id}#trening",
                Placeholders: new() { ["[klient]"] = ShortName(soon.FirstName, soon.LastName, "kolejny trening") }));
        }

        // Własna skrzynka nie działa — maile idą awaryjnie przez serwer platformy (widzi właściciel studia, który ma ustawienia poczty).
        if (studioWide)
        {
            var mail = await emailSettings.GetAsync();
            if (mail.IsEnabled && mail.OwnFailing(clock.UtcNow))
                tips.Add(new($"mail-{mail.LastErrorAt:yyyyMMddHH}", "poczta_blad", "remind", 98,
                    mail.FallbackCount > 0
                        ? $"Twoja skrzynka nie wysyła maili — {mail.FallbackCount} {Plural(mail.FallbackCount, "wiadomość wyszła", "wiadomości wyszły", "wiadomości wyszło")} awaryjnie przez serwer platformy. Sprawdź hasło w ustawieniach poczty."
                        : "Twoja skrzynka nie wysyła maili — sprawdź hasło w ustawieniach poczty. Do tego czasu maile idą przez serwer platformy.",
                    new() { ["awaryjne_wysylki"] = mail.FallbackCount.ToString() }, "Ustawienia poczty", "/admin/email"));
        }

        // Rezerwacje „zapłacę u trenera” czekające na decyzję.
        var approvals = await sessions.CountAsync(s => s.AwaitingApproval && s.Status == SessionStatus.Scheduled && s.StartTime >= now);
        if (approvals > 0)
            tips.Add(new("approvals", "akceptacje", "remind", 95,
                $"{approvals} {Plural(approvals, "rezerwacja czeka", "rezerwacje czekają", "rezerwacji czeka")} na Twoją akceptację.",
                new() { ["liczba_rezerwacji"] = approvals.ToString() }, "Zobacz", "/sessions"));

        // Treningi poza pakietem z płatnością u trenera — odbyte, a nieopłacone.
        var since = now.AddDays(-60);
        var unpaid = await sessions.CountAsync(s => s.OffPackagePayment == "trainer" && s.PaidAt == null
            && s.Status == SessionStatus.Completed && s.StartTime >= since);
        if (unpaid > 0)
            tips.Add(new("unpaid", "niezaplacone", "remind", 90,
                $"{unpaid} {Plural(unpaid, "trening poza pakietem czeka", "treningi poza pakietem czekają", "treningów poza pakietem czeka")} na zapłatę. Warto przypomnieć klientom.",
                new() { ["liczba_treningow"] = unpaid.ToString() }, "Zobacz", "/sessions"));

        // Faktury na koniec miesiąca (właściciel studia z włączonym KSeF).
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        if (studioWide && now.Day >= daysInMonth - 2 && await db.FinanceTaxConfigs.AnyAsync(c => c.KsefEnabled))
        {
            var month = now.ToString("MMMM", Pl);
            tips.Add(new($"invoices-{now:yyyy-MM}", "faktury", "remind", 70,
                $"Koniec miesiąca! Sprawdź, czy wszystkie faktury za {month} są wystawione.",
                new() { ["miesiac"] = month }, "Finanse", "/finance"));
        }

        // Pakiety, w których zostało najwyżej jedno wejście.
        var endingIds = await db.SessionPackages.AsNoTracking()
            .Where(p => p.Status == PackageStatus.Active && !p.IsHidden && p.TotalSessions > 0
                && p.TotalSessions - p.UsedSessions <= 1 && (studioWide || p.Client.TrainerUserId == userId))
            .Select(p => new { p.ClientId, p.Client.FirstName, p.Client.LastName }).Distinct().Take(50).ToListAsync();
        var ending = endingIds.Select(e => e.ClientId).Distinct().Count();
        if (ending == 1)
            tips.Add(new($"packages-{endingIds[0].ClientId}", "pakiety", "think", 55,
                "[klient] kończy się pakiet — dobry moment, żeby zaproponować kolejny. Przygotuję wiadomość w Twoim stylu.",
                new() { ["klient"] = "[klient]" }, "Napisz", $"/clients/{endingIds[0].ClientId}?szkic=Package",
                Placeholders: new() { ["[klient]"] = ShortName(endingIds[0].FirstName, endingIds[0].LastName, "Klientowi") }));
        else if (ending > 1)
            tips.Add(new("packages", "pakiety", "think", 55,
                $"{ending} {Plural(ending, "klientowi kończy", "klientom kończy", "klientom kończy")} się pakiet — dobry moment, żeby zaproponować kolejny.",
                new() { ["liczba_klientow"] = ending.ToString() }, "Klienci", "/clients"));

        // Kolejne 10 treningów klienta — świętujemy najwyżej 3 dni (karta na pulpicie i tak pokazuje to raz).
        var weekAgo = now.AddDays(-3);
        var milestones = await sessions.Where(s => s.Status == SessionStatus.Completed && s.StartTime <= now)
            .GroupBy(s => s.ClientId)
            .Select(g => new { ClientId = g.Key, Total = g.Count(), Before = g.Count(s => s.StartTime < weekAgo) })
            .Where(x => x.Total >= 10 && x.Total > x.Before)
            .ToListAsync();
        var reached = milestones.Where(m => m.Total / 10 > m.Before / 10)
            .Select(m => (m.ClientId, Count: m.Total / 10 * 10)).OrderByDescending(m => m.Count).Take(2).ToList();
        if (reached.Count > 0)
        {
            var ids = reached.Select(r => r.ClientId).ToList();
            var names = await db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });
            foreach (var (clientId, count) in reached)
            {
                if (!names.TryGetValue(clientId, out var name) || string.IsNullOrWhiteSpace(name.FirstName)) continue;
                tips.Add(new($"milestone-{clientId}-{count}", "kamien_milowy", "celebrate", 50,
                    $"[klient] ma za sobą już {count} treningów! 🎉",
                    new() { ["klient"] = "[klient]", ["liczba_treningow"] = count.ToString() },
                    "Profil", $"/clients/{clientId}", Once: true,
                    Placeholders: new() { ["[klient]"] = ShortName(name.FirstName, name.LastName, "Klient") }));
            }
        }

        // Klienci, którzy mogą odejść (reguły retencji); bez nich — prostsza reguła „dawno nie byli”.
        var risks = await retention.GetRisksAsync(studioWide ? null : userId);
        var highRisk = risks.Count(r => r.Result.IsHigh);
        if (risks.Count > 0)
            tips.Add(new($"retention-{today:yyyy-MM-dd}", "ryzyko_odejscia", "think", 45,
                highRisk > 0
                    ? $"{risks.Count} {Plural(risks.Count, "klient może", "klientów może", "klientów może")} odejść (w tym {highRisk} z wysokim ryzykiem). Krótka wiadomość często wystarczy — przygotuję szkic w Twoim stylu."
                    : $"{risks.Count} {Plural(risks.Count, "klient może", "klientów może", "klientów może")} odejść. Krótka wiadomość często wystarczy — przygotuję szkic w Twoim stylu.",
                highRisk > 0
                    ? new() { ["liczba_klientow"] = risks.Count.ToString(), ["wysokie_ryzyko"] = highRisk.ToString() }
                    : new() { ["liczba_klientow"] = risks.Count.ToString() },
                "Zobacz", "/trainer/retencja"));
        else
        {
            var from = now.AddDays(-90);
            var to = now.AddDays(-21);
            var inactive = await clients.Where(c => c.Status == ClientStatus.Active)
                .Select(c => new
                {
                    Last = db.Sessions.Where(s => s.ClientId == c.Id && s.Status != SessionStatus.Cancelled).Max(s => (DateTime?)s.StartTime)
                })
                .CountAsync(x => x.Last != null && x.Last >= from && x.Last <= to);
            if (inactive > 0)
                tips.Add(new("inactive", "nieaktywni", "think", 40,
                    $"{inactive} {Plural(inactive, "osoba nie była", "osoby nie były", "osób nie było")} na treningu od ponad 3 tygodni. Może krótka wiadomość?",
                    new() { ["liczba_osob"] = inactive.ToString(), ["dni"] = "21" }, "Klienci", "/clients"));
        }

        // Rekordowy tydzień (od poniedziałku) na tle 8 poprzednich.
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var history = await sessions.Where(s => s.Status == SessionStatus.Completed && s.StartTime >= weekStart.AddDays(-56) && s.StartTime <= now)
            .Select(s => s.StartTime).ToListAsync();
        var thisWeek = history.Count(d => d >= weekStart);
        var best = Enumerable.Range(1, 8).Select(w => history.Count(d => d >= weekStart.AddDays(-7 * w) && d < weekStart.AddDays(-7 * (w - 1)))).Max();
        if (thisWeek >= 5 && best > 0 && thisWeek > best)
            tips.Add(new($"record-{weekStart:yyyy-MM-dd}", "rekord_tygodnia", "celebrate", 30,
                $"Rekordowy tydzień: {thisWeek} {Plural(thisWeek, "trening", "treningi", "treningów")}! Poprzedni rekord to {best}. Tak trzymać 💪",
                new() { ["treningi_w_tym_tygodniu"] = thisWeek.ToString(), ["poprzedni_rekord"] = best.ToString() }, null, null, Once: true));

        // „Stonks”: przychód z ostatnich 30 dni wyraźnie wyższy niż w poprzednich 30 (najwyżej raz na pół miesiąca).
        try
        {
            var (revenue, before) = await RevenueAsync(userId, studioWide);
            if (before >= 300 && revenue >= before * 1.15m && revenue - before >= 200)
            {
                var pct = (int)Math.Round((revenue - before) / before * 100);
                tips.Add(new($"stonks-{today:yyyy-MM}-{(today.Day <= 15 ? 1 : 2)}", "przychod_w_gore", "celebrate", 35,
                    $"Stonks! 📈 Przychód z ostatnich 30 dni: {revenue.ToString("N0", Pl)} zł — o {pct}% więcej niż miesiąc wcześniej.",
                    new() { ["przychod_30_dni"] = revenue.ToString("0", Pl), ["poprzednie_30_dni"] = before.ToString("0", Pl), ["wzrost_procent"] = pct.ToString() },
                    "Raport", "/stats/business", Once: true));
            }
        }
        catch (Exception) { /* raport jest dodatkiem — bez niego podpowiedzi działają */ }

        // Plan na dziś albo pusty grafik na jutro.
        var tomorrow = today.AddDays(1);
        var todayCount = await sessions.CountAsync(s => s.Status == SessionStatus.Scheduled && s.StartTime >= now && s.StartTime < tomorrow);
        var greeting = now.Hour < 18 ? "Dzień dobry!" : "Dobry wieczór!";
        if (todayCount > 0)
            tips.Add(new($"today-{today:yyyy-MM-dd}", "dzis", "hello", 10,
                $"{greeting} Dziś jeszcze {todayCount} {Plural(todayCount, "trening", "treningi", "treningów")} w grafiku. Powodzenia!",
                new() { ["treningi_dzis"] = todayCount.ToString() }, "Grafik", "/calendar"));
        else if (!await sessions.AnyAsync(s => s.Status == SessionStatus.Scheduled && s.StartTime >= tomorrow && s.StartTime < tomorrow.AddDays(1)))
            tips.Add(new($"empty-{tomorrow:yyyy-MM-dd}", "pusty_grafik", "hello", 10,
                "Jutro grafik jest jeszcze pusty. Może podeślesz klientom link do zapisów?",
                [], "Grafik", "/calendar"));

        // Ciche propozycje (bez wyłączonych przez trenera), najwyżej dwie dziennie — najważniejsze pierwsze.
        try
        {
            var muted = await GetMutedAsync(userId);
            var quiet = (await proposals.GetAsync(userId, studioWide, risks.Select(r => r.ClientId).ToHashSet()))
                .Where(t => !muted.Contains(t.Kind)).OrderByDescending(t => t.Priority).Take(QuietPerDay).ToList();
            if (quiet.Any(t => t.Kind == "zaleglosci")) tips.RemoveAll(t => t.Kind == "niezaplacone");
            if (quiet.Any(t => t.Kind == "okienko")) tips.RemoveAll(t => t.Kind == "pusty_grafik");
            tips.AddRange(quiet);
        }
        catch (Exception) { /* propozycje są dodatkiem */ }

        return tips.OrderByDescending(t => t.Priority).ToList();
    }

    private async Task<(decimal Now, decimal Before)> RevenueAsync(string userId, bool studioWide)
    {
        var key = studioWide ? "*" : userId;
        if (RevenueCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromHours(1)) return (hit.Now, hit.Before);
        var r = await reports.GetAsync(30, studioWide ? null : userId);
        RevenueCache[key] = (DateTime.UtcNow, r.Revenue, r.RevenuePrevious);
        return (r.Revenue, r.RevenuePrevious);
    }

    /// <summary>Rodzaje propozycji wyłączone przez trenera („Nie proponuj tego”).</summary>
    public async Task<HashSet<string>> GetMutedAsync(string userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var raw = await db.TrainerConfigs.AsNoTracking().Where(c => c.TrainerUserId == userId).Select(c => c.MutedHints).FirstOrDefaultAsync();
        return (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
    }

    public async Task SetMutedAsync(string userId, string kind, bool muted)
    {
        if (!TrainerProposalsService.Kinds.Any(k => k.Kind == kind)) return;
        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.TrainerConfigs.FirstOrDefaultAsync(c => c.TrainerUserId == userId);
        if (cfg is null) db.TrainerConfigs.Add(cfg = new PTScheduler.Domain.Entities.TrainerConfig { TrainerUserId = userId });
        var set = (cfg.MutedHints ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        if (muted) set.Add(kind); else set.Remove(kind);
        cfg.MutedHints = set.Count == 0 ? null : string.Join(",", set.Order());
        await db.SaveChangesAsync();
    }

    /// <summary>Polska odmiana: 1 trening, 2–4 treningi, 5+ treningów (12–14 zawsze w trzeciej formie).</summary>
    /// <summary>Imię z inicjałem nazwiska („Hubert K.”) — dwie osoby o tym samym imieniu nie mylą się w podpowiedziach.</summary>
    public static string ShortName(string? first, string? last, string fallback)
    {
        if (string.IsNullOrWhiteSpace(first)) return fallback;
        var initial = string.IsNullOrWhiteSpace(last) ? "" : $" {char.ToUpperInvariant(last.Trim()[0])}.";
        return first.Trim() + initial;
    }

    public static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
