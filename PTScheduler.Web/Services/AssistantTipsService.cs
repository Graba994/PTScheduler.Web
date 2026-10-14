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
public class AssistantTipsService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock)
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <param name="Key">Klucz do zamykania („nie pokazuj dziś”); przy <paramref name="Once"/> zamknięcie jest na stałe.</param>
    /// <param name="Placeholders">Znaczniki podmieniane po stronie aplikacji (imiona klientów nie trafiają do AI).</param>
    public sealed record Tip(string Key, string Kind, string Mood, int Priority, string Fallback,
        Dictionary<string, string> Facts, string? ActionLabel, string? ActionHref, bool Once = false,
        Dictionary<string, string>? Placeholders = null);

    public async Task<List<Tip>> GetTipsAsync(string userId, bool studioWide)
    {
        var now = clock.LocalNow;
        var today = now.Date;
        var tips = new List<Tip>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var sessions = db.Sessions.AsNoTracking().Where(s => studioWide || s.TrainerUserId == userId);
        var clients = db.Clients.AsNoTracking().Where(c => studioWide || c.TrainerUserId == userId);

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
        var ending = await db.SessionPackages.AsNoTracking()
            .Where(p => p.Status == PackageStatus.Active && !p.IsHidden && p.TotalSessions > 0
                && p.TotalSessions - p.UsedSessions <= 1 && (studioWide || p.Client.TrainerUserId == userId))
            .Select(p => p.ClientId).Distinct().CountAsync();
        if (ending > 0)
            tips.Add(new("packages", "pakiety", "think", 55,
                $"{ending} {Plural(ending, "klientowi kończy", "klientom kończy", "klientom kończy")} się pakiet — dobry moment, żeby zaproponować kolejny.",
                new() { ["liczba_klientow"] = ending.ToString() }, "Klienci", "/clients"));

        // Kolejne 10 treningów klienta w ostatnim tygodniu.
        var weekAgo = now.AddDays(-7);
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
            var names = await db.Clients.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName);
            foreach (var (clientId, count) in reached)
            {
                if (!names.TryGetValue(clientId, out var name) || string.IsNullOrWhiteSpace(name)) continue;
                tips.Add(new($"milestone-{clientId}-{count}", "kamien_milowy", "celebrate", 50,
                    $"[klient] ma za sobą już {count} treningów! 🎉",
                    new() { ["klient"] = "[klient]", ["liczba_treningow"] = count.ToString() },
                    "Profil", $"/clients/{clientId}", Once: true,
                    Placeholders: new() { ["[klient]"] = name.Trim() }));
            }
        }

        // Aktywni klienci bez treningu od 3 tygodni (i bez zaplanowanego).
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

        return tips.OrderByDescending(t => t.Priority).ToList();
    }

    /// <summary>Polska odmiana: 1 trening, 2–4 treningi, 5+ treningów (12–14 zawsze w trzeciej formie).</summary>
    public static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
