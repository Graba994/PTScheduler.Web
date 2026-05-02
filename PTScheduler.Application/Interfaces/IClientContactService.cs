using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IClientContactService
{
    /// <summary>Returns the contact list visible to a given client (their pairs + peers if AllowDiscover).</summary>
    Task<List<ClientContactDto>> GetContactsForClientAsync(int clientId);

    Task<List<ClientContactDto>> GetPairsAsync(string trainerUserId);
    Task AddPairAsync(string trainerUserId, int client1Id, int client2Id);
    Task RemovePairAsync(int contactId);

    Task<List<SessionInvitationDto>> GetPendingInvitationsAsync(int clientId);
    Task CreateInvitationAsync(int sessionId, int invitedClientId, string createdByUserId);
    Task RespondToInvitationAsync(int invitationId, bool accept, string? note = null);
    Task AcceptOnBehalfAsync(int invitationId, string trainerUserId);
}
