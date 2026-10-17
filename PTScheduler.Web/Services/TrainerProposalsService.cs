using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using Tip = PTScheduler.Web.Services.AssistantTipsService.Tip;

namespace PTScheduler.Web.Services;

/// <summary>
/// Ciche propozycje asystenta: pomysły, nie sprawy do załatwienia. Nie otwierają dymka same, nie liczą się
/// w kropce przy maskotce, dziennie widać najwyżej dwie, a każdy rodzaj można wyłączyć („Nie proponuj tego”).
/// Liczą je reguły (<see cref="TrainerNudges"/>, <see cref="FreeSlots"/>); imiona klientów nie trafiają do AI.
/// </summary>
public class TrainerProposalsService(IDbContextFactory<ApplicationDbContext> dbFactory, IAppClock clock,
    SlotInviteService slots, ITrainerAvailabilityService availability, ILogger<TrainerProposalsService> logger)
{
    /// <summary>Rodzaje cichych propozycji — do ustawień („co asystent może proponować”).</summary>
    public static readonly (string Kind, string Label)[] Kinds =
    [
        ("okienko", "Wolne okienka i kogo zaprosić"),
        ("nieobecnosci", "Dodatkowe przypomnienie po nieobecnościach"),
        ("cisza", "Osoba, która zwykle trenuje regularnie, a teraz ma długą przerwę"),
        ("postep", "Rekord klienta — moment na zdjęcie postępów lub opinię"),
        ("plan_stoi", "Plan bez progresu — czas na nowy blok"),
        ("tydzien", "Twój tydzień w poniedziałek"),
        ("cena", "Pełny grafik od tygodni — przemyśl cenę"),
        ("zaleglosci", "Podsumowanie zaległości"),
        ("przeciazenie", "Bardzo pracowity tydzień"),
        ("kamien_trenera", "Twoje kamienie milowe"),
    ];

    private static readonly ConcurrentDictionary<string, (DateTime At, List<Tip> Tips)> Cache = new();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    /// <summary>Po zmianach (np. wysłane zaproszenia, włączone przypomnienie) — następne odczytanie policzy od nowa.</summary>
    public static void Invalidate(string userId)
    {
        foreach (var key in Cache.Keys.Where(k => k.StartsWith(userId + "|"))) Cache.TryRemove(key, out _);
    }

    /// <summary>Czy klient ma dodatkowe przypomnienie 2 h przed wizytą (null — brak dostępu).</summary>
    public async Task<bool?> GetExtraReminderAsync(string userId, bool isAdmin, int clientId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Clients.AsNoTracking().Where(c => c.Id == clientId && (isAdmin || c.TrainerUserId == userId))
            .Select(c => (bool?)c.ExtraReminder).FirstOrDefaultAsync();
    }

    public async Task<bool> SetExtraReminderAsync(string userId, bool isAdmin, int clientId, bool on)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId && (isAdmin || c.TrainerUserId == userId));
        if (client is null) return false;
        client.ExtraReminder = on;
        await db.SaveChangesAsync();
        Invalidate(userId);
        return true;
    }

    public async Task<List<Tip>> GetAsync(string userId, bool studioWide, IReadOnlySet<int> retentionClientIds)
    {
        var key = $"{userId}|{studioWide}|{clock.Today:yyyyMMdd}";
        if (Cache.TryGetValue(key, out var hit) && clock.UtcNow - hit.At < CacheFor) return hit.Tips;
        var tips = new List<Tip>();
        foreach (var step in new Func<Task<IEnumerable<Tip>>>[]
                 {
                     () => SlotsAsync(userId), () => NoShowsAsync(userId), () => QuietClientsAsync(userId, retentionClientIds),
                     () => RecordsAsync(userId), () => StalledPlansAsync(userId), () => WeekAsync(userId), () => PriceAsync(userId),
                     () => DebtsAsync(userId, studioWide), () => OverloadAsync(userId), () => MilestonesAsync(userId),
                 })
        {
            // Każda propozycja osobno — błąd jednej nie gasi pozostałych.
            try { tips.AddRange(await step()); }
            catch (Exception ex) { logger.LogDebug(ex, "Propozycja asystenta nie policzyła się."); }
        }
        Cache[key] = (clock.UtcNow, tips);
        if (Cache.Count > 500) Cache.Clear();
        return tips;
    }

    private static Dictionary<string, string> Name(string? first) =>
        new() { ["[klient]"] = string.IsNullOrWhiteSpace(first) ? "Klient" : first.Trim() };

    // ── Wolne okienka ──
    private async Task<IEnumerable<Tip>> SlotsAsync(string userId)
    {
        var list = await slots.GetAsync(userId, days: 2, maxPeople: 5);
        var best = list.OrderBy(o => o.Window.Start).FirstOrDefault();
        if (best is null) return [];
        var n = best.People.Count;
        return
        [
            new($"okienko-{best.Window.Start:yyyyMMddHHmm}", "okienko", "think", 47,
                $"Masz {best.Label.Replace("dziś", "dziś wolne").Replace("jutro", "jutro wolne")}. {n} {AssistantTipsService.Plural(n, "osoba zwykle trenuje", "osoby zwykle trenują", "osób zwykle trenuje")} o tej porze — napisać do {(n == 1 ? "niej" : "nich")}?",
                new() { ["okienko"] = best.Label, ["liczba_osob"] = n.ToString() }, "Zaproś", "/okienka", Quiet: true),
        ];
    }

    // ── Nieobecności → dodatkowe przypomnienie ──
    private async Task<IEnumerable<Tip>> NoShowsAsync(string userId)
    {
        var now = clock.LocalNow;
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = now.AddDays(-60);
        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= since && s.StartTime < now && !s.Client.ExtraReminder
                && (s.Status == SessionStatus.Completed || s.Status == SessionStatus.NoShow))
            .Select(s => new { s.ClientId, s.StartTime, s.Status, s.Client.FirstName })
            .ToListAsync();
        var upcoming = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= now && s.StartTime < now.AddDays(14) && s.Status == SessionStatus.Scheduled)
            .Select(s => s.ClientId).Distinct().ToListAsync();
        var pick = rows.GroupBy(r => r.ClientId)
            .Where(g => upcoming.Contains(g.Key))
            .Select(g => new { g.Key, g.First().FirstName, Missed = TrainerNudges.RecentNoShows(g.OrderByDescending(x => x.StartTime).Select(x => x.Status)) })
            .Where(x => x.Missed >= 2).OrderByDescending(x => x.Missed).FirstOrDefault();
        if (pick is null) return [];
        return
        [
            new($"noshow-{pick.Key}", "nieobecnosci", "think", 46,
                $"[klient]: {pick.Missed} {AssistantTipsService.Plural(pick.Missed, "nieobecność", "nieobecności", "nieobecności")} na ostatnich 4 wizytach. Włączyć dodatkowe przypomnienie 2 godziny przed wizytą — tylko dla tej osoby?",
                new() { ["klient"] = "[klient]", ["nieobecnosci"] = pick.Missed.ToString() }, "Włącz", $"/clients/{pick.Key}?przypomnienie=1",
                Placeholders: Name(pick.FirstName), Quiet: true),
        ];
    }

    // ── Cisza po treningu (rytm klienta) ──
    private async Task<IEnumerable<Tip>> QuietClientsAsync(string userId, IReadOnlySet<int> retention)
    {
        var now = clock.LocalNow;
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = now.AddDays(-150);
        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= since && s.StartTime < now && s.Status == SessionStatus.Completed
                && s.Client.Status == ClientStatus.Active)
            .Select(s => new { s.ClientId, s.StartTime, s.Client.FirstName }).ToListAsync();
        var upcoming = (await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= now && (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.AwaitingPackage))
            .Select(s => s.ClientId).Distinct().ToListAsync()).ToHashSet();
        var best = rows.GroupBy(r => r.ClientId).Where(g => !retention.Contains(g.Key))
            .Select(g =>
            {
                var gap = TrainerNudges.MedianGapDays(g.Select(x => x.StartTime));
                var since = (now.Date - g.Max(x => x.StartTime).Date).Days;
                return new { g.Key, g.First().FirstName, Gap = gap, Since = since, Quiet = TrainerNudges.IsUnusuallyQuiet(gap, since, upcoming.Contains(g.Key)) };
            })
            .Where(x => x.Quiet).OrderByDescending(x => x.Since / Math.Max(1, x.Gap ?? 1)).FirstOrDefault();
        if (best is null) return [];
        var gapDays = (int)Math.Round(best.Gap ?? 7);
        return
        [
            new($"cisza-{best.Key}-{TrainerNudges.WeekStart(clock.Today):yyyyMMdd}", "cisza", "think", 44,
                $"[klient] zwykle trenuje co {gapDays} {AssistantTipsService.Plural(gapDays, "dzień", "dni", "dni")}, a od ostatniej wizyty minęło {best.Since}. Krótka wiadomość? Przygotuję szkic w Twoim stylu.",
                new() { ["klient"] = "[klient]", ["zwykle_co_dni"] = gapDays.ToString(), ["dni_od_wizyty"] = best.Since.ToString() },
                "Napisz", $"/clients/{best.Key}?szkic=Comeback", Placeholders: Name(best.FirstName), Quiet: true),
        ];
    }

    // ── Rekord klienta ──
    private async Task<IEnumerable<Tip>> RecordsAsync(string userId)
    {
        var today = clock.Today;
        var weekAgo = today.AddDays(-7);
        await using var db = await dbFactory.CreateDbContextAsync();
        var recent = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.WorkoutDate >= weekAgo && l.Client!.TrainerUserId == userId)
            .Select(l => new { l.ClientId, l.ExerciseId, Name = l.Exercise!.NamePl, l.Client!.FirstName, Top = l.Sets.Max(s => (decimal?)s.WeightKg) ?? 0 })
            .Where(x => x.Top > 0).ToListAsync();
        if (recent.Count == 0) return [];
        var pairs = recent.Select(r => new { r.ClientId, r.ExerciseId }).Distinct().ToList();
        var clientIds = pairs.Select(p => p.ClientId).Distinct().ToList();
        var exerciseIds = pairs.Select(p => p.ExerciseId).Distinct().ToList();
        var before = await db.WorkoutLogs.AsNoTracking()
            .Where(l => l.WorkoutDate < weekAgo && clientIds.Contains(l.ClientId) && exerciseIds.Contains(l.ExerciseId))
            .GroupBy(l => new { l.ClientId, l.ExerciseId })
            .Select(g => new { g.Key.ClientId, g.Key.ExerciseId, Max = g.SelectMany(l => l.Sets).Max(s => (decimal?)s.WeightKg) ?? 0 })
            .ToListAsync();
        var best = recent.GroupBy(r => new { r.ClientId, r.ExerciseId })
            .Select(g =>
            {
                var top = g.Max(x => x.Top);
                var prev = before.FirstOrDefault(b => b.ClientId == g.Key.ClientId && b.ExerciseId == g.Key.ExerciseId)?.Max ?? 0;
                return new { g.Key.ClientId, g.First().Name, g.First().FirstName, Top = top, Prev = prev, Gain = prev > 0 ? (top - prev) / prev : 0 };
            })
            .Where(x => x.Prev > 0 && x.Gain >= 0.05m).OrderByDescending(x => x.Gain).FirstOrDefault();
        if (best is null) return [];
        return
        [
            new($"postep-{best.ClientId}-{TrainerNudges.WeekStart(today):yyyyMMdd}", "postep", "celebrate", 42,
                $"[klient] bije rekord: {best.Name} {best.Top:0.##} kg (było {best.Prev:0.##}). Dobry moment na zdjęcie postępów albo prośbę o opinię Google.",
                new() { ["klient"] = "[klient]", ["cwiczenie"] = best.Name, ["kg"] = best.Top.ToString("0.##"), ["bylo_kg"] = best.Prev.ToString("0.##") },
                "Profil", $"/clients/{best.ClientId}", Placeholders: Name(best.FirstName), Quiet: true),
        ];
    }

    // ── Plan bez progresu ──
    private async Task<IEnumerable<Tip>> StalledPlansAsync(string userId)
    {
        var now = clock.LocalNow;
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = now.AddDays(-120);
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= since && s.StartTime < now && s.Status == SessionStatus.Completed)
            .OrderByDescending(s => s.StartTime)
            .Select(s => new { s.ClientId, s.StartTime, s.PlanDayId, s.Client.FirstName, Label = s.PlanDay != null ? s.PlanDay.Label : null, Order = s.PlanDay != null ? s.PlanDay.Order : 0, PlanId = s.PlanDay != null ? s.PlanDay.PlanId : 0 })
            .ToListAsync();
        foreach (var g in sessions.GroupBy(s => s.ClientId))
        {
            var streak = g.TakeWhile(s => s.PlanDayId is not null && s.PlanDayId == g.First().PlanDayId).ToList();
            if (streak.Count < 6) continue;
            var dates = streak.Select(s => DateOnly.FromDateTime(s.StartTime)).ToList();
            var logs = await db.WorkoutLogs.AsNoTracking()
                .Where(l => l.ClientId == g.Key && dates.Contains(l.WorkoutDate))
                .Select(l => new { l.WorkoutDate, l.ExerciseId, Top = l.Sets.Max(s => (decimal?)s.WeightKg) ?? 0 }).ToListAsync();
            var perWorkout = dates.OrderBy(d => d)
                .Select(d => (IReadOnlyDictionary<int, decimal>)logs.Where(l => l.WorkoutDate == d).GroupBy(l => l.ExerciseId).ToDictionary(x => x.Key, x => x.Max(y => y.Top)))
                .Where(d => d.Count > 0).ToList();
            if (TrainerNudges.IsStalled(perWorkout) != true) continue;
            var first = streak[0];
            var day = string.IsNullOrWhiteSpace(first.Label) ? $"Dzień {first.Order + 1}" : first.Label;
            return
            [
                new($"plan-{g.Key}-{first.PlanDayId}", "plan_stoi", "think", 40,
                    $"[klient] robi „{day}” już {streak.Count} razy z rzędu, a ciężary stoją w miejscu. Może czas na nowy blok?",
                    new() { ["klient"] = "[klient]", ["dzien_planu"] = day, ["razy"] = streak.Count.ToString() },
                    "Plan", $"/plans/edit/{first.PlanId}", Once: true, Placeholders: Name(first.FirstName), Quiet: true),
            ];
        }
        return [];
    }

    // ── Twój tydzień (poniedziałek i wtorek) ──
    private async Task<IEnumerable<Tip>> WeekAsync(string userId)
    {
        var today = clock.Today;
        if (today.DayOfWeek is not (DayOfWeek.Monday or DayOfWeek.Tuesday)) return [];
        var thisWeek = TrainerNudges.WeekStart(today);
        var last = thisWeek.AddDays(-7).ToDateTime(TimeOnly.MinValue);
        var prev = thisWeek.AddDays(-14).ToDateTime(TimeOnly.MinValue);
        var end = thisWeek.ToDateTime(TimeOnly.MinValue);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= prev && s.StartTime < end)
            .Select(s => new { s.StartTime, s.Status, s.ClientId }).ToListAsync();
        var lastWeek = rows.Where(r => r.StartTime >= last).ToList();
        var done = lastWeek.Count(r => r.Status == SessionStatus.Completed);
        if (done == 0) return [];
        var before = rows.Count(r => r.StartTime < last && r.Status == SessionStatus.Completed);
        var noShows = lastWeek.Count(r => r.Status == SessionStatus.NoShow);
        var people = lastWeek.Where(r => r.Status == SessionStatus.Completed).Select(r => r.ClientId).Distinct().Count();
        var lastUtc = DateTime.SpecifyKind(last, DateTimeKind.Utc).AddHours(-2);
        var endUtc = DateTime.SpecifyKind(end, DateTimeKind.Utc);
        var newClients = await db.Clients.CountAsync(c => c.TrainerUserId == userId && c.CreatedAt >= lastUtc && c.CreatedAt < endUtc);
        var diff = done - before;
        var text = $"Twój tydzień: {done} {AssistantTipsService.Plural(done, "wizyta", "wizyty", "wizyt")}"
            + (before > 0 && diff != 0 ? $" ({(diff > 0 ? "+" : "")}{diff} względem poprzedniego)" : "")
            + $", {people} {AssistantTipsService.Plural(people, "osoba", "osoby", "osób")}"
            + (noShows > 0 ? $", {noShows} {AssistantTipsService.Plural(noShows, "nieobecność", "nieobecności", "nieobecności")}" : ", bez nieobecności")
            + (newClients > 0 ? $", {newClients} {AssistantTipsService.Plural(newClients, "nowa osoba", "nowe osoby", "nowych osób")}" : "") + ".";
        return
        [
            new($"tydzien-{thisWeek:yyyyMMdd}", "tydzien", diff >= 0 ? "celebrate" : "hello", 38, text,
                new() { ["wizyty"] = done.ToString(), ["zmiana"] = diff.ToString(), ["osoby"] = people.ToString(), ["nieobecnosci"] = noShows.ToString(), ["nowi"] = newClients.ToString() },
                "Raport", "/stats/business", Once: true, Quiet: true),
        ];
    }

    // ── Pełny grafik → cena ──
    private async Task<IEnumerable<Tip>> PriceAsync(string userId)
    {
        var today = clock.Today;
        var rules = await availability.GetAvailabilityRulesAsync(userId);
        if (rules.Count == 0) return [];
        var thisWeek = TrainerNudges.WeekStart(today);
        var from = thisWeek.AddDays(-42).ToDateTime(TimeOnly.MinValue);
        var to = thisWeek.ToDateTime(TimeOnly.MinValue);
        await using var db = await dbFactory.CreateDbContextAsync();
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= from && s.StartTime < to && s.Status != SessionStatus.Cancelled)
            .Select(s => new { s.StartTime, s.SessionType.DurationMinutes }).ToListAsync();
        var weekly = Enumerable.Range(1, 6).Select(w =>
        {
            var start = thisWeek.AddDays(-7 * w);
            var a = start.ToDateTime(TimeOnly.MinValue);
            var booked = sessions.Where(s => s.StartTime >= a && s.StartTime < a.AddDays(7)).Sum(s => s.DurationMinutes);
            return TrainerNudges.Occupancy(booked, TrainerNudges.AvailableMinutes(rules, start));
        }).ToList();
        if (!TrainerNudges.SuggestPriceReview(weekly)) return [];
        var pct = (int)Math.Round(weekly.Min() * 100);
        return
        [
            new($"cena-{today:yyyyMM}", "cena", "think", 36,
                $"Od 6 tygodni masz zajęte co najmniej {pct}% godzin. Wielu trenerów w takiej sytuacji podnosi cenę dla nowych klientów — decyzja należy do Ciebie.",
                new() { ["tygodnie"] = "6", ["oblozenie_procent"] = pct.ToString() }, "Cennik", "/admin/package-offers", Once: true, Quiet: true),
        ];
    }

    // ── Zaległości (raz w tygodniu) ──
    private async Task<IEnumerable<Tip>> DebtsAsync(string userId, bool studioWide)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var debts = await PTScheduler.Infrastructure.Services.DebtService.LoadAsync(db, clock.LocalNow, studioWide ? null : userId, null);
        if (debts.Count < 2) return [];
        var total = debts.Sum(d => d.Total);
        var week = TrainerNudges.WeekStart(clock.Today);
        return
        [
            new($"zaleglosci-{week:yyyyMMdd}", "zaleglosci", "think", 49,
                total > 0
                    ? $"{debts.Count} {AssistantTipsService.Plural(debts.Count, "osoba zalega", "osoby zalegają", "osób zalega")} razem {total:N0} zł. Przypomnieć uprzejmie, w Twoim stylu?"
                    : $"{debts.Count} {AssistantTipsService.Plural(debts.Count, "osoba ma", "osoby mają", "osób ma")} nieopłacone treningi lub pakiety. Przypomnieć uprzejmie, w Twoim stylu?",
                new() { ["liczba_osob"] = debts.Count.ToString(), ["kwota"] = total.ToString("N0") }, "Zaległości", "/zaleglosci", Quiet: true),
        ];
    }

    // ── Przeciążenie ──
    private async Task<IEnumerable<Tip>> OverloadAsync(string userId)
    {
        var today = clock.Today;
        var week = TrainerNudges.WeekStart(today);
        var from = week.AddDays(-56).ToDateTime(TimeOnly.MinValue);
        var to = week.AddDays(7).ToDateTime(TimeOnly.MinValue);
        await using var db = await dbFactory.CreateDbContextAsync();
        var starts = await db.Sessions.AsNoTracking()
            .Where(s => s.TrainerUserId == userId && s.StartTime >= from && s.StartTime < to && s.Status != SessionStatus.Cancelled)
            .Select(s => s.StartTime).ToListAsync();
        var weekStart = week.ToDateTime(TimeOnly.MinValue);
        var current = starts.Where(s => s >= weekStart).ToList();
        var previous = Enumerable.Range(1, 8).Select(w => starts.Count(s => s >= weekStart.AddDays(-7 * w) && s < weekStart.AddDays(-7 * (w - 1)))).Where(c => c > 0).ToList();
        var evenings = current.Where(s => s.Hour >= 18).Select(s => s.Date).Distinct().Count();
        if (!TrainerNudges.IsOverloaded(current.Count, previous, evenings)) return [];
        return
        [
            new($"przeciazenie-{week:yyyyMMdd}", "przeciazenie", "think", 35,
                $"W tym tygodniu masz {current.Count} wizyt i {evenings} zajętych wieczorów. Zaplanuj sobie chwilę wolnego — regeneracja trenera też się liczy.",
                new() { ["wizyty"] = current.Count.ToString(), ["wieczory"] = evenings.ToString() }, "Dostępność", "/trainer/availability", Once: true, Quiet: true),
        ];
    }

    // ── Kamienie milowe trenera ──
    private async Task<IEnumerable<Tip>> MilestonesAsync(string userId)
    {
        var now = clock.LocalNow;
        var weekAgo = now.AddDays(-7);
        await using var db = await dbFactory.CreateDbContextAsync();
        var done = db.Sessions.AsNoTracking().Where(s => s.TrainerUserId == userId && s.Status == SessionStatus.Completed && s.StartTime <= now);
        var total = await done.CountAsync();
        var before = await done.CountAsync(s => s.StartTime < weekAgo);
        var tips = new List<Tip>();
        if (TrainerNudges.CrossedMilestone(before, total) is { } m)
            tips.Add(new($"trener-{m}", "kamien_trenera", "celebrate", 34, $"Twoja {m}. wizyta w aplikacji! Tak trzymaj 🎉",
                new() { ["wizyty"] = m.ToString() }, null, null, Once: true, Quiet: true));
        var first = await done.MinAsync(s => (DateTime?)s.StartTime);
        if (first is { } f)
        {
            var years = now.Year - f.Year;
            var anniversary = f.AddYears(years);
            if (years >= 1 && anniversary <= now && anniversary > weekAgo)
                tips.Add(new($"rocznica-{years}", "kamien_trenera", "celebrate", 34,
                    years == 1 ? "Rok z aplikacją! Dzięki, że prowadzisz tu swoich klientów 🎉" : $"{years} lata z aplikacją! Dzięki, że prowadzisz tu swoich klientów 🎉",
                    new() { ["lata"] = years.ToString() }, null, null, Once: true, Quiet: true));
        }
        return tips;
    }
}
