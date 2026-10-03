namespace PTScheduler.Domain.Enums;

/// <summary>
/// Poziom trudności ćwiczenia — słownik z Free Exercise DB (pole „level").
/// Wartości trafiają do bazy jako int; nie zmieniać kolejności istniejących.
/// </summary>
public enum ExerciseLevel
{
    Beginner,
    Intermediate,
    Expert
}
