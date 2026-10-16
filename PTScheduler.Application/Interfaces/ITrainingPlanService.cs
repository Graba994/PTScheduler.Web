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

    // --- Widok klienta (do trenowania) ---

    /// <summary>Plany przypisane danemu klientowi w kolejności ułożonej przez trenera (bez zakończonych, chyba że <paramref name="includeArchived"/>).</summary>
    Task<List<TrainingPlanListItemDto>> GetClientPlansAsync(int clientId, bool includeArchived = false);

    /// <summary>Nowa kolejność planów klienta (Id od góry). Trener — tylko swoje plany; administrator — wszystkie.</summary>
    Task ReorderClientPlansAsync(string userId, bool isAdmin, int clientId, IReadOnlyList<int> orderedPlanIds);

    /// <summary>Zakończenie planu (znika z list klienta) albo przywrócenie.</summary>
    Task SetArchivedAsync(string userId, bool isAdmin, int planId, bool archived);

    /// <summary>Wybór dnia planu na konkretną wizytę (null — kolejny dzień z planu). Fałsz, gdy wizyta albo dzień nie pasują.</summary>
    Task<bool> AssignSessionDayAsync(string userId, bool isAdmin, int sessionId, int? planDayId);
    /// <summary>
    /// Rozpisuje dni planu po kolei na tę i następne zaplanowane wizyty klienta (A → B → A…), zaczynając od wskazanego dnia.
    /// Pomija wizyty z dniem z innego planu (trener wybrał je świadomie). Zwraca liczbę ustawionych wizyt.
    /// </summary>
    Task<int> RotateUpcomingAsync(string userId, bool isAdmin, int fromSessionId, int startPlanDayId, int maxSessions = 12);

    /// <summary>Dni planu wybrane na podane wizyty.</summary>
    Task<Dictionary<int, SessionPlanDayDto>> GetSessionPlanDaysAsync(IReadOnlyCollection<int> sessionIds);

    /// <summary>Plan do wykonania przez klienta; null jeśli nie jest do niego przypisany.</summary>
    Task<PlanEditDto?> GetForWorkoutAsync(int clientId, int planId);
}
