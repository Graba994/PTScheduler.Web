namespace PTScheduler.Domain.Enums;

/// <summary>
/// Źródło wideo instruktażowego ćwiczenia. Strategia „zero produkcji":
/// baza korzysta wyłącznie z osadzonych linków YouTube (nigdy re-hosting),
/// a trener może dołożyć własny plik na Bunny (liczony do limitu GB w planie).
/// </summary>
public enum ExerciseVideoType
{
    None,
    YouTube,
    Bunny
}
