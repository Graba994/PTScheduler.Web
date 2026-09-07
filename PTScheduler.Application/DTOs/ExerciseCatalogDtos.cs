using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

/// <summary>Zakres katalogu wybierany zakładką filtra.</summary>
public enum ExerciseCatalogScope
{
    All,        // baza publiczna + moje
    Public,     // tylko baza publiczna / udostępnione
    Mine,       // tylko ćwiczenia trenera
    Favorites,  // „interesujące mnie"
    Recent      // ostatnio używane (wg LastUsedAt)
}

/// <summary>Kryteria filtrowania katalogu ćwiczeń.</summary>
public sealed class ExerciseFilterDto
{
    public string? Search { get; set; }
    public ExerciseCatalogScope Scope { get; set; } = ExerciseCatalogScope.All;
    public MuscleGroup? Muscle { get; set; }
    public ExerciseCategory? Category { get; set; }
    public ExerciseLevel? Level { get; set; }
    public string? Equipment { get; set; }
}

/// <summary>Pozycja na liście katalogu (karta).</summary>
public sealed class ExerciseListItemDto
{
    public int Id { get; set; }
    public string NamePl { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string PrimaryMuscles { get; set; } = string.Empty;
    public ExerciseCategory Category { get; set; }
    public ExerciseLevel Level { get; set; }
    public string? Equipment { get; set; }
    public string? ThumbnailUrl { get; set; }
    public bool IsMine { get; set; }
    public bool IsFavorite { get; set; }
    public bool HasVideo { get; set; }
}

/// <summary>Pełne dane ćwiczenia do widoku szczegółów.</summary>
public sealed class ExerciseDetailDto
{
    public int Id { get; set; }
    public string NamePl { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? DescriptionPl { get; set; }
    public string? DescriptionEn { get; set; }
    public string PrimaryMuscles { get; set; } = string.Empty;
    public string SecondaryMuscles { get; set; } = string.Empty;
    public ExerciseCategory Category { get; set; }
    public ExerciseLevel Level { get; set; }
    public string? Equipment { get; set; }
    public string? Force { get; set; }
    public string? Mechanic { get; set; }
    public IReadOnlyList<string> ImageUrls { get; set; } = [];
    public ExerciseVideoType VideoType { get; set; }
    public string? VideoRef { get; set; }
    public bool IsMine { get; set; }
    public bool IsFavorite { get; set; }
}

/// <summary>Zapis własnego ćwiczenia trenera (dodanie/edycja).</summary>
public sealed class SaveExerciseDto
{
    public int? Id { get; set; }
    public string NamePl { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? DescriptionPl { get; set; }
    public string? DescriptionEn { get; set; }
    public List<MuscleGroup> PrimaryMuscles { get; set; } = [];
    public List<MuscleGroup> SecondaryMuscles { get; set; } = [];
    public ExerciseCategory Category { get; set; } = ExerciseCategory.Strength;
    public ExerciseLevel Level { get; set; } = ExerciseLevel.Beginner;
    public string? Equipment { get; set; }
    public List<string> ImageUrls { get; set; } = [];
    public ExerciseVideoType VideoType { get; set; } = ExerciseVideoType.None;
    public string? VideoRef { get; set; }
}
