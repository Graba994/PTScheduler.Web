namespace PTScheduler.Domain.Rules;

/// <summary>
/// Obciążenie treningowe z aktywności z zegarka: minuty × intensywność z tętna (rezerwa tętna, Karvonen), ostatnie
/// 7 dni względem typowego tygodnia z 3 tygodni wcześniej. Do tego wczorajszy mocny trening — trafia do gotowości.
/// Same reguły; to podpowiedź dla trenera, nie diagnoza.
/// </summary>
public static class TrainingLoadRules
{
    /// <summary>Aktywność: dzień (czas lokalny), nazwa, czas trwania i tętno.</summary>
    public sealed record Act(DateOnly Date, string Name, int DurationSeconds, int? AverageHeartRate, int? MaxHeartRate);

    public sealed record WeekLoad(DateOnly From, int Units, int Minutes);

    /// <param name="Units">Obciążenie z 7 dni (minuty × intensywność).</param>
    /// <param name="TypicalUnits">Typowy tydzień z 3 tygodni przed nimi — null, gdy historia krótsza niż 2 tygodnie.</param>
    /// <param name="ChangePercent">Zmiana względem typowego tygodnia (np. +60) — null bez porównania.</param>
    public sealed record Result(int Units, int Minutes, int Sessions, int HardMinutes, int? TypicalUnits, int? ChangePercent,
        string? Warning, int EstimatedMaxHr, IReadOnlyList<WeekLoad> Weeks)
    {
        public bool HasData => Sessions > 0 || Weeks.Any(w => w.Units > 0);
    }

    /// <summary>Tętno maksymalne: najwyższe zmierzone na treningach, nie niżej niż wzór Tanaki (208 − 0,7 × wiek).</summary>
    public static int EstimateMaxHr(IEnumerable<Act> acts, int? age)
    {
        var observed = acts.Select(a => a.MaxHeartRate ?? 0).DefaultIfEmpty(0).Max();
        var formula = age is int y and > 10 and < 100 ? (int)Math.Round(208 - 0.7 * y) : 0;
        var best = Math.Max(observed, formula);
        return best >= 140 ? best : 190;
    }

    /// <summary>Intensywność 0,2–1: część rezerwy tętna (średnie − spoczynkowe) / (maks. − spoczynkowe). Bez tętna — 0,6.</summary>
    public static double Intensity(int? averageHr, int maxHr, int? restingHr)
    {
        if (averageHr is not int avg || avg <= 0) return 0.6;
        var rest = restingHr is int r and > 30 and < 100 ? r : 60;
        if (maxHr <= rest + 20) return 0.6;
        return Math.Clamp((avg - rest) / (double)(maxHr - rest), 0.2, 1.0);
    }

    public static double Units(Act a, int maxHr, int? restingHr) => a.DurationSeconds / 60.0 * Intensity(a.AverageHeartRate, maxHr, restingHr);

    public static Result For(DateOnly today, IReadOnlyCollection<Act> acts, int maxHr, int? restingHr)
    {
        var from7 = today.AddDays(-6);
        var last7 = acts.Where(a => a.Date >= from7 && a.Date <= today).ToList();
        var units = (int)Math.Round(last7.Sum(a => Units(a, maxHr, restingHr)));
        var minutes = (int)Math.Round(last7.Sum(a => a.DurationSeconds) / 60.0);
        var hard = (int)Math.Round(last7.Where(a => Intensity(a.AverageHeartRate, maxHr, restingHr) >= 0.7).Sum(a => a.DurationSeconds) / 60.0);

        // Typowy tydzień: 21 dni przed ostatnim tygodniem / 3 — tylko gdy dane sięgają co najmniej 2 tygodnie wstecz.
        int? typical = null;
        var first = acts.Select(a => a.Date).DefaultIfEmpty(today).Min();
        if (first <= today.AddDays(-13))
        {
            var before = acts.Where(a => a.Date >= today.AddDays(-27) && a.Date < from7).Sum(a => Units(a, maxHr, restingHr));
            var weeks = Math.Clamp((from7.DayNumber - Math.Max(first.DayNumber, today.AddDays(-27).DayNumber)) / 7.0, 1, 3);
            typical = (int)Math.Round(before / weeks);
        }
        int? change = typical is int t and >= 30 ? (int)Math.Round((units - t) * 100.0 / t) : null;
        var warning = change switch
        {
            >= 50 when units >= 60 => $"obciążenie z 7 dni +{change}% względem zwykłego tygodnia — ryzyko przeciążenia",
            <= -50 => $"ruch z 7 dni {change}% względem zwykłego tygodnia",
            _ => null
        };

        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weeksList = Enumerable.Range(0, 6).Select(i => monday.AddDays(-7 * (5 - i)))
            .Select(w => new WeekLoad(w,
                (int)Math.Round(acts.Where(a => a.Date >= w && a.Date < w.AddDays(7)).Sum(a => Units(a, maxHr, restingHr))),
                (int)Math.Round(acts.Where(a => a.Date >= w && a.Date < w.AddDays(7)).Sum(a => a.DurationSeconds) / 60.0)))
            .ToList();
        return new(units, minutes, last7.Count, hard, typical, change, warning, maxHr, weeksList);
    }

    /// <summary>
    /// Wskazówki do gotowości: wczorajszy mocny trening i gwałtowny skok obciążenia (kara do wyniku + powód).
    /// </summary>
    public static IReadOnlyList<(int Penalty, string Reason)> ReadinessHints(DateOnly today, IReadOnlyCollection<Act> acts, int maxHr, int? restingHr)
    {
        var hints = new List<(int, string)>();
        var yesterday = acts.Where(a => a.Date == today.AddDays(-1)).ToList();
        var yUnits = yesterday.Sum(a => Units(a, maxHr, restingHr));
        if (yesterday.Count > 0 && yUnits >= 50)
        {
            var main = yesterday.OrderByDescending(a => Units(a, maxHr, restingHr)).First();
            var mins = (int)Math.Round(yesterday.Sum(a => a.DurationSeconds) / 60.0);
            var hr = main.AverageHeartRate is int h ? $", śr. tętno {h}" : "";
            hints.Add((yUnits >= 80 ? 15 : 8, $"wczoraj mocny trening ({main.Name}, {ReadinessRules.Duration(mins)}{hr})"));
        }
        var load = For(today, acts, maxHr, restingHr);
        if (load.ChangePercent is >= 50 && load.Units >= 60) hints.Add((5, $"obciążenie z 7 dni +{load.ChangePercent}% względem zwykłego tygodnia"));
        return hints;
    }
}
