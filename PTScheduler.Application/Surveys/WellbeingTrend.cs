using System.Globalization;

namespace PTScheduler.Application.Surveys;

public enum TrendDirection { Unknown, Down, Stable, Up }

/// <summary>Jeden punkt trendu: indeks samopoczucia 1–5 z jednej ankiety po treningu.</summary>
public sealed record WellbeingPoint(DateOnly Date, decimal Score, bool Pain);

public sealed record WellbeingSummary(List<WellbeingPoint> Points, TrendDirection Direction, decimal? Recent, decimal? Before,
    int PainInLast5, decimal? Satisfaction, string Text)
{
    public bool IsDropping => Direction == TrendDirection.Down;
    public bool LowSatisfaction => Satisfaction is <= 2m;
}

/// <summary>
/// Trend samopoczucia z ankiet po treningu: indeks 1–5 = średnia z energii, snu, zadowolenia
/// i odwróconego zmęczenia mięśni (6 − wynik). Ostatnie 3 ankiety vs do 5 wcześniejszych;
/// zmiana o 0,7 punktu i więcej to „spada” albo „rośnie”.
/// </summary>
public static class WellbeingTrend
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");
    public const decimal Threshold = 0.7m;

    public static WellbeingSummary Compute(IEnumerable<SurveyResponseDto> responses)
    {
        var points = responses
            .Where(r => r.WorkoutDate is not null)
            .OrderBy(r => r.WorkoutDate).ThenBy(r => r.SubmittedAtUtc)
            .Select(r => (r, score: Score(r)))
            .Where(x => x.score is not null)
            .Select(x => new WellbeingPoint(x.r.WorkoutDate!.Value, Math.Round(x.score!.Value, 2), x.r[SurveyKeys.Pain]?.Bool == true))
            .TakeLast(12)
            .ToList();

        var ordered = responses.Where(r => r.WorkoutDate is not null).OrderBy(r => r.WorkoutDate).ThenBy(r => r.SubmittedAtUtc).ToList();
        var pain = ordered.TakeLast(5).Count(r => r[SurveyKeys.Pain]?.Bool == true);
        var sat = ordered.TakeLast(5).Select(r => r[SurveyKeys.Satisfaction]?.Number).Where(n => n is not null).Select(n => n!.Value).ToList();
        decimal? satisfaction = sat.Count >= 2 ? Math.Round(sat.Average(), 1) : null;

        if (points.Count < 4)
            return new(points, TrendDirection.Unknown, null, null, pain, satisfaction,
                points.Count == 0 ? "Brak ankiet po treningu z oceną samopoczucia." : "Za mało ankiet, żeby mówić o trendzie (potrzebne 4).");

        var recent = Math.Round(points.TakeLast(3).Average(p => p.Score), 1);
        var before = Math.Round(points.SkipLast(3).TakeLast(5).Average(p => p.Score), 1);
        var direction = recent - before <= -Threshold ? TrendDirection.Down
            : recent - before >= Threshold ? TrendDirection.Up : TrendDirection.Stable;
        var text = direction switch
        {
            TrendDirection.Down => $"Samopoczucie spada: {F(before)} → {F(recent)} (ostatnie 3 ankiety).",
            TrendDirection.Up => $"Samopoczucie rośnie: {F(before)} → {F(recent)} (ostatnie 3 ankiety).",
            _ => $"Samopoczucie stabilne: około {F(recent)}/5.",
        };
        if (pain >= 2) text += $" Ból zgłoszony w {pain} z ostatnich 5 ankiet.";
        return new(points, direction, recent, before, pain, satisfaction, text);
    }

    /// <summary>Indeks 1–5 z dostępnych odpowiedzi (null, gdy ankieta nie ma żadnej z nich).</summary>
    public static decimal? Score(SurveyResponseDto r)
    {
        var parts = new List<decimal>();
        if (r[SurveyKeys.Energy]?.Number is { } e) parts.Add(e);
        if (r[SurveyKeys.Sleep]?.Number is { } s) parts.Add(s);
        if (r[SurveyKeys.Satisfaction]?.Number is { } sat) parts.Add(sat);
        if (r[SurveyKeys.Soreness]?.Number is { } so) parts.Add(6 - so);
        return parts.Count == 0 ? null : parts.Average();
    }

    private static string F(decimal v) => v.ToString("0.0", Pl);
}
