using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>
/// Kreator planów treningowych: szablony i plany przypisane klientom, złożone
/// z dni i ćwiczeń. Wszystko w kontekście trenera (własność sprawdzana).
/// </summary>
public interface ITrainingPlanService
{
    Task<List<TrainingPlanListItemDto>> GetPlansAsync(string trainerUserId);

    /// <summary>Pełny plan do edycji; null gdy nie istnieje lub nie należy do trenera.</summary>
    Task<PlanEditDto?> GetForEditAsync(string trainerUserId, int planId);

    /// <summary>
    /// Tworzy nowy albo aktualizuje istniejący plan wraz z dniami i ćwiczeniami
    /// (dopasowanie po Id: aktualizacja/dodanie/usunięcie). Zwraca Id planu.
    /// Odświeża też „ostatnio używane" dla użytych ćwiczeń.
    /// </summary>
    Task<int> SavePlanAsync(string trainerUserId, PlanEditDto dto);

    Task DeletePlanAsync(string trainerUserId, int planId);

    /// <summary>Kopiuje plan (opcjonalnie jako szablon, bez przypisania klienta). Zwraca Id kopii.</summary>
    Task<int> DuplicateAsync(string trainerUserId, int planId, bool asTemplate);
}
