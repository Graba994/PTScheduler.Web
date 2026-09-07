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
}
