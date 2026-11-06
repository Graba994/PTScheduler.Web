namespace PTScheduler.Domain.Entities;

/// <summary>
/// Wykonanie ćwiczenia przez klienta w danym dniu. Może być powiązane z
/// pozycją planu (<see cref="PlanExerciseId"/>) albo luźne (wpis poza planem).
/// Dzień treningu to <see cref="WorkoutDate"/> (zegar ścienny / DateOnly —
/// „wtorek" jest wtorkiem niezależnie od strefy); <see cref="CreatedAt"/> to
/// instant utworzenia. Zob. IAppClock po opis konwencji czasu.
/// </summary>
public class WorkoutLog
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>Powiązanie z pozycją planu; null = wpis luźny (poza planem).</summary>
    public int? PlanExerciseId { get; set; }
    public PlanExercise? PlanExercise { get; set; }

    /// <summary>Ćwiczenie — trzymane wprost, bo wpis może być luźny (bez planu).</summary>
    public int ExerciseId { get; set; }
    public Exercise? Exercise { get; set; }

    public DateOnly WorkoutDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WorkoutSetLog> Sets { get; set; } = [];
}
