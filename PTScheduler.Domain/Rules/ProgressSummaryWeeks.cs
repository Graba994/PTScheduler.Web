namespace PTScheduler.Domain.Rules;

/// <summary>Seria tygodni z treningiem — do pochwał w „Twoich postępach” („5 tygodni z rzędu”).</summary>
public static class ProgressSummaryWeeks
{
    /// <summary>
    /// Tygodnie (od poniedziałku) z co najmniej jednym treningiem, liczone wstecz bez przerwy.
    /// Bieżący tydzień bez treningu nie zrywa serii — jeszcze trwa.
    /// </summary>
    public static int Streak(IEnumerable<DateOnly> trainingDays, DateOnly today)
    {
        var weeks = trainingDays.Select(WeekStart).ToHashSet();
        var week = WeekStart(today);
        if (!weeks.Contains(week)) week = week.AddDays(-7);
        var streak = 0;
        while (weeks.Contains(week)) { streak++; week = week.AddDays(-7); }
        return streak;
    }

    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
