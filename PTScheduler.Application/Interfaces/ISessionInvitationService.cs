using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISessionInvitationService
{
    Task<SessionInvitationDto> SendAsync(int sessionId, int invitedClientId, string sentByUserId);
    Task<List<SessionInvitationDto>> GetForSessionAsync(int sessionId);
    Task<List<SessionInvitationDto>> GetPendingForClientAsync(int clientId);
    Task RespondAsync(int invitationId, bool accept, string? note = null);
    Task RevokeAsync(int invitationId);
}
