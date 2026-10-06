namespace PTScheduler.Domain.Entities;

/// <summary>
/// Nakładka per-trener na ćwiczenie — NIE duplikuje rekordu ćwiczenia.
/// Trzyma „interesujące mnie" (ulubione) oraz znacznik „ostatnio używane"
/// (ustawiany automatycznie z użycia w planach). Jeden wiersz na parę
/// (trener, ćwiczenie).
/// </summary>
public class TrainerExercisePref
{
    public int Id { get; set; }

    public string TrainerUserId { get; set; } = string.Empty;

    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    /// <summary>„Interesujące mnie" — ulubione / szybki wybór.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>Ostatnie użycie w planie (instant, UTC). Null = nieużywane.</summary>
    public DateTime? LastUsedAt { get; set; }
}
