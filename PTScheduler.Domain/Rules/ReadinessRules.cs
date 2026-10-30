namespace PTScheduler.Domain.Rules;

/// <summary>
/// Gotowość klienta na dziś z danych z zegarka: sen, tętno spoczynkowe i HRV względem własnej normy (średnia
/// z ostatnich 28 dni), Body Battery. Same reguły — AI może je najwyżej ładniej ująć. Wynik to podpowiedź dla
/// trenera, nie diagnoza: decyzję o treningu podejmuje trener po rozmowie z klientem.
/// </summary>
public static class ReadinessRules
{
    public enum Level { Unknown, Low, Ok, Good }

    public sealed record Day(DateOnly Date, int? SleepMinutes, int? SleepScore, int? RestingHeartRate, int? Hrv, int? BodyBatteryHigh);

    public sealed record Result(Level Level, int Score, IReadOnlyList<string> Reasons, string Suggestion);

    /// <summary>Gotowość na <paramref name="today"/> — dzień z zegarka z tej daty albo poprzedniej nocy.</summary>
    /// <param name="extra">Dodatkowe wskazówki (np. wczorajszy mocny trening z <see cref="TrainingLoadRules"/>): kara do wyniku i powód.</param>
    public static Result For(DateOnly today, IReadOnlyList<Day> history, IReadOnlyList<(int Penalty, string Reason)>? extra = null)
    {
        var day = history.Where(d => d.Date == today || d.Date == today.AddDays(-1)).OrderByDescending(d => d.Date).FirstOrDefault();
        if (day is null || (day.SleepMinutes is null && day.RestingHeartRate is null && day.Hrv is null && day.BodyBatteryHigh is null))
            return new(Level.Unknown, 0, [], "Brak świeżych danych z zegarka.");

        var baseline = history.Where(d => d.Date < day.Date && d.Date >= day.Date.AddDays(-28)).ToList();
        var score = 100;
        var reasons = new List<string>();

        if (day.SleepMinutes is int sleep)
        {
            if (sleep < 360) { score -= 25; reasons.Add($"krótki sen ({Duration(sleep)})"); }
            else if (sleep < 420) { score -= 10; reasons.Add($"sen poniżej 7 h ({Duration(sleep)})"); }
        }
        if (day.SleepScore is < 60) { score -= 10; reasons.Add($"słaba jakość snu ({day.SleepScore}/100)"); }

        if (day.RestingHeartRate is int rhr && Average(baseline.Select(d => d.RestingHeartRate)) is double rhrBase)
        {
            var diff = rhr - rhrBase;
            if (diff >= 5) { score -= 20; reasons.Add($"tętno spoczynkowe +{diff:0} względem normy"); }
            else if (diff >= 3) { score -= 10; reasons.Add($"tętno spoczynkowe lekko podwyższone (+{diff:0})"); }
        }
        if (day.Hrv is int hrv && Average(baseline.Select(d => d.Hrv)) is double hrvBase && hrvBase > 0)
        {
            var ratio = hrv / hrvBase;
            if (ratio <= .85) { score -= 20; reasons.Add($"HRV poniżej normy ({hrv} ms, zwykle {hrvBase:0})"); }
            else if (ratio <= .92) { score -= 8; reasons.Add($"HRV trochę niższe niż zwykle ({hrv} ms)"); }
        }
        if (day.BodyBatteryHigh is int bb)
        {
            if (bb < 40) { score -= 15; reasons.Add($"niska Body Battery ({bb})"); }
            else if (bb < 55) { score -= 5; reasons.Add($"Body Battery {bb}"); }
        }

        foreach (var (penalty, reason) in extra ?? [])
        {
            score -= penalty;
            reasons.Add(reason);
        }

        score = Math.Clamp(score, 0, 100);
        var level = score >= 75 ? Level.Good : score >= 50 ? Level.Ok : Level.Low;
        var suggestion = level switch
        {
            Level.Low => "Rozważ lżejszy trening albo więcej przerw — na początku zapytaj o samopoczucie.",
            Level.Ok => "Normalny trening, obserwuj zmęczenie w trakcie.",
            _ => "Dobry dzień na mocniejszy trening."
        };
        return new(level, score, reasons, suggestion);
    }

    public static string Label(Level level) => level switch
    {
        Level.Good => "dobra", Level.Ok => "średnia", Level.Low => "niska", _ => "brak danych"
    };

    /// <summary>Średnia z co najmniej 3 dni z danymi — z krótszej historii normy nie liczymy.</summary>
    private static double? Average(IEnumerable<int?> values)
    {
        var list = values.Where(v => v is not null).Select(v => (double)v!.Value).ToList();
        return list.Count >= 3 ? list.Average() : null;
    }

    public static string Duration(int minutes) =>
        minutes < 60 ? $"{Math.Max(0, minutes)} min" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";

    /// <summary>
    /// Wizyta, którą obejmuje aktywność z zegarka: największa część wspólna, co najmniej połowa krótszego z odcinków
    /// (zegarek włączony 5 min po starcie albo wyłączony chwilę po końcu nadal pasuje).
    /// </summary>
    public static int? MatchSession(DateTime activityStart, int durationSeconds, IEnumerable<(int Id, DateTime Start, DateTime End)> sessions)
    {
        var aEnd = activityStart.AddSeconds(Math.Max(60, durationSeconds));
        int? best = null;
        double bestOverlap = 0;
        foreach (var (id, start, end) in sessions)
        {
            var overlap = (Min(aEnd, end) - Max(activityStart, start)).TotalSeconds;
            var shorter = Math.Min((aEnd - activityStart).TotalSeconds, (end - start).TotalSeconds);
            if (overlap > 0 && overlap >= shorter / 2 && overlap > bestOverlap) { best = id; bestOverlap = overlap; }
        }
        return best;

        static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    }
}
