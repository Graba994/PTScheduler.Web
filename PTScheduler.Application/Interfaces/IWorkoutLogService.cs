using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Dziennik wykonanych treningów klienta. Zapis następuje po zakończeniu sesji
/// (do tego czasu postęp żyje w localStorage przeglądarki — patrz /train).
/// </summary>
public interface IWorkoutLogService
{
    /// <summary>Zapisuje wykonany trening (WorkoutLog + serie). Zwraca liczbę zapisanych serii.</summary>
    Task<int> LogWorkoutAsync(int clientId, LogWorkoutDto dto);

    /// <summary>Ostatnie treningi klienta (data, liczba ćwiczeń/serii, objętość) — pod dziennik.</summary>
    Task<List<WorkoutHistoryItemDto>> GetRecentForClientAsync(int clientId, int take = 20);

    // --- Statystyki i dziennik (Faza 5) ---

    /// <summary>Objętość dzienna w oknie ostatnich `days` dni (do wykresu liniowego).</summary>
    Task<List<VolumePointDto>> GetVolumeOverTimeAsync(int clientId, int days = 90);

    /// <summary>Objętość „per partia" w oknie ostatnich `days` dni (malejąco).</summary>
    Task<List<MuscleVolumeDto>> GetVolumeByMuscleAsync(int clientId, int days = 90);

    /// <summary>Rekordy życiowe klienta (największy ciężar / najlepsza objętość serii).</summary>
    Task<List<PersonalRecordDto>> GetPersonalRecordsAsync(int clientId, int take = 12);

    /// <summary>Dziennik treningowy: dni z rozbiciem na ćwiczenia i serie (najnowsze pierwsze).</summary>
    Task<List<WorkoutJournalDayDto>> GetJournalAsync(int clientId, int takeDays = 20);

    /// <summary>Aktywność podopiecznych trenera w oknie ostatnich `days` dni.</summary>
    Task<List<ClientActivityDto>> GetClientsActivityAsync(string trainerUserId, int days = 30);
}
