using PTScheduler.Domain.Entities;

namespace PTScheduler.Domain.Rules;

/// <summary>Obliczenia raportów biznesowych trenera — czyste funkcje, łatwe do przetestowania.</summary>
public static class BusinessReportRules
{
    /// <summary>
    /// Minuty pracy trenera w danym dniu według godzin pracy (reguły cotygodniowe + jednorazowe).
    /// Nakładające się przedziały liczymy raz.
    /// </summary>
    public static int AvailableMinutes(IEnumerable<TrainerAvailability> rules, DateOnly date)
    {
        var intervals = rules
            .Where(r => r.IsActive && r.EndTime > r.StartTime)
            .Where(r => r.SpecificDate is { } d
                ? d == date
                : r.DayOfWeek == date.DayOfWeek
                  && (r.ValidFrom is null || r.ValidFrom <= date)
                  && (r.ValidUntil is null || r.ValidUntil >= date))
            .Select(r => (Start: r.StartTime.ToTimeSpan().TotalMinutes, End: r.EndTime.ToTimeSpan().TotalMinutes))
            .OrderBy(i => i.Start)
            .ToList();

        double total = 0, curStart = -1, curEnd = -1;
        foreach (var (start, end) in intervals)
        {
            if (start > curEnd)
            {
                if (curEnd > curStart) total += curEnd - curStart;
                curStart = start;
                curEnd = end;
            }
            else if (end > curEnd) curEnd = end;
        }
        if (curEnd > curStart) total += curEnd - curStart;
        return (int)total;
    }

    /// <summary>Procent (0–100) z zaokrągleniem; null, gdy nie ma podstawy.</summary>
    public static int? Percent(double part, double whole) =>
        whole <= 0 ? null : (int)Math.Round(Math.Clamp(part / whole, 0, 1) * 100);

    /// <summary>
    /// Retencja: jaki odsetek klientów aktywnych w poprzednim okresie trenował także w bieżącym.
    /// </summary>
    public static int? Retention(IReadOnlyCollection<int> previousActive, IReadOnlySet<int> currentActive) =>
        Percent(previousActive.Count(currentActive.Contains), previousActive.Count);

    /// <summary>
    /// Jak często klient kupuje kolejny pakiet: z pakietów, które się skończyły (wykorzystane albo wygasłe),
    /// jaki odsetek ma następny pakiet tego samego klienta kupiony później.
    /// </summary>
    public static int? RepurchaseRate(IEnumerable<(int ClientId, DateTime PurchasedAt, bool Finished)> packages)
    {
        var list = packages.ToList();
        var finished = list.Where(p => p.Finished).ToList();
        if (finished.Count == 0) return null;
        var bought = finished.Count(f => list.Any(p => p.ClientId == f.ClientId && p.PurchasedAt > f.PurchasedAt));
        return Percent(bought, finished.Count);
    }
}
