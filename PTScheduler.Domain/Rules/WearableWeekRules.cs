using System.Globalization;
using PTScheduler.Domain.Entities;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Podsumowania z zegarka dla klienta i trenera: jedna linijka po treningu dopiętym do wizyty i raport poprzedniego
/// tygodnia (poniedziałek–niedziela) z porównaniem do tygodnia wcześniej. Same liczby z reguł — bez AI.
/// </summary>
public static class WearableWeekRules
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Aktywność w raporcie: dzień (czas lokalny), czas trwania i czy pokryła się z wizytą.</summary>
    public sealed record Act(DateOnly Date, int DurationSeconds, bool AtSession);

    public sealed record Week(DateOnly From, int DaysWithData, int? AvgSleepMinutes, int? PrevAvgSleepMinutes,
        int StepsTotal, int? AvgSteps, int? AvgRestingHr, int? PrevAvgRestingHr, int? AvgHrv, int? PrevAvgHrv,
        int Activities, int ActiveMinutes, int SessionsWithWatch)
    {
        public DateOnly To => From.AddDays(6);
        public bool HasData => DaysWithData > 0 || Activities > 0;
    }

    /// <summary>Poniedziałek ostatniego pełnego tygodnia (w poniedziałek — tydzień, który właśnie się skończył).</summary>
    public static DateOnly LastFullWeekStart(DateOnly today)
    {
        var sinceMonday = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-sinceMonday - 7);
    }

    public static Week For(DateOnly from, IReadOnlyCollection<WearableDay> days, IReadOnlyCollection<Act> acts)
    {
        var to = from.AddDays(7);
        var prevFrom = from.AddDays(-7);
        var week = days.Where(d => d.Date >= from && d.Date < to).ToList();
        var prev = days.Where(d => d.Date >= prevFrom && d.Date < from).ToList();
        var weekActs = acts.Where(a => a.Date >= from && a.Date < to).ToList();
        var steps = week.Where(d => d.Steps is > 0).Select(d => d.Steps!.Value).ToList();
        return new Week(from,
            week.Count(d => d.Steps is > 0 || d.SleepMinutes is > 0 || d.RestingHeartRate is > 0),
            Avg(week.Select(d => d.SleepMinutes)), Avg(prev.Select(d => d.SleepMinutes)),
            steps.Sum(), steps.Count == 0 ? null : (int)Math.Round(steps.Average()),
            Avg(week.Select(d => d.RestingHeartRate)), Avg(prev.Select(d => d.RestingHeartRate)),
            Avg(week.Select(d => d.HrvLastNight)), Avg(prev.Select(d => d.HrvLastNight)),
            weekActs.Count, (int)Math.Round(weekActs.Sum(a => a.DurationSeconds) / 60.0), weekActs.Count(a => a.AtSession));
    }

    /// <summary>„Sen śr. 7 h 05 min (+20 min) · 52 300 kroków · 4 aktywności (3 h 10 min) · tętno spocz. 58 (−2)”.</summary>
    public static string Text(Week w)
    {
        var parts = new List<string>();
        if (w.AvgSleepMinutes is int s)
            parts.Add($"Sen śr. {ReadinessRules.Duration(s)}{Delta(s, w.PrevAvgSleepMinutes, d => $"{Math.Abs(d)} min", 10)}");
        if (w.StepsTotal > 0) parts.Add($"{w.StepsTotal.ToString("N0", Pl)} kroków");
        if (w.Activities > 0)
            parts.Add($"{w.Activities} {(w.Activities == 1 ? "aktywność" : "aktywności")} ({ReadinessRules.Duration(w.ActiveMinutes)})");
        if (w.AvgRestingHr is int r) parts.Add($"tętno spocz. {r}{Delta(r, w.PrevAvgRestingHr, d => Math.Abs(d).ToString(Pl), 1)}");
        return parts.Count == 0 ? "Brak danych z zegarka w tym tygodniu." : string.Join(" · ", parts);
    }

    /// <summary>Linijka po treningu: „52 min · 430 kcal · śr. tętno 138 · max 171 · 5,2 km”.</summary>
    public static string SessionLine(int durationSeconds, int? kcal, int? avgHr, int? maxHr, int? distanceMeters)
    {
        var parts = new List<string> { ReadinessRules.Duration((int)Math.Round(durationSeconds / 60.0)) };
        if (kcal is > 0) parts.Add($"{kcal} kcal");
        if (avgHr is > 0) parts.Add($"śr. tętno {avgHr}");
        if (maxHr is > 0) parts.Add($"max {maxHr}");
        if (distanceMeters is >= 100) parts.Add($"{(distanceMeters.Value / 1000.0).ToString("0.0", Pl)} km");
        return string.Join(" · ", parts);
    }

    private static int? Avg(IEnumerable<int?> values)
    {
        var list = values.Where(v => v is > 0).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : (int)Math.Round(list.Average());
    }

    /// <summary>„ (+20 min)” / „ (−2)” — pomijamy, gdy brak porównania albo zmiana poniżej progu.</summary>
    private static string Delta(int now, int? before, Func<int, string> fmt, int threshold)
    {
        if (before is not int b) return "";
        var d = now - b;
        return Math.Abs(d) < threshold ? "" : $" ({(d > 0 ? "+" : "−")}{fmt(d)})";
    }
}
