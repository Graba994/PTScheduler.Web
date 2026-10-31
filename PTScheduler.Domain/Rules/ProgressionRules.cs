using System.Globalization;
using System.Text.RegularExpressions;

namespace PTScheduler.Domain.Rules;

public enum ProgressionAction
{
    /// <summary>Dołożyć ciężaru — serie weszły lekko.</summary>
    Increase,
    /// <summary>Zdjąć ciężar — klient nie domyka zakresu przy bardzo wysokim wysiłku.</summary>
    Deload
}

/// <summary>Jedna seria z ostatniego wykonania ćwiczenia.</summary>
public readonly record struct SetResult(int Reps, decimal WeightKg);

/// <summary>Wynik reguły: co zaproponować trenerowi i dlaczego.</summary>
public sealed record ProgressionDecision(ProgressionAction Action, decimal FromKg, decimal ToKg, string Reason)
{
    public decimal DeltaKg => ToKg - FromKg;
}

/// <summary>
/// Podwójna progresja sterowana ankietą po treningu (RPE 0–10, skala CR-10).
/// Klient pracuje w zakresie powtórzeń („8-12"); gdy wszystkie serie robocze
/// dochodzą do górnej granicy, a wysiłek był umiarkowany (RPE ≤ 7), proponujemy
/// większy ciężar. Gdy przy RPE ≥ 9 serie nie dochodzą do dolnej granicy —
/// albo zakres nie wchodzi dwa treningi z rzędu — proponujemy zejście o ~10%.
/// Reguła tylko podpowiada; decyzję zawsze podejmuje trener.
/// </summary>
public static class ProgressionRules
{
    public const decimal EasyRpe = 7m;
    public const decimal HardRpe = 9m;

    private static readonly HashSet<string> LowerBody =
        new(StringComparer.OrdinalIgnoreCase) { "quadriceps", "hamstrings", "glutes" };

    /// <summary>
    /// Zakres powtórzeń z pola planu: „10" → (10,10), „8-12" / „8–12" → (8,12).
    /// Null dla „max", „AMRAP", czasu itp. — tam nie liczymy progresji.
    /// </summary>
    public static (int Min, int Max)? ParseReps(string? reps)
    {
        if (string.IsNullOrWhiteSpace(reps)) return null;
        var m = Regex.Match(reps.Trim(), @"^(\d{1,3})\s*(?:[-–—]\s*(\d{1,3}))?$");
        if (!m.Success) return null;
        var a = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : a;
        if (a <= 0 || b <= 0) return null;
        return (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>
    /// Krok podbicia ciężaru: małe ciężary +1 kg, hantle/kettle +2 kg,
    /// ciężkie wielostawowe na nogi +5 kg, reszta +2,5 kg.
    /// </summary>
    public static decimal Increment(decimal weightKg, string? primaryMusclesCsv, string? mechanic, string? equipment)
    {
        if (weightKg < 20m) return 1m;
        var eq = equipment ?? "";
        if (eq.Contains("dumbbell", StringComparison.OrdinalIgnoreCase) || eq.Contains("kettlebell", StringComparison.OrdinalIgnoreCase))
            return 2m;
        var lower = (primaryMusclesCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(LowerBody.Contains);
        if (lower && string.Equals(mechanic, "compound", StringComparison.OrdinalIgnoreCase) && weightKg >= 60m)
            return 5m;
        return 2.5m;
    }

    /// <summary>Około −10%, zaokrąglone do sensownego kroku talerzy, zawsze choć jeden krok w dół.</summary>
    public static decimal DeloadWeight(decimal weightKg, decimal step)
    {
        var target = Math.Floor(weightKg * 0.9m / step) * step;
        if (target >= weightKg) target = weightKg - step;
        return Math.Max(0m, target);
    }

    /// <summary>
    /// Decyzja na podstawie ostatniego wykonania (i ewentualnie poprzedniego).
    /// Liczą się serie robocze = serie z najwyższym ciężarem danego dnia.
    /// </summary>
    /// <param name="plannedSets">Liczba serii w planie (0 = bez wymagania).</param>
    /// <param name="reps">Pole „powtórzenia" z planu.</param>
    /// <param name="last">Serie z ostatniego treningu.</param>
    /// <param name="previous">Serie z treningu wcześniej (albo null).</param>
    /// <param name="rpe">RPE z ankiety po tamtym treningu (albo null).</param>
    public static ProgressionDecision? Decide(
        int plannedSets, string? reps, IReadOnlyList<SetResult> last, IReadOnlyList<SetResult>? previous,
        decimal? rpe, string? primaryMusclesCsv, string? mechanic, string? equipment)
    {
        var range = ParseReps(reps);
        if (range is null || last.Count == 0) return null;
        var (min, max) = range.Value;

        var top = last.Max(s => s.WeightKg);
        if (top <= 0m) return null; // masa ciała — tu progresja to powtórzenia, nie kilogramy
        var working = last.Where(s => s.WeightKg == top).ToList();
        var step = Increment(top, primaryMusclesCsv, mechanic, equipment);
        var rpeText = rpe is decimal r ? $"RPE {r.ToString("0.#", CultureInfo.GetCultureInfo("pl-PL"))}" : null;

        var enoughSets = working.Count >= Math.Max(1, plannedSets);
        var allAtTop = working.All(s => s.Reps >= max);
        if (enoughSets && allAtTop && (rpe is null || rpe <= EasyRpe))
        {
            var why = rpe is null
                ? $"Wszystkie serie robocze po {max}+ powtórzeń na {Kg(top)} (brak ankiety — dopytaj o samopoczucie)."
                : $"Wszystkie serie robocze po {max}+ powtórzeń na {Kg(top)} przy {rpeText}.";
            return new ProgressionDecision(ProgressionAction.Increase, top, top + step, why);
        }

        var missed = working.Any(s => s.Reps < min);
        if (missed && rpe >= HardRpe)
            return new ProgressionDecision(ProgressionAction.Deload, top, DeloadWeight(top, step),
                $"Poniżej {min} powtórzeń na {Kg(top)} przy {rpeText} — warto zejść z ciężaru.");

        if (missed && previous is { Count: > 0 })
        {
            var prevTop = previous.Max(s => s.WeightKg);
            if (prevTop == top && previous.Where(s => s.WeightKg == top).Any(s => s.Reps < min))
                return new ProgressionDecision(ProgressionAction.Deload, top, DeloadWeight(top, step),
                    $"Drugi trening z rzędu poniżej {min} powtórzeń na {Kg(top)}.");
        }

        return null;
    }

    private static string Kg(decimal kg) => kg.ToString("0.##", CultureInfo.GetCultureInfo("pl-PL")) + " kg";
}
