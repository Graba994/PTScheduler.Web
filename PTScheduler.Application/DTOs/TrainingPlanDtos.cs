namespace PTScheduler.Application.DTOs;

/// <summary>Pozycja na liście planów trenera.</summary>
public sealed class TrainingPlanListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsTemplate { get; set; }
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public int DayCount { get; set; }
    public int ExerciseCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public PTScheduler.Domain.Enums.PlanUse Use { get; set; }
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    /// <summary>Ostatni zapisany trening z tego planu (dziennik klienta).</summary>
    public DateOnly? LastWorkout { get; set; }
    /// <summary>Najbliższa wizyta, na którą wybrano dzień z tego planu.</summary>
    public DateTime? NextSessionAt { get; set; }
}

/// <summary>Dzień planu przypisany do wizyty (pokazywany klientowi i trenerowi przy wizycie).</summary>
public sealed record SessionPlanDayDto(int SessionId, int PlanId, int PlanDayId, string PlanName, string DayLabel, int ExerciseCount);

/// <summary>Pełny plan do edycji w kreatorze (plan → dni → ćwiczenia).</summary>
public sealed class PlanEditDto
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>„Po co ten plan” — widzi klient.</summary>
    public string? Purpose { get; set; }
    public bool IsTemplate { get; set; }
    public int? ClientId { get; set; }
    /// <summary>Z trenerem / samodzielnie / oba.</summary>
    public PTScheduler.Domain.Enums.PlanUse Use { get; set; }
    public List<PlanDayEditDto> Days { get; set; } = [];
}

public sealed class PlanDayEditDto
{
    public int? Id { get; set; }
    public int Order { get; set; }
    public string Label { get; set; } = string.Empty;
    public List<PlanExerciseEditDto> Exercises { get; set; } = [];
}

public sealed class PlanExerciseEditDto
{
    public int? Id { get; set; }
    public int ExerciseId { get; set; }
    // Pola pomocnicze do wyświetlenia w kreatorze (nie zapisywane wprost).
    public string ExerciseNamePl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public PTScheduler.Domain.Enums.ExerciseTracking Tracking { get; set; }

    public int Order { get; set; }
    public int Sets { get; set; } = 3;
    public string? Reps { get; set; }
    public decimal? TargetWeightKg { get; set; }
    public int? TargetDurationSeconds { get; set; }
    public decimal? TargetDistanceMeters { get; set; }
    public string? Tempo { get; set; }
    public int? RestSeconds { get; set; }
    public string? Notes { get; set; }
}
