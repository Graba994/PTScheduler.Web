using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Scheduling;

/// <summary>
/// Reguły cichych propozycji asystenta dla trenera (bez AI): rytm klienta, nieobecności, plan bez progresu,
/// kamienie milowe, przeciążenie i obłożenie grafiku. Same liczby — teksty i hamulce są w aplikacji.
/// </summary>
public static class TrainerNudges
{
    /// <summary>Mediana odstępów (w dniach) między odbytymi treningami; null przy mniej niż 4 treningach.</summary>
    public static double? MedianGapDays(IEnumerable<DateTime> completed)
    {
        var days = completed.Select(d => d.Date).Distinct().OrderBy(d => d).ToList();
        if (days.Count < 4) return null;
        var gaps = days.Zip(days.Skip(1), (a, b) => (b - a).TotalDays).OrderBy(g => g).ToList();
        var mid = gaps.Count / 2;
        return gaps.Count % 2 == 1 ? gaps[mid] : (gaps[mid - 1] + gaps[mid]) / 2;
    }

    /// <summary>
    /// „Cisza”: klient zwykle wraca co <c>gap</c> dni, a od ostatniej wizyty minęło wyraźnie więcej (co najmniej 10 dni
    /// i 1,6× zwykłego odstępu, ale nie więcej niż 60 — dalej to już sprawa dla retencji), bez nic zaplanowanego.
    /// </summary>
    public static bool IsUnusuallyQuiet(double? gap, int daysSince, bool hasUpcoming) =>
        gap is { } g && !hasUpcoming && daysSince >= Math.Max(10, Math.Ceiling(g * 1.6)) && daysSince <= 60;

    /// <summary>Ile nieobecności w ostatnich 4 wizytach (od najnowszej); odwołane się nie liczą.</summary>
    public static int RecentNoShows(IEnumerable<SessionStatus> pastNewestFirst) =>
        pastNewestFirst.Where(s => s is SessionStatus.Completed or SessionStatus.NoShow).Take(4).Count(s => s == SessionStatus.NoShow);

    public static readonly int[] Milestones = [100, 250, 500, 1000, 2000, 5000];

    /// <summary>Kamień milowy przekroczony między <paramref name="before"/> a <paramref name="now"/> (największy), albo null.</summary>
    public static int? CrossedMilestone(int before, int now) =>
        Milestones.Where(m => before < m && now >= m).Select(m => (int?)m).LastOrDefault();

    /// <summary>
    /// Przeciążenie: w tym tygodniu co najmniej 25 wizyt i o 25% więcej niż zwykle (średnia poprzednich tygodni),
    /// a do tego zajęte wieczory (wizyta od 18:00) w co najmniej 5 dni.
    /// </summary>
    public static bool IsOverloaded(int thisWeek, IReadOnlyList<int> previousWeeks, int busyEvenings)
    {
        var usual = previousWeeks.Count == 0 ? 0 : previousWeeks.Average();
        return thisWeek >= 25 && thisWeek >= usual * 1.25 && busyEvenings >= 5;
    }

    /// <summary>Minuty dostępności w tygodniu od <paramref name="weekStart"/> (reguły cykliczne i jednorazowe).</summary>
    public static int AvailableMinutes(IEnumerable<TrainerAvailabilityDto> rules, DateOnly weekStart)
    {
        var total = 0;
        foreach (var r in rules.Where(r => r.IsActive))
        {
            for (var d = 0; d < 7; d++)
            {
                var day = weekStart.AddDays(d);
                var applies = r.SpecificDate is { } sd ? sd == day
                    : r.DayOfWeek == day.DayOfWeek && (r.ValidFrom is null || r.ValidFrom <= day) && (r.ValidUntil is null || r.ValidUntil >= day);
                if (applies && r.EndTime > r.StartTime) total += (int)(r.EndTime - r.StartTime).TotalMinutes;
            }
        }
        return total;
    }

    /// <summary>Obłożenie tygodnia: minuty wizyt / minuty dostępności (0, gdy brak dostępności).</summary>
    public static double Occupancy(int bookedMinutes, int availableMinutes) =>
        availableMinutes <= 0 ? 0 : Math.Min(1.5, bookedMinutes / (double)availableMinutes);

    /// <summary>Podpowiedź o cenie: każdy z co najmniej 6 ostatnich tygodni zajęty w 90% lub więcej.</summary>
    public static bool SuggestPriceReview(IReadOnlyList<double> weeklyOccupancy) =>
        weeklyOccupancy.Count >= 6 && weeklyOccupancy.All(o => o >= 0.9);

    /// <summary>
    /// Plan bez progresu: ten sam dzień planu w co najmniej 6 kolejnych treningach, a najlepszy ciężar w żadnym
    /// ćwiczeniu nie wzrósł (porównanie pierwszej i drugiej połowy tych treningów). Bez zapisanych ciężarów — null.
    /// </summary>
    public static bool? IsStalled(IReadOnlyList<IReadOnlyDictionary<int, decimal>> topKgPerWorkoutOldestFirst)
    {
        if (topKgPerWorkoutOldestFirst.Count < 6) return null;
        var half = topKgPerWorkoutOldestFirst.Count / 2;
        var first = Best(topKgPerWorkoutOldestFirst.Take(half));
        var second = Best(topKgPerWorkoutOldestFirst.Skip(half));
        var common = first.Keys.Intersect(second.Keys).Where(k => first[k] > 0).ToList();
        if (common.Count == 0) return null;
        return common.All(k => second[k] <= first[k]);

        static Dictionary<int, decimal> Best(IEnumerable<IReadOnlyDictionary<int, decimal>> workouts) =>
            workouts.SelectMany(w => w).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Max(x => x.Value));
    }

    /// <summary>Poniedziałek tygodnia, w którym jest <paramref name="day"/>.</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
}
