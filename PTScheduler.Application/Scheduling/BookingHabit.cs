using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Scheduling;

/// <summary>
/// Stały termin klienta wyczytany z ostatnich wizyt (np. „wtorek 18:00”) — do podpowiedzi przy rezerwacji.
/// Bez AI: najczęstsza para dzień tygodnia + godzina, jeśli powtórzyła się co najmniej dwa razy.
/// </summary>
public sealed record BookingHabit(DayOfWeek Day, TimeOnly Time, int SessionTypeId, int Count)
{
    public static BookingHabit? Detect(IEnumerable<(DateTime Start, int SessionTypeId, SessionStatus Status)> recent, int take = 10)
    {
        var list = recent
            .Where(s => s.Status != SessionStatus.Cancelled)
            .OrderByDescending(s => s.Start)
            .Take(take)
            .ToList();
        var best = list
            .GroupBy(s => (s.Start.DayOfWeek, TimeOnly.FromDateTime(s.Start)))
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(x => x.Start), Type = g.OrderByDescending(x => x.Start).First().SessionTypeId })
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last)
            .FirstOrDefault();
        return best is { Count: >= 2 } ? new BookingHabit(best.Key.DayOfWeek, best.Key.Item2, best.Type, best.Count) : null;
    }

    /// <summary>Najbliższy taki termin po <paramref name="now"/> (dziś, jeśli godzina jeszcze nie minęła).</summary>
    public DateTime NextAfter(DateTime now)
    {
        var days = ((int)Day - (int)now.DayOfWeek + 7) % 7;
        var candidate = DateOnly.FromDateTime(now).AddDays(days).ToDateTime(Time);
        return candidate > now ? candidate : candidate.AddDays(7);
    }
}
