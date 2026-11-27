namespace PTScheduler.Domain.Enums;

/// <summary>
/// Skąd pochodzi publiczny katalog ćwiczeń. Administrator wybiera jedno źródło;
/// ćwiczenia z drugiego zostają w bazie (plany dalej działają), tylko znikają z katalogu.
/// </summary>
public enum ExerciseSource
{
    /// <summary>Free Exercise DB (domena publiczna) — wbudowana, z pełnym tłumaczeniem PL.</summary>
    FreeExerciseDb = 0,
    /// <summary>wger.de — otwarta baza społeczności (CC-BY-SA), pobierana przez API bez klucza.</summary>
    Wger = 1
}
