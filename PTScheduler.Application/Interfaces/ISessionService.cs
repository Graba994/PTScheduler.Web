using PTScheduler.Application.Scheduling;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Interfaces;

public interface ISessionService
{
    Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null, int? clientId = null);
    Task<SessionDto?> GetSessionAsync(int id);
    /// <param name="allowOverlap">
    /// Gdy false (domyślnie), kolizja z inną sesją trenera rzuca
    /// <see cref="Exceptions.SlotConflictException"/>. Trener może świadomie
    /// nałożyć terminy, przekazując true.
    /// </param>
    Task<SessionDto> CreateSessionAsync(CreateSessionDto dto, bool allowAwaitingPackage = true, bool allowOverlap = false);
    /// <param name="chargeSession">Przy odwołaniu: sesja przepada (późne odwołanie klienta).</param>
    Task UpdateStatusAsync(int id, SessionStatus status, string? cancellationReason = null, string? completionNotes = null,
        bool chargeSession = false);
    Task<List<SessionTypeDto>> GetSessionTypesAsync();
    Task<List<ClientSummaryDto>> GetClientsAsync(string? trainerUserId = null);
    Task<List<SessionDto>> GetClientSessionsAsync(int clientId, int count = 20);
    Task<List<SessionDto>> GetUpcomingAsync(string? trainerUserId = null, int? clientId = null, int count = 10);
    Task<List<SessionDto>> GetPastSessionsAsync(string? trainerUserId = null, int? clientId = null, int count = 50);
    Task<List<SessionDto>> GetAwaitingPackageAsync(string? trainerUserId = null);
    Task RescheduleAsync(int id, DateTime newStartTime, bool allowOverlap = false);
    Task RestoreAsync(int id);
    /// <summary>Czy (i na jakich warunkach) klient może teraz odwołać tę wizytę.</summary>
    Task<CancellationDecision> GetClientCancellationAsync(int id, string clientUserId);
    Task<CancellationDecision> ClientCancelSessionAsync(int id, string clientUserId, string? reason = null);
}
