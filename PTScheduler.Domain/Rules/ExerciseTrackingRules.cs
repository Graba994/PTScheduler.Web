using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Automatyczne przypisanie typu pomiaru ćwiczeniom z katalogu (na podstawie kategorii,
/// sprzętu i angielskiej nazwy z Free Exercise DB). Trener może to zmienić w edytorze.
/// </summary>
public static class ExerciseTrackingRules
{
    private static readonly string[] HoldWords =
        ["plank", "side bridge", "isometric", "hold", "wall sit", "l-sit", "dead hang", "crucifix", "static"];
    private static readonly string[] CarryWords =
        ["walk", "carry", "drag", "sled push", "prowler", "yoke"];
    private static readonly string[] TimedCardioWords =
        ["rope jumping", "jump rope", "stairmaster", "step mill", "elliptical"];

    public static ExerciseTracking Guess(ExerciseCategory category, string? equipment, string? nameEn, string? namePl = null)
    {
        var name = $"{nameEn} {namePl}".ToLowerInvariant();
        var eq = (equipment ?? "").Trim().ToLowerInvariant();
        bool Has(IEnumerable<string> words) => words.Any(name.Contains);

        switch (category)
        {
            case ExerciseCategory.Cardio:
                return Has(TimedCardioWords) || name.Contains("skakank") ? ExerciseTracking.Time : ExerciseTracking.DistanceTime;
            case ExerciseCategory.Stretching:
                return ExerciseTracking.Time;
            case ExerciseCategory.Strongman:
                return Has(CarryWords) ? ExerciseTracking.WeightDistance
                    : Has(HoldWords) ? ExerciseTracking.Time
                    : ExerciseTracking.WeightReps;
        }

        if (Has(HoldWords) || name.Contains("deska") || name.Contains("izometri")) return ExerciseTracking.Time;
        if (name.Contains("farmer") || name.Contains("sled")) return ExerciseTracking.WeightDistance;

        var bodyweight = eq is "" or "body only" or "none";
        if (category == ExerciseCategory.Plyometrics)
            return eq is "medicine ball" or "dumbbell" or "kettlebells" ? ExerciseTracking.WeightReps : ExerciseTracking.Reps;
        return bodyweight ? ExerciseTracking.Reps : ExerciseTracking.WeightReps;
    }

    /// <summary>Etykieta dla trenera.</summary>
    public static string Label(ExerciseTracking t) => t switch
    {
        ExerciseTracking.Reps => "Powtórzenia (masa ciała)",
        ExerciseTracking.Time => "Czas",
        ExerciseTracking.DistanceTime => "Dystans i czas",
        ExerciseTracking.WeightDistance => "Ciężar i dystans",
        _ => "Ciężar i powtórzenia"
    };

    public static bool UsesReps(ExerciseTracking t) => t is ExerciseTracking.WeightReps or ExerciseTracking.Reps;
    public static bool UsesWeight(ExerciseTracking t) => t is ExerciseTracking.WeightReps or ExerciseTracking.WeightDistance;
    public static bool UsesTime(ExerciseTracking t) => t is ExerciseTracking.Time or ExerciseTracking.DistanceTime;
    public static bool UsesDistance(ExerciseTracking t) => t is ExerciseTracking.DistanceTime or ExerciseTracking.WeightDistance;

    /// <summary>„1:05” / „45 s”.</summary>
    public static string FormatDuration(int seconds) =>
        seconds >= 60 ? $"{seconds / 60}:{seconds % 60:00}" : $"{seconds} s";

    /// <summary>„800 m” / „5,2 km”.</summary>
    public static string FormatDistance(decimal meters) =>
        meters >= 1000 ? $"{meters / 1000m:0.##} km".Replace('.', ',') : $"{meters:0} m";
}
