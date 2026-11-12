using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.DTOs;

/// <summary>Propozycja zmiany ciężaru dla pozycji planu, wyliczona z ostatniego treningu i RPE.</summary>
public sealed class ProgressionSuggestionDto
{
    public int ClientId { get; set; }
    public int PlanExerciseId { get; set; }
    public int PlanId { get; set; }
    public string PlanName { get; set; } = "";
    public string DayLabel { get; set; } = "";
    public string ExerciseName { get; set; } = "";
    public int PlannedSets { get; set; }
    public string? Reps { get; set; }
    public decimal? CurrentTargetKg { get; set; }

    public DateOnly LastWorkoutDate { get; set; }
    /// <summary>Powtórzenia serii roboczych, np. „12 / 12 / 13".</summary>
    public string LastRepsText { get; set; } = "";
    public decimal? Rpe { get; set; }

    public ProgressionAction Action { get; set; }
    public decimal FromKg { get; set; }
    public decimal SuggestedKg { get; set; }
    public decimal DeltaKg => SuggestedKg - FromKg;
    public string Reason { get; set; } = "";
}
