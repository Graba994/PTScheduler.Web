using System.Globalization;
using System.Text.Json.Nodes;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Wearables;

/// <summary>
/// Dzień planu → trening siłowy dla Garmin Training API V2 (jeden segment STRENGTH_TRAINING). Każde ćwiczenie to
/// powtórzenie (WorkoutRepeatStep) z krokiem ćwiczenia i przerwą; jedna seria — sam krok. Ćwiczenie dopasowane
/// do katalogu Garmina dostaje kategorię i nazwę (zegarek liczy powtórzenia), inne — polską nazwę w opisie kroku.
/// </summary>
public static class GarminWorkoutBuilder
{
    /// <summary>Limit kroków w treningu jednej dyscypliny (wliczając bloki powtórzeń).</summary>
    public const int MaxSteps = 100;
    public const string Provider = "Hantly";

    public sealed record Item(string NameEn, string NamePl, ExerciseTracking Tracking, int Sets, string? Reps,
        decimal? WeightKg, int? DurationSeconds, decimal? DistanceMeters, int? RestSeconds, string? Notes);

    public sealed record Result(JsonObject? Workout, int Steps, IReadOnlyList<string> Unmatched, string? Error);

    public static Result Build(string name, string? description, IReadOnlyList<Item> items)
    {
        if (items.Count == 0) return new(null, 0, [], "Ten dzień planu nie ma ćwiczeń.");
        var steps = new JsonArray();
        var unmatched = new List<string>();
        var order = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var match = GarminExerciseMap.Find(it.NameEn, it.NamePl);
            if (match is null) unmatched.Add(it.NamePl);
            var last = i == items.Count - 1;
            var sets = Math.Clamp(it.Sets, 1, 50);
            if (sets > 1)
            {
                var repeat = new JsonObject
                {
                    ["type"] = "WorkoutRepeatStep",
                    ["stepOrder"] = ++order,
                    ["repeatType"] = "REPEAT_UNTIL_STEPS_CMPLT",
                    ["repeatValue"] = sets,
                    ["skipLastRestStep"] = true,
                };
                var inner = new JsonArray { Exercise(it, match, ++order), Rest(it.RestSeconds, ++order) };
                repeat["steps"] = inner;
                steps.Add(repeat);
            }
            else
            {
                steps.Add(Exercise(it, match, ++order));
                if (!last) steps.Add(Rest(it.RestSeconds, ++order));
            }
        }
        if (order > MaxSteps)
            return new(null, order, unmatched, $"Za dużo kroków dla zegarka ({order}, najwięcej {MaxSteps}) — podziel ten dzień na dwa treningi.");

        var workout = new JsonObject
        {
            ["workoutName"] = Cut(name, 60),
            ["description"] = string.IsNullOrWhiteSpace(description) ? null : Cut(description, 1024),
            ["sport"] = "STRENGTH_TRAINING",
            ["workoutProvider"] = Provider,
            ["workoutSourceId"] = Provider,
            ["isSessionTransitionEnabled"] = false,
            ["segments"] = new JsonArray
            {
                new JsonObject { ["segmentOrder"] = 1, ["sport"] = "STRENGTH_TRAINING", ["steps"] = steps }
            }
        };
        return new(workout, order, unmatched, null);
    }

    private static JsonObject Exercise(Item it, GarminExerciseMap.Match? match, int order)
    {
        var (durationType, durationValue) = Duration(it);
        var step = new JsonObject
        {
            ["type"] = "WorkoutStep",
            ["stepOrder"] = order,
            ["intensity"] = "ACTIVE",
            ["description"] = Cut(Describe(it), 512),
            ["durationType"] = durationType,
            ["durationValue"] = durationValue,
            ["targetType"] = "OPEN",
            ["exerciseCategory"] = match?.Category,
            ["exerciseName"] = match?.Name,
        };
        if (durationType == "DISTANCE") step["durationValueType"] = "METER";
        if (it.WeightKg is > 0 && it.Tracking is ExerciseTracking.WeightReps or ExerciseTracking.WeightDistance)
        {
            step["weightValue"] = (double)it.WeightKg.Value;
            step["weightDisplayUnit"] = "KILOGRAM";
        }
        return step;
    }

    /// <summary>Przerwa: czas z planu albo do naciśnięcia przycisku okrążenia (OPEN).</summary>
    private static JsonObject Rest(int? seconds, int order) => new()
    {
        ["type"] = "WorkoutStep",
        ["stepOrder"] = order,
        ["intensity"] = "REST",
        ["durationType"] = seconds is > 0 ? "TIME" : "OPEN",
        ["durationValue"] = seconds is > 0 ? seconds.Value : null,
        ["targetType"] = "OPEN",
    };

    /// <summary>Powtórzenia (pierwsza liczba z „8-10”), czas albo dystans z planu; „max”/brak — do przycisku okrążenia.</summary>
    public static (string Type, double? Value) Duration(Item it)
    {
        switch (it.Tracking)
        {
            case ExerciseTracking.Time when it.DurationSeconds is > 0:
                return ("TIME", it.DurationSeconds.Value);
            case ExerciseTracking.DistanceTime or ExerciseTracking.WeightDistance when it.DistanceMeters is > 0:
                return ("DISTANCE", (double)it.DistanceMeters.Value);
            case ExerciseTracking.DistanceTime when it.DurationSeconds is > 0:
                return ("TIME", it.DurationSeconds.Value);
        }
        if (FirstNumber(it.Reps) is int reps and > 0) return ("REPS", reps);
        if (it.DurationSeconds is > 0) return ("TIME", it.DurationSeconds.Value);
        return ("OPEN", null);
    }

    private static int? FirstNumber(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var digits = new string(s.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>Opis kroku: polska nazwa, zakres powtórzeń/tempo, uwagi trenera — widoczne na zegarku i w Garmin Connect.</summary>
    private static string Describe(Item it)
    {
        var parts = new List<string> { it.NamePl.Trim() };
        if (!string.IsNullOrWhiteSpace(it.Reps) && it.Reps.Trim().Any(c => !char.IsDigit(c))) parts.Add(it.Reps.Trim());
        if (!string.IsNullOrWhiteSpace(it.Notes)) parts.Add(it.Notes.Trim());
        return string.Join(" · ", parts);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
