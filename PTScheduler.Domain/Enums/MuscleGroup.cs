namespace PTScheduler.Domain.Enums;

/// <summary>
/// Partia mięśniowa — słownik zgodny z Free Exercise DB (pola
/// „primaryMuscles" / „secondaryMuscles"). Służy do agregacji objętości
/// treningowej „per partia". Na encji ćwiczenia mięśnie trzymamy jako CSV
/// kanonicznych kluczy (patrz <c>Domain.Rules.Muscles</c>); ten enum jest
/// warstwą prezentacji i wykresów. Nie zmieniać kolejności istniejących
/// pozycji.
/// </summary>
public enum MuscleGroup
{
    Abdominals,
    Abductors,
    Adductors,
    Biceps,
    Calves,
    Chest,
    Forearms,
    Glutes,
    Hamstrings,
    Lats,
    LowerBack,
    MiddleBack,
    Neck,
    Quadriceps,
    Shoulders,
    Traps,
    Triceps
}
