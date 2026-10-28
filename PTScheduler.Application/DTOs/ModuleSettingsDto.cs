namespace PTScheduler.Application.DTOs;

/// <summary>
/// Which optional platform modules are enabled for clients. Stored as a JSON
/// blob in the persistent branding volume (no migration). Extend as modules grow.
/// </summary>
public class ModuleSettingsDto
{
    // Training portal (courses) visible to clients.
    public bool CoursesEnabled { get; set; } = true;

    // Automatyczne przypomnienia klientom o kończącym się / wygasającym pakiecie.
    public bool PackageRemindersEnabled { get; set; } = true;

    // Karta „Twoje postępy” na pulpicie klienta (siła, ciało, liczba treningów; tekst może ułożyć AI).
    public bool ClientProgressSummariesEnabled { get; set; } = true;

    // Zegarki klientów (Garmin): powiadomienie z podsumowaniem treningu dopiętego do wizyty (czas, kalorie, tętno).
    public bool WearableSessionSummaryEnabled { get; set; }

    // Zegarki klientów (Garmin): poniedziałkowy raport tygodnia dla klienta (sen, kroki, aktywności, tętno spoczynkowe).
    public bool WearableWeeklyReportEnabled { get; set; }

    // Źródło publicznego katalogu ćwiczeń (Free Exercise DB albo wger.de).
    public PTScheduler.Domain.Enums.ExerciseSource ExerciseCatalogSource { get; set; } = PTScheduler.Domain.Enums.ExerciseSource.FreeExerciseDb;

    // Ostatnia udana aktualizacja z wger.de i liczba pobranych ćwiczeń.
    public DateTime? WgerSyncedAt { get; set; }
    public int WgerExerciseCount { get; set; }
}
