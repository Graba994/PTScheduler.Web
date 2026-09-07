using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

/// <summary>Punkt na wykresie objętości w czasie (dzień → objętość).</summary>
public sealed class VolumePointDto
{
    public DateOnly Date { get; set; }
    public decimal Volume { get; set; }
}

/// <summary>Objętość zagregowana na partię mięśniową.</summary>
public sealed class MuscleVolumeDto
{
    public MuscleGroup Muscle { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Volume { get; set; }
}

/// <summary>Rekord życiowy klienta dla ćwiczenia.</summary>
public sealed class PersonalRecordDto
{
    public int ExerciseId { get; set; }
    public string ExerciseName { get; set; } = string.Empty;
    public decimal MaxWeight { get; set; }
    public int RepsAtMax { get; set; }
    public decimal BestSetVolume { get; set; }
}

/// <summary>Dzień dziennika treningowego z rozbiciem na ćwiczenia i serie.</summary>
public sealed class WorkoutJournalDayDto
{
    public DateOnly Date { get; set; }
    public int SetCount { get; set; }
    public decimal TotalVolume { get; set; }
    public List<WorkoutJournalExerciseDto> Exercises { get; set; } = [];
}

public sealed class WorkoutJournalExerciseDto
{
    public string ExerciseName { get; set; } = string.Empty;
    public decimal Volume { get; set; }
    public List<WorkoutJournalSetDto> Sets { get; set; } = [];
}

public sealed class WorkoutJournalSetDto
{
    public int SetNumber { get; set; }
    public int Reps { get; set; }
    public decimal WeightKg { get; set; }
}

/// <summary>Wiersz aktywności podopiecznego (widok trenera).</summary>
public sealed class ClientActivityDto
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public DateOnly? LastWorkout { get; set; }
    public int WorkoutsInWindow { get; set; }
    public decimal VolumeInWindow { get; set; }
}
