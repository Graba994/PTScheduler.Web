using System.Globalization;

namespace PTScheduler.Domain.Rules;

/// <summary>Jedno wykonanie ćwiczenia danego dnia (kilka zapisów z tego samego dnia — scalone).</summary>
public sealed record ExerciseDay(DateOnly Date, IReadOnlyList<SetResult> Sets)
{
    /// <summary>Najwyższy ciężar dnia (serie robocze).</summary>
    public decimal TopKg => Sets.Count == 0 ? 0m : Sets.Max(s => s.WeightKg);
    /// <summary>Najwięcej powtórzeń w seriach z najwyższym ciężarem.</summary>
    public int TopReps => Sets.Count == 0 ? 0 : Sets.Where(s => s.WeightKg == TopKg).Max(s => s.Reps);
}

/// <summary>Co trener zaplanował dla ćwiczenia.</summary>
public sealed record PlannedTarget(int Sets, string? Reps, decimal? TargetKg, int? RestSeconds);

public enum InsightKind
{
    /// <summary>Ostatnia seria regularnie nie dochodzi do zakresu powtórzeń.</summary>
    LastSetDrop,
    /// <summary>Żadna seria nie dochodzi do dolnej granicy zakresu — ciężar albo zakres za ambitny.</summary>
    BelowRange,
    /// <summary>Klient robi mniej serii niż w planie.</summary>
    FewerSets,
    /// <summary>Ciężar wyraźnie niższy niż docelowy w planie.</summary>
    BelowTarget,
    /// <summary>Ten sam ciężar i powtórzenia od kilku treningów.</summary>
    Stagnation
}

public sealed record PlanInsight(InsightKind Kind, string Finding, string Recommendation);

/// <summary>
/// Wnioski z dziennika treningów — liczone regułami, bez AI: zmiany siły (ciężar roboczy na początku
/// i teraz) oraz odstępstwa od planu, z konkretną propozycją dla trenera. AI może je najwyżej
/// opisać słowami; liczby pochodzą zawsze stąd.
/// </summary>
public static class ProgressInsights
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Ile ostatnich treningów oceniamy przy odstępstwach (wzorzec = co najmniej 2 z nich).</summary>
    public const int RecentWindow = 3;
    public const int StagnationWindow = 4;

    /// <summary>Ciężar roboczy z pierwszego i ostatniego treningu z obciążeniem. Null, gdy za mało danych.</summary>
    public static (decimal FromKg, decimal ToKg)? StrengthChange(IReadOnlyList<ExerciseDay> days)
    {
        var loaded = days.Where(d => d.TopKg > 0m).OrderBy(d => d.Date).ToList();
        if (loaded.Count < 2) return null;
        return (loaded[0].TopKg, loaded[^1].TopKg);
    }

    /// <summary>
    /// Odstępstwa od planu na podstawie ostatnich treningów (od najnowszego). Każdy wzorzec musi
    /// wystąpić co najmniej w 2 z 3 ostatnich treningów, w tym w najnowszym — pojedynczy słabszy dzień to nie problem.
    /// </summary>
    public static List<PlanInsight> Analyze(PlannedTarget plan, IReadOnlyList<ExerciseDay> newestFirst)
    {
        var result = new List<PlanInsight>();
        var recent = newestFirst.Where(d => d.Sets.Count > 0).Take(RecentWindow).ToList();
        if (recent.Count < 2) return result;
        var range = ProgressionRules.ParseReps(plan.Reps);

        // Wzorzec musi być też widoczny w najnowszym treningu — jeśli klient już dogonił plan, nie ma czego poprawiać
        // (a tekst z liczbami z ostatniego treningu nie brzmiałby „27,5 kg zamiast zaplanowanych 27,5 kg”).
        bool Pattern(Func<ExerciseDay, bool> test) => test(recent[0]) && recent.Count(test) >= 2;

        if (range is { } r1 && Pattern(d => d.Sets.Count >= 2 && d.Sets[^1].Reps < r1.Min && d.Sets[0].Reps >= r1.Min))
        {
            var last = recent[0];
            var rest = plan.RestSeconds is int s and > 0 ? $"z {s} s do {s + 30} s" : "o 30 s";
            result.Add(new(InsightKind.LastSetDrop,
                $"Ostatnia seria spada poniżej {r1.Min} powtórzeń (ostatnio {RepsText(last)}).",
                $"Wydłuż przerwę przed ostatnią serią {rest} albo zmniejsz ciężar o jeden krok."));
        }

        if (range is { } r2 && Pattern(d => d.Sets.All(x => x.Reps < r2.Min)))
        {
            var lower = r2.Min > 6 ? $"{r2.Min - 2}–{r2.Max - 2}" : null;
            result.Add(new(InsightKind.BelowRange,
                $"Zakres {r2.Min}{(r2.Max > r2.Min ? $"–{r2.Max}" : "")} nie wchodzi (ostatnio {RepsText(recent[0])}{(recent[0].TopKg > 0 ? $" na {Kg(recent[0].TopKg)}" : "")}).",
                lower is null ? "Zmniejsz ciężar o jeden krok, aż zakres zacznie wchodzić."
                    : $"Zmniejsz ciężar o jeden krok albo obniż zakres do {lower} powtórzeń."));
        }

        if (plan.Sets > 1 && Pattern(d => d.Sets.Count < plan.Sets))
            result.Add(new(InsightKind.FewerSets,
                $"Zwykle {recent[0].Sets.Count} z {plan.Sets} zaplanowanych serii.",
                "Sprawdź, czy wystarcza czasu na trening — albo zmniejsz liczbę serii w planie."));

        if (plan.TargetKg is decimal target and > 0m && Pattern(d => d.TopKg > 0m && d.TopKg < target * 0.95m))
            result.Add(new(InsightKind.BelowTarget,
                $"Ciężar {Kg(recent[0].TopKg)} zamiast zaplanowanych {Kg(target)}.",
                $"Rozważ cel {Kg(recent[0].TopKg)} w planie i podnoszenie stopniowo."));

        var window = newestFirst.Where(d => d.Sets.Count > 0).Take(StagnationWindow).ToList();
        if (window.Count == StagnationWindow && window[0].TopKg > 0m
            && window.All(d => d.TopKg == window[0].TopKg)
            && window[0].TopReps <= window[^1].TopReps
            && (range is null || window[0].TopReps < range.Value.Max))
            result.Add(new(InsightKind.Stagnation,
                $"Od {StagnationWindow} treningów {Kg(window[0].TopKg)} × {window[0].TopReps} bez postępu.",
                "Zmień wariant ćwiczenia albo zakres powtórzeń, ewentualnie zaplanuj lżejszy tydzień."));

        return result;
    }

    public static string RepsText(ExerciseDay day) => string.Join("/", day.Sets.Select(s => s.Reps));

    public static string Kg(decimal kg) => kg.ToString("0.##", Pl) + " kg";
}
