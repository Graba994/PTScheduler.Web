namespace PTScheduler.Domain.Entities;

/// <summary>
/// Pozycja ćwiczenia w dniu planu: ile serii/powtórzeń, docelowy ciężar,
/// tempo, przerwa. Powtórzenia jako tekst, żeby dopuścić zakresy („8-12").
/// </summary>
public class PlanExercise
{
    public int Id { get; set; }

    public int PlanDayId { get; set; }
    public PlanDay? PlanDay { get; set; }

    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    public int Order { get; set; }

    public int Sets { get; set; }
    /// <summary>Powtórzenia — tekst, aby dopuścić zakres (np. „8-12", „do upadku").</summary>
    public string? Reps { get; set; }
    public decimal? TargetWeightKg { get; set; }
    /// <summary>Cel czasowy serii (deska 45 s, bieg 30 min) — dla ćwiczeń mierzonych czasem.</summary>
    public int? TargetDurationSeconds { get; set; }
    /// <summary>Cel dystansu (bieg 5 km, spacer farmera 40 m).</summary>
    public decimal? TargetDistanceMeters { get; set; }
    public string? Tempo { get; set; }
    public int? RestSeconds { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Data treningu, dla którego trener już rozpatrzył propozycję progresji
    /// (zastosował albo pominął) — kolejna pojawi się dopiero po nowym treningu.
    /// </summary>
    public DateOnly? ProgressionReviewedFor { get; set; }
}
