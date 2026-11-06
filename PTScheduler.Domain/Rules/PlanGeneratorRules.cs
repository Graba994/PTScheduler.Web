using System.Globalization;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Propozycja planu treningowego z historii klienta — bez AI, same reguły, więc działa zawsze.
/// Podział tygodnia wynika z liczby dni, ćwiczenia dobieramy do „slotów” (wzorców ruchu) z katalogu:
/// najpierw te, które klient już robił, z pominięciem kolidujących z ankietą zdrowotną i zgłoszonym bólem.
/// Ciężar to ostatni wynik z drobną progresją, gdy RPE było umiarkowane. To propozycja — trener ją zatwierdza.
/// </summary>
public static class PlanGeneratorRules
{
    public enum Focus { General, Strength, Hypertrophy, FatLoss, Conditioning }
    public enum Kit { Gym, Dumbbells, Bodyweight }

    public sealed record Options(int DaysPerWeek, int Minutes, Focus Focus, Kit Kit);

    public sealed record Candidate(int Id, string Name, string PrimaryMuscles, string SecondaryMuscles, ExerciseCategory Category,
        string? Equipment, string? Force, string? Mechanic, ExerciseLevel Level, ExerciseTracking? Tracking);

    /// <summary>Historia ćwiczenia u klienta: ile razy, ostatni najcięższy zestaw i średnie RPE z ankiet po tych treningach.</summary>
    public sealed record History(int ExerciseId, int TimesDone, decimal LastWeightKg, int LastReps, decimal? AvgRpe, DateOnly LastDate);

    public sealed record Pick(int ExerciseId, string Name, int Sets, string? Reps, int? DurationSeconds, decimal? WeightKg, int RestSeconds,
        string Why, bool Familiar);

    public sealed record Day(string Label, List<Pick> Exercises);

    public sealed record Result(string Name, string Purpose, Focus Focus, List<Day> Days, List<string> Notes);

    private sealed record Slot(string Key, string Label, string[] Muscles, string? Force, bool Main);

    private static readonly Slot Squat = new("squat", "przysiad", ["quadriceps"], "push", true);
    private static readonly Slot Hinge = new("hinge", "zawias biodrowy", ["hamstrings", "glutes", "lower back"], "pull", true);
    private static readonly Slot Lunge = new("lunge", "jednonóż", ["quadriceps", "glutes"], "push", false);
    private static readonly Slot HPush = new("hpush", "wyciskanie poziome", ["chest"], "push", true);
    private static readonly Slot VPush = new("vpush", "wyciskanie nad głowę", ["shoulders"], "push", true);
    private static readonly Slot HPull = new("hpull", "przyciąganie poziome", ["middle back", "lats"], "pull", true);
    private static readonly Slot VPull = new("vpull", "przyciąganie pionowe", ["lats"], "pull", true);
    private static readonly Slot Biceps = new("biceps", "biceps", ["biceps"], "pull", false);
    private static readonly Slot Triceps = new("triceps", "triceps", ["triceps"], "push", false);
    private static readonly Slot Delts = new("delts", "barki", ["shoulders"], null, false);
    private static readonly Slot Hams = new("hams", "tył uda", ["hamstrings"], "pull", false);
    private static readonly Slot Calves = new("calves", "łydki", ["calves"], null, false);
    private static readonly Slot Core = new("core", "brzuch", ["abdominals"], null, false);
    private static readonly Slot Glutes = new("glutes", "pośladki", ["glutes"], null, false);

    private static readonly Slot[] FullBody = [Squat, HPush, HPull, Hinge, VPush, Core, Biceps, Triceps];
    private static readonly Slot[] Upper = [HPush, HPull, VPush, VPull, Biceps, Triceps, Delts];
    private static readonly Slot[] Lower = [Squat, Hinge, Lunge, Hams, Glutes, Calves, Core];
    private static readonly Slot[] Push = [HPush, VPush, HPush, Triceps, Delts, Core];
    private static readonly Slot[] Pull = [VPull, HPull, Hinge, Biceps, Delts, Core];

    /// <summary>Cel z karty klienta → profil planu (proste słowa kluczowe, po polsku).</summary>
    public static Focus FocusFromGoal(string? goal)
    {
        var g = (goal ?? "").ToLowerInvariant();
        if (g.Length == 0) return Focus.General;
        if (new[] { "schud", "redukc", "kilogram", "waga", "wagi", "tłuszcz", "spal", "odchudz" }.Any(g.Contains)) return Focus.FatLoss;
        if (new[] { "kondycj", "wytrzymał", "bieg", "biec", " km", "maraton", "cardio", "kardio", "zadysz" }.Any(g.Contains)) return Focus.Conditioning;
        if (new[] { "masa", "masy", "mięśn", "rzeźb", "sylwetk", "hipertrof", "przyrost" }.Any(g.Contains)) return Focus.Hypertrophy;
        if (new[] { "sił", "silniejs", "trójbój", "rekord", "martwy", "przysiad" }.Any(g.Contains)) return Focus.Strength;
        return Focus.General;
    }

    public static string FocusLabel(Focus f) => f switch
    {
        Focus.Strength => "siła",
        Focus.Hypertrophy => "masa mięśniowa",
        Focus.FatLoss => "redukcja",
        Focus.Conditioning => "kondycja",
        _ => "ogólna sprawność"
    };

    /// <summary>Ile ćwiczeń zmieści się w treningu danej długości (z rozgrzewką).</summary>
    public static int ExercisesFor(int minutes) => minutes switch
    {
        <= 35 => 4,
        <= 50 => 5,
        <= 65 => 6,
        _ => 7
    };

    /// <summary>Podział tygodnia: nazwy dni i ich sloty.</summary>
    private static List<(string Label, Slot[] Slots)> Split(int days, Focus focus)
    {
        var d = Math.Clamp(days, 1, 6);
        var heavy = focus is Focus.Strength or Focus.Hypertrophy;
        return d switch
        {
            1 => [("Całe ciało", FullBody)],
            2 => [("Całe ciało A", FullBody), ("Całe ciało B", FullBody)],
            3 when heavy => [("Push — klatka, barki, triceps", Push), ("Pull — plecy, biceps", Pull), ("Nogi", Lower)],
            3 => [("Całe ciało A", FullBody), ("Całe ciało B", FullBody), ("Całe ciało C", FullBody)],
            4 => [("Góra A", Upper), ("Dół A", Lower), ("Góra B", Upper), ("Dół B", Lower)],
            5 => [("Góra", Upper), ("Dół", Lower), ("Push", Push), ("Pull", Pull), ("Nogi", Lower)],
            _ => [("Push A", Push), ("Pull A", Pull), ("Nogi A", Lower), ("Push B", Push), ("Pull B", Pull), ("Nogi B", Lower)]
        };
    }

    private static (int Sets, string Reps, int RestSeconds) Scheme(Focus f, bool main) => (f, main) switch
    {
        (Focus.Strength, true) => (4, "4-6", 150),
        (Focus.Strength, false) => (3, "8-10", 90),
        (Focus.Hypertrophy, true) => (4, "6-10", 120),
        (Focus.Hypertrophy, false) => (3, "10-12", 75),
        (Focus.FatLoss, _) => (3, "10-15", 60),
        (Focus.Conditioning, _) => (3, "12-15", 45),
        (_, true) => (3, "8-10", 90),
        _ => (3, "10-12", 60)
    };

    private static readonly HashSet<string> GymOnly = new(StringComparer.OrdinalIgnoreCase) { "barbell", "machine", "cable", "e-z curl bar" };
    private static readonly HashSet<string> DumbbellKit = new(StringComparer.OrdinalIgnoreCase) { "dumbbell", "body only", "kettlebells", "bands", "exercise ball", "medicine ball" };

    private static bool KitAllows(Kit kit, string? equipment)
    {
        var e = (equipment ?? "").Trim();
        if (e is "foam roll") return false;
        return kit switch
        {
            Kit.Gym => true,
            Kit.Dumbbells => e.Length == 0 || DumbbellKit.Contains(e),
            _ => e.Length == 0 || e.Equals("body only", StringComparison.OrdinalIgnoreCase) || e.Equals("bands", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string[] Csv(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Buduje propozycję. <paramref name="conflict"/> zwraca nazwę obszaru (np. „Kolano”), gdy ćwiczenie koliduje
    /// z ankietą zdrowotną albo zgłoszonym bólem — takie pomijamy i zapisujemy w uwagach.
    /// </summary>
    public static Result Build(Options o, IReadOnlyList<Candidate> catalog, IReadOnlyDictionary<int, History> history, Func<Candidate, string?> conflict)
    {
        var notes = new List<string>();
        var skipped = new Dictionary<string, HashSet<string>>();
        var usable = catalog.Where(c => c.Category is ExerciseCategory.Strength or ExerciseCategory.Powerlifting or ExerciseCategory.Plyometrics or ExerciseCategory.Cardio
                                     && KitAllows(o.Kit, c.Equipment)
                                     && Csv(c.PrimaryMuscles).Length > 0).ToList();
        var safe = new List<Candidate>();
        foreach (var c in usable)
        {
            if (conflict(c) is { } area)
            {
                if (history.ContainsKey(c.Id) || c.Level != ExerciseLevel.Expert)
                    (skipped.TryGetValue(area, out var set) ? set : skipped[area] = []).Add(c.Name);
                continue;
            }
            safe.Add(c);
        }

        var perDay = ExercisesFor(o.Minutes);
        var days = new List<Day>();
        var usedInPlan = new HashSet<int>();
        var split = Split(o.DaysPerWeek, o.Focus);
        for (var di = 0; di < split.Count; di++)
        {
            var (label, slots) = split[di];
            var picks = new List<Pick>();
            var usedToday = new HashSet<int>();
            foreach (var slot in slots)
            {
                if (picks.Count >= perDay) break;
                var best = safe
                    .Where(c => !usedToday.Contains(c.Id) && Csv(c.PrimaryMuscles).Intersect(slot.Muscles, StringComparer.OrdinalIgnoreCase).Any())
                    .Where(c => o.Focus == Focus.Conditioning || c.Category != ExerciseCategory.Cardio)
                    .Select(c => (c, score: Score(c, slot, history, usedInPlan, di)))
                    .OrderByDescending(x => x.score).ThenBy(x => x.c.Id)
                    .Select(x => x.c).FirstOrDefault();
                if (best is null) continue;
                usedToday.Add(best.Id);
                usedInPlan.Add(best.Id);
                picks.Add(MakePick(best, slot, o.Focus, history.GetValueOrDefault(best.Id)));
            }
            if (picks.Count > 0) days.Add(new Day(label, picks));
        }

        foreach (var (area, names) in skipped)
            notes.Add($"{area}: pominięto {string.Join(", ", names.Take(3))}{(names.Count > 3 ? " i inne" : "")} — z ankiety zdrowotnej lub zgłoszonego bólu.");
        var familiar = days.SelectMany(d => d.Exercises).Count(p => p.Familiar);
        var total = days.Sum(d => d.Exercises.Count);
        if (total > 0 && familiar == 0) notes.Add("Brak historii ćwiczeń — ciężary dobierz na pierwszym treningu.");
        else if (familiar < total) notes.Add($"{total - familiar} z {total} ćwiczeń to nowości — ciężar dobierz na pierwszym treningu.");

        var name = $"Plan {FocusLabel(o.Focus)} · {days.Count} {DaysWord(days.Count)}";
        var purpose = Purpose(o, days.Count);
        return new Result(name, purpose, o.Focus, days, notes);
    }

    private static int Score(Candidate c, Slot slot, IReadOnlyDictionary<int, History> history, HashSet<int> usedInPlan, int dayIndex)
    {
        var score = 0;
        if (history.TryGetValue(c.Id, out var h)) score += 40 + Math.Min(h.TimesDone, 10) * 2;
        var primary = Csv(c.PrimaryMuscles);
        if (primary.Length > 0 && slot.Muscles.Contains(primary[0], StringComparer.OrdinalIgnoreCase)) score += 12;
        if (slot.Main == string.Equals(c.Mechanic, "compound", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (slot.Force is not null && string.Equals(c.Force, slot.Force, StringComparison.OrdinalIgnoreCase)) score += 5;
        score += c.Level switch { ExerciseLevel.Beginner => 6, ExerciseLevel.Intermediate => 4, _ => h is null ? -15 : 0 };
        // Te same ćwiczenia w kolejnych dniach (A/B) tylko gdy nie ma innych — plan ma różnorodność.
        if (usedInPlan.Contains(c.Id)) score -= dayIndex > 0 ? 25 : 0;
        if (c.Category == ExerciseCategory.Plyometrics) score -= 6;
        return score;
    }

    private static Pick MakePick(Candidate c, Slot slot, Focus focus, History? h)
    {
        var (sets, reps, rest) = Scheme(focus, slot.Main);
        if (c.Tracking is ExerciseTracking.Time)
            return new Pick(c.Id, c.Name, sets, null, focus == Focus.Conditioning ? 45 : 40, null, Math.Min(rest, 60),
                h is null ? $"{slot.Label} · nowe" : $"{slot.Label} · znane ({h.TimesDone}×)", h is not null);
        if (h is null || h.LastWeightKg <= 0m)
        {
            var why = h is null ? $"{slot.Label} · nowe — dobierz ciężar na pierwszym treningu" : $"{slot.Label} · własnym ciężarem ciała, ostatnio {h.LastReps} powt.";
            return new Pick(c.Id, c.Name, sets, reps, null, null, rest, why, h is not null);
        }
        var weight = NextWeight(h, c, reps, out var how);
        return new Pick(c.Id, c.Name, sets, reps, null, weight, rest, $"{slot.Label} · ostatnio {Kg(h.LastWeightKg)} × {h.LastReps}{Rpe(h.AvgRpe)} → {how}", true);
    }

    /// <summary>Ciężar startowy: +krok przy RPE ≤ 7 i domkniętym zakresie, −~10% przy RPE ≥ 9, inaczej bez zmian.</summary>
    public static decimal NextWeight(History h, Candidate c, string reps, out string how)
    {
        var range = ProgressionRules.ParseReps(reps);
        var step = ProgressionRules.Increment(h.LastWeightKg, c.PrimaryMuscles, c.Mechanic, c.Equipment);
        if (h.AvgRpe is decimal r && r >= ProgressionRules.HardRpe)
        {
            var w = ProgressionRules.DeloadWeight(h.LastWeightKg, step);
            how = $"{Kg(w)} (lżej — było ciężko)";
            return w;
        }
        if (h.AvgRpe is decimal e && e <= ProgressionRules.EasyRpe && (range is null || h.LastReps >= range.Value.Min))
        {
            var w = h.LastWeightKg + step;
            how = $"{Kg(w)} (+{Kg(step)})";
            return w;
        }
        how = $"{Kg(h.LastWeightKg)} (bez zmian)";
        return h.LastWeightKg;
    }

    private static string Purpose(Options o, int days) => o.Focus switch
    {
        Focus.Strength => $"Przez najbliższe 6–8 tygodni budujemy siłę w głównych ćwiczeniach: {days} {DaysWord(days)} w tygodniu po ok. {o.Minutes} min, mało powtórzeń i dłuższe przerwy. Ciężar rośnie, gdy serie wchodzą lekko.",
        Focus.Hypertrophy => $"Przez 6–8 tygodni pracujemy nad masą mięśniową: {days} {DaysWord(days)} w tygodniu po ok. {o.Minutes} min, średnie zakresy powtórzeń i kontrolowane tempo. Dokładamy, gdy górna granica powtórzeń wchodzi swobodnie.",
        Focus.FatLoss => $"Plan wspiera redukcję: {days} {DaysWord(days)} w tygodniu po ok. {o.Minutes} min, więcej powtórzeń i krótsze przerwy, żeby utrzymać tętno i siłę.",
        Focus.Conditioning => $"Budujemy kondycję: {days} {DaysWord(days)} w tygodniu po ok. {o.Minutes} min, krótkie przerwy i ćwiczenia całego ciała.",
        _ => $"Ogólna sprawność i siła całego ciała: {days} {DaysWord(days)} w tygodniu po ok. {o.Minutes} min. Ciężary rosną stopniowo, gdy ćwiczenia wchodzą lekko."
    };

    public static string DaysWord(int n) => n == 1 ? "dzień" : "dni";

    private static string Kg(decimal kg) => kg.ToString("0.##", CultureInfo.GetCultureInfo("pl-PL")) + " kg";

    private static string Rpe(decimal? rpe) => rpe is decimal r ? $" (RPE {r.ToString("0.#", CultureInfo.GetCultureInfo("pl-PL"))})" : "";
}
