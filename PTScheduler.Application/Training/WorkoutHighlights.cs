using System.Globalization;

namespace PTScheduler.Application.Training;

/// <summary>Seria z dzisiejszego albo wcześniejszego treningu.</summary>
public sealed record SetRecord(int Reps, decimal WeightKg);

/// <summary>Dzisiejsze ćwiczenie i to, co klient robił w nim wcześniej.</summary>
public sealed record ExerciseToday(int ExerciseId, string Name, IReadOnlyList<SetRecord> Today, decimal? PreviousMaxKg,
    IReadOnlyList<SetRecord>? LastTime);

public sealed record Highlight(string Kind, string Text);

public sealed record WorkoutHighlightsResult(List<Highlight> Items, int Records, int Improved, string Fallback, Dictionary<string, string> Facts);

/// <summary>
/// Podsumowanie po treningu dla klienta: rekordy ciężaru, poprawa względem ostatniego razu
/// (więcej powtórzeń albo kilogramów w najlepszej serii), seria tygodni z treningiem i treningi w miesiącu.
/// Tekst zapasowy dla AI (pod marką trenera, bez maskotki) — liczby tylko stąd.
/// </summary>
public static class WorkoutHighlights
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static WorkoutHighlightsResult Compute(IReadOnlyList<ExerciseToday> exercises, int weekStreak, int workoutsThisMonth)
    {
        var items = new List<Highlight>();
        var records = 0;
        var improved = 0;
        string? topRecord = null;

        foreach (var e in exercises)
        {
            var working = e.Today.Where(s => s.Reps > 0).ToList();
            if (working.Count == 0) continue;
            var maxToday = working.Max(s => s.WeightKg);
            if (maxToday > 0 && e.PreviousMaxKg is { } prevMax && maxToday > prevMax)
            {
                records++;
                topRecord ??= $"{e.Name}: {Kg(maxToday)} kg";
                items.Add(new("rekord", $"Rekord: {e.Name} — {Kg(maxToday)} kg (poprzednio {Kg(prevMax)} kg)"));
                continue;
            }
            if (e.LastTime is { Count: > 0 } last && Best(working) is var (todayKg, todayReps) && Best(last.Where(s => s.Reps > 0).ToList()) is var (lastKg, lastReps)
                && (todayKg > lastKg || (todayKg == lastKg && todayReps > lastReps)))
            {
                improved++;
                items.Add(new("postep", todayKg > lastKg
                    ? $"{e.Name}: {Kg(todayKg)} kg zamiast {Kg(lastKg)} kg ostatnio"
                    : $"{e.Name}: {todayReps} powt. przy {Kg(todayKg)} kg (ostatnio {lastReps})"));
            }
        }

        if (weekStreak >= 2) items.Add(new("seria", $"{weekStreak}. tydzień z rzędu z treningiem"));
        if (workoutsThisMonth >= 4) items.Add(new("miesiac", $"{workoutsThisMonth}. trening w tym miesiącu"));

        var facts = new Dictionary<string, string> { ["cwiczenia"] = exercises.Count.ToString() };
        if (records > 0) facts["rekordy"] = records.ToString();
        if (topRecord is not null) facts["najlepszy_rekord"] = topRecord;
        if (improved > 0) facts["poprawione_cwiczenia"] = improved.ToString();
        if (weekStreak >= 2) facts["tygodnie_z_rzedu"] = weekStreak.ToString();
        if (workoutsThisMonth >= 1) facts["treningi_w_miesiacu"] = workoutsThisMonth.ToString();

        var fallback = records > 0
            ? $"Nowy rekord ({topRecord})! Tak wygląda postęp — dobra robota."
            : improved > 0
                ? $"Lepiej niż ostatnio w {improved} {Plural(improved, "ćwiczeniu", "ćwiczeniach", "ćwiczeniach")}. Krok po kroku do celu!"
                : weekStreak >= 2
                    ? $"{weekStreak}. tydzień z rzędu z treningiem — regularność robi robotę."
                    : "Trening zaliczony — każdy kolejny buduje formę.";
        return new(items, records, improved, fallback, facts);
    }

    /// <summary>Najlepsza seria: najwyższy ciężar, a przy równym — najwięcej powtórzeń.</summary>
    private static (decimal Kg, int Reps) Best(IReadOnlyList<SetRecord> sets) =>
        sets.Count == 0 ? (0m, 0) : sets.OrderByDescending(s => s.WeightKg).ThenByDescending(s => s.Reps).Select(s => (s.WeightKg, s.Reps)).First();

    /// <summary>Krótki opis ostatniego razu: „3×10 · 60 kg” albo „10, 10, 8 powt.”.</summary>
    public static string Describe(IReadOnlyList<SetRecord> sets)
    {
        var working = sets.Where(s => s.Reps > 0).ToList();
        if (working.Count == 0) return "";
        var allSame = working.All(s => s.Reps == working[0].Reps && s.WeightKg == working[0].WeightKg);
        if (allSame)
            return working[0].WeightKg > 0 ? $"{working.Count}×{working[0].Reps} · {Kg(working[0].WeightKg)} kg" : $"{working.Count}×{working[0].Reps}";
        return string.Join(" · ", working.Select(s => s.WeightKg > 0 ? $"{s.Reps}×{Kg(s.WeightKg)}" : $"{s.Reps}")) + (working.Any(s => s.WeightKg > 0) ? " kg" : " powt.");
    }

    /// <summary>Ile tygodni z rzędu (do bieżącego włącznie albo poprzedniego) klient trenował.</summary>
    public static int WeekStreak(IEnumerable<DateOnly> workoutDays, DateOnly today)
    {
        static DateOnly Monday(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        var weeks = workoutDays.Select(Monday).ToHashSet();
        var week = Monday(today);
        if (!weeks.Contains(week)) week = week.AddDays(-7);
        var streak = 0;
        while (weeks.Contains(week)) { streak++; week = week.AddDays(-7); }
        return streak;
    }

    private static string Kg(decimal v) => v.ToString("0.##", Pl);

    private static string Plural(int n, string one, string few, string many) =>
        n == 1 ? one : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? few : many;
}
