using System.Globalization;

namespace PTScheduler.Application.Surveys;

public sealed record SurveyStatTile(string Key, string Label, decimal? Average, decimal? Previous, int Max, string? Unit, int Count);

public sealed record SurveyLoadPoint(DateOnly Date, decimal? Rpe, decimal? Load);

public enum InsightLevel { Info, Positive, Warning, Danger }

public sealed record SurveyInsight(InsightLevel Level, string Text);

public sealed class PostWorkoutStats
{
    public int Responses { get; init; }
    public List<SurveyStatTile> Tiles { get; init; } = [];
    public List<SurveyLoadPoint> Series { get; init; } = [];
    /// <summary>Acute:Chronic Workload Ratio (7 dni / średnia tygodniowa z 28 dni); null gdy za mało danych.</summary>
    public decimal? Acwr { get; init; }
    public List<SurveyInsight> Insights { get; init; } = [];
    public List<(DateOnly Date, string Text)> Comments { get; init; } = [];
}

/// <summary>
/// Statystyki i wnioski z ankiet po treningu dla trenera. Progi oparte na praktyce
/// monitorowania obciążeń: sRPE (RPE × minuty, Foster 2001), ACWR z „bezpieczną strefą”
/// ok. 0,8–1,3 i ryzykiem przeciążenia powyżej 1,5 (Gabbett 2016), trend RPE i samopoczucia.
/// </summary>
public static class SurveyInsights
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static PostWorkoutStats Compute(IReadOnlyList<SurveyResponseDto> responses, IReadOnlyList<SurveyQuestion> questions, DateOnly today)
    {
        var ordered = responses.Where(r => r.WorkoutDate is not null)
            .OrderBy(r => r.WorkoutDate).ThenBy(r => r.SubmittedAtUtc).ToList();

        var recentFrom = today.AddDays(-30);
        var prevFrom = today.AddDays(-60);

        // Kafelki: średnia z 30 dni vs poprzednie 30 dni dla pytań liczbowych oznaczonych w statystykach.
        var tiles = questions
            .Where(q => q.ShowInStats && q.Type is SurveyQuestionType.Scale or SurveyQuestionType.Number)
            .Select(q =>
            {
                var recent = Values(ordered.Where(r => r.WorkoutDate >= recentFrom), q.Key);
                var prev = Values(ordered.Where(r => r.WorkoutDate >= prevFrom && r.WorkoutDate < recentFrom), q.Key);
                return new SurveyStatTile(q.Key, q.Label, Avg(recent), Avg(prev), q.Max, q.Unit, recent.Count);
            })
            .Where(t => t.Count > 0)
            .ToList();

        var series = ordered.TakeLast(30).Select(r =>
        {
            var rpe = r[SurveyKeys.Rpe]?.Number;
            var dur = r[SurveyKeys.Duration]?.Number;
            return new SurveyLoadPoint(r.WorkoutDate!.Value, rpe, rpe is not null && dur is not null ? rpe * dur : null);
        }).ToList();

        var acwr = Acwr(ordered, today);
        var insights = new List<SurveyInsight>();

        // Ból w ostatnich 14 dniach.
        var painFrom = today.AddDays(-14);
        var pains = ordered.Where(r => r.WorkoutDate >= painFrom && r[SurveyKeys.Pain]?.Bool == true).ToList();
        if (pains.Count > 0)
        {
            var where = pains.Select(r => r[SurveyKeys.PainLocation]?.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            insights.Add(new(InsightLevel.Danger,
                $"Ból zgłoszony {Times(pains.Count)} w ostatnich 14 dniach" + (where.Count > 0 ? $": {string.Join("; ", where)}." : ".")));
        }

        // ACWR.
        if (acwr is { } ratio)
        {
            if (ratio > 1.5m)
                insights.Add(new(InsightLevel.Danger, $"Gwałtowny wzrost obciążenia (ACWR {F(ratio, "0.00")}) — podwyższone ryzyko przeciążenia i kontuzji. Rozważ lżejszy tydzień."));
            else if (ratio > 1.3m)
                insights.Add(new(InsightLevel.Warning, $"Obciążenie rośnie szybciej niż zwykle (ACWR {F(ratio, "0.00")}). Obserwuj regenerację."));
            else if (ratio < 0.8m)
                insights.Add(new(InsightLevel.Warning, $"Obciążenie spada (ACWR {F(ratio, "0.00")}) — ryzyko utraty formy przy dłuższej przerwie."));
            else
                insights.Add(new(InsightLevel.Positive, $"Obciążenie w bezpiecznej strefie (ACWR {F(ratio, "0.00")})."));
        }

        // Trend RPE: ostatnie 14 dni vs poprzednie 14 dni.
        var rpeNow = Avg(Values(ordered.Where(r => r.WorkoutDate > today.AddDays(-14)), SurveyKeys.Rpe));
        var rpeBefore = Avg(Values(ordered.Where(r => r.WorkoutDate > today.AddDays(-28) && r.WorkoutDate <= today.AddDays(-14)), SurveyKeys.Rpe));
        if (rpeNow is { } rn && rpeBefore is { } rb)
        {
            if (rn - rb >= 1.5m)
                insights.Add(new(InsightLevel.Warning, $"Odczuwany wysiłek (RPE) wzrósł z {F(rb)} do {F(rn)} — trening może być zbyt ciężki albo klient słabiej się regeneruje."));
            else if (rb - rn >= 1.5m)
                insights.Add(new(InsightLevel.Positive, $"Odczuwany wysiłek (RPE) spadł z {F(rb)} do {F(rn)} — klient może być gotowy na większe obciążenie."));
        }

        // Samopoczucie z ostatnich 5 ankiet.
        var last5 = ordered.TakeLast(5).ToList();
        CheckLow(insights, last5, SurveyKeys.Energy, 2m, "Niska energia w ostatnich treningach (śr. {0}/5).");
        CheckLow(insights, last5, SurveyKeys.Sleep, 2m, "Słaba jakość snu (śr. {0}/5) — gorsza regeneracja; rozważ niższą intensywność.");
        CheckHigh(insights, last5, SurveyKeys.Soreness, 4m, "Duże zmęczenie mięśni przed treningami (śr. {0}/5) — sprawdź objętość i przerwy między sesjami.");
        CheckLow(insights, last5, SurveyKeys.Satisfaction, 2m, "Niskie zadowolenie z treningów (śr. {0}/5) — warto porozmawiać o planie i celach.");

        var comments = ordered
            .Where(r => !string.IsNullOrWhiteSpace(r[SurveyKeys.Comment]?.Text))
            .OrderByDescending(r => r.WorkoutDate)
            .Take(5)
            .Select(r => (r.WorkoutDate!.Value, r[SurveyKeys.Comment]!.Text!))
            .ToList();

        return new PostWorkoutStats
        {
            Responses = ordered.Count,
            Tiles = tiles,
            Series = series,
            Acwr = acwr,
            Insights = insights,
            Comments = comments
        };
    }

    /// <summary>
    /// ACWR = obciążenie z ostatnich 7 dni / (obciążenie z ostatnich 28 dni / 4).
    /// Liczone tylko przy danych z co najmniej 3 tygodni, inaczej wynik byłby przypadkowy.
    /// </summary>
    public static decimal? Acwr(IReadOnlyList<SurveyResponseDto> ordered, DateOnly today)
    {
        var withLoad = ordered
            .Select(r => (Date: r.WorkoutDate!.Value, Load: r[SurveyKeys.Rpe]?.Number * r[SurveyKeys.Duration]?.Number))
            .Where(x => x.Load is not null)
            .ToList();
        if (withLoad.Count == 0 || today.DayNumber - withLoad.Min(x => x.Date).DayNumber < 21) return null;

        var acute = withLoad.Where(x => x.Date > today.AddDays(-7)).Sum(x => x.Load!.Value);
        var chronicWeekly = withLoad.Where(x => x.Date > today.AddDays(-28)).Sum(x => x.Load!.Value) / 4m;
        if (chronicWeekly <= 0) return null;
        return Math.Round(acute / chronicWeekly, 2);
    }

    private static void CheckLow(List<SurveyInsight> list, List<SurveyResponseDto> recent, string key, decimal threshold, string format)
    {
        var values = Values(recent, key);
        if (values.Count >= 3 && Avg(values) is { } avg && avg <= threshold)
            list.Add(new(InsightLevel.Warning, string.Format(format, F(avg))));
    }

    private static void CheckHigh(List<SurveyInsight> list, List<SurveyResponseDto> recent, string key, decimal threshold, string format)
    {
        var values = Values(recent, key);
        if (values.Count >= 3 && Avg(values) is { } avg && avg >= threshold)
            list.Add(new(InsightLevel.Warning, string.Format(format, F(avg))));
    }

    private static List<decimal> Values(IEnumerable<SurveyResponseDto> rs, string key) =>
        rs.Select(r => r[key]?.Number).Where(n => n is not null).Select(n => n!.Value).ToList();

    private static decimal? Avg(List<decimal> v) => v.Count == 0 ? null : Math.Round(v.Average(), 1);

    private static string F(decimal d, string format = "0.#") => d.ToString(format, Pl);

    private static string Times(int n) => n == 1 ? "1 raz" : $"{n} razy";
}
