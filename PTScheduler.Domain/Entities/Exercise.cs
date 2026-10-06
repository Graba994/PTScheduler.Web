using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Ćwiczenie w katalogu. Rekordy bazowe (OwnerTrainerUserId = null,
/// Visibility = Public) pochodzą z Free Exercise DB albo wger.de (zob. Source) i są współdzielone —
/// koszt właściciela ponoszony raz. Trener może dodawać własne ćwiczenia
/// (OwnerTrainerUserId = jego id, Visibility = Mine) z własnymi mediami.
///
/// Konwencja mediów: obrazy bazowe wskazują na współdzielony magazyn (URL),
/// custom trenera na Bunny/URL. Wideo bazy = wyłącznie osadzony YouTube;
/// custom = YouTube (link) lub plik na Bunny (limit GB w planie).
/// </summary>
public class Exercise
{
    public int Id { get; set; }

    /// <summary>Null = ćwiczenie bazowe/systemowe. Inaczej właściciel (trener).</summary>
    public string? OwnerTrainerUserId { get; set; }

    public ExerciseVisibility Visibility { get; set; } = ExerciseVisibility.Public;

    public string NamePl { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    /// <summary>Opis PL. EN pokazywany dodatkowo pod flagą 🇬🇧.</summary>
    public string? DescriptionPl { get; set; }
    public string? DescriptionEn { get; set; }

    /// <summary>CSV kanonicznych kluczy partii (zob. Domain.Rules.Muscles).</summary>
    public string PrimaryMuscles { get; set; } = string.Empty;
    public string SecondaryMuscles { get; set; } = string.Empty;

    public ExerciseCategory Category { get; set; } = ExerciseCategory.Strength;
    public ExerciseLevel Level { get; set; } = ExerciseLevel.Beginner;

    /// <summary>Sprzęt (np. „barbell", „dumbbell", „body only") — słownik FED.</summary>
    public string? Equipment { get; set; }
    /// <summary>Kierunek siły (push/pull/static) — słownik FED, opcjonalny.</summary>
    public string? Force { get; set; }
    /// <summary>Mechanika (compound/isolation) — słownik FED, opcjonalny.</summary>
    public string? Mechanic { get; set; }

    /// <summary>Co mierzymy w serii (ciężar+powtórzenia, czas, dystans…). Null = jeszcze nieprzypisane — uzupełnia start aplikacji.</summary>
    public ExerciseTracking? Tracking { get; set; }

    /// <summary>CSV adresów obrazów. Bazowe → współdzielony magazyn; custom → Bunny/URL.</summary>
    public string ImageUrls { get; set; } = string.Empty;

    public ExerciseVideoType VideoType { get; set; } = ExerciseVideoType.None;
    /// <summary>Dla YouTube: id/URL filmu. Dla Bunny: id wideo. Null gdy None.</summary>
    public string? VideoRef { get; set; }

    /// <summary>Klucz źródłowy (id Free Exercise DB albo „wger:{uuid}”) — dedup przy ponownym imporcie. Unikalny gdy nie-null.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Źródło rekordu bazowego; katalog pokazuje tylko ćwiczenia wybranego źródła.</summary>
    public ExerciseSource Source { get; set; } = ExerciseSource.FreeExerciseDb;

    /// <summary>Autorzy i licencja treści (wymagane przez CC-BY-SA dla wger). Null dla domeny publicznej i własnych bez źródła.</summary>
    public string? Attribution { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
