namespace PTScheduler.Application.DTOs;

/// <summary>Zapis wykonanego treningu (jedna sesja = jeden dzień planu).</summary>
public sealed class LogWorkoutDto
{
    public int? PlanId { get; set; }
    public int? PlanDayId { get; set; }
    public DateOnly Date { get; set; }
    public List<LogExerciseDto> Exercises { get; set; } = [];
}

public sealed class LogExerciseDto
{
    public int ExerciseId { get; set; }
    public int? PlanExerciseId { get; set; }
    public List<LogSetDto> Sets { get; set; } = [];
}

public sealed class LogSetDto
{
    public int SetNumber { get; set; }
    public int Reps { get; set; }
    public decimal WeightKg { get; set; }
}

/// <summary>Pozycja historii treningów klienta (pod dziennik i wykresy).</summary>
public sealed class WorkoutHistoryItemDto
{
    public DateOnly Date { get; set; }
    public int ExerciseCount { get; set; }
    public int SetCount { get; set; }
    public decimal TotalVolume { get; set; }
}
