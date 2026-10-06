using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Serwerowy backup trwającego treningu klienta (draft). Uzupełnia bufor
/// localStorage: chroni dane nawet przy utracie urządzenia i pozwala wznowić
/// trening na innym urządzeniu. Jeden otwarty draft na klienta.
/// </summary>
public interface IWorkoutSessionService
{
    /// <summary>Zapisuje/aktualizuje draft trwającego treningu (upsert po kliencie).</summary>
    Task SaveDraftAsync(int clientId, string draftJson);

    /// <summary>Zwraca otwarty draft klienta (JSON + znacznik czasu) albo null.</summary>
    Task<WorkoutSessionDraftDto?> GetOpenDraftAsync(int clientId);

    /// <summary>Usuwa otwarty draft (po „Zakończ"/„Odrzuć").</summary>
    Task DiscardAsync(int clientId);
}
