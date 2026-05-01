using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IClientService
{
    Task<List<ClientDto>> GetClientsAsync();
    Task<ClientDto?> GetClientAsync(int id);
    Task<ClientDto?> GetClientByUserIdAsync(string userId);
    Task<ClientDto> CreateClientAsync(CreateClientDto dto);
    Task UpdateClientAsync(int id, UpdateClientDto dto);
    Task UpdateProfilePictureAsync(int id, string pictureUrl);
    Task<List<TrainerNoteDto>> GetNotesAsync(int clientId);
    Task AddNoteAsync(int clientId, string trainerUserId, string content);
    Task DeleteNoteAsync(int noteId);
    Task ApproveClientAsync(int clientId);
    Task<List<ClientDto>> GetPendingClientsAsync();
    Task SetAllowSelfBookingAsync(int clientId, bool allow);
}
