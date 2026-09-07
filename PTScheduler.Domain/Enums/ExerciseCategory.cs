namespace PTScheduler.Domain.Enums;

/// <summary>
/// Kategoria ćwiczenia — słownik z Free Exercise DB (pole „category").
/// Kolejność stała: wartości trafiają do bazy jako int, więc nie zmieniać
/// istniejących pozycji, tylko dopisywać nowe na końcu.
/// </summary>
public enum ExerciseCategory
{
    Strength,
    Stretching,
    Plyometrics,
    Strongman,
    Powerlifting,
    Cardio,
    OlympicWeightlifting,
    Other
}
