using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Interfaces;

public interface ISessionService
{
    Task<List<SessionDto>> GetSessionsAsync(DateTime from, DateTime to, string? trainerUserId = null);
    Task<SessionDto?> GetSessionAsync(int id);
    Task<SessionDto> CreateSessionAsync(CreateSessionDto dto);
    Task UpdateStatusAsync(int id, SessionStatus status, string? cancellationReason = null);
    Task<List<SessionTypeDto>> GetSessionTypesAsync();
    Task<List<ClientSummaryDto>> GetClientsAsync();
    Task<List<SessionDto>> GetClientSessionsAsync(int clientId, int count = 20);
    Task<List<SessionDto>> GetUpcomingAsync(string? trainerUserId = null, int? clientId = null, int count = 10);
}
