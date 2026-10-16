using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IClientService
{
    Task<List<ClientDto>> GetClientsAsync(string? trainerUserId = null);
    Task<ClientDto?> GetClientAsync(int id);
    Task<ClientDto?> GetClientByUserIdAsync(string userId);
    Task<ClientDto> CreateClientAsync(CreateClientDto dto);
    /// <summary>Dodanie klienta z formularza: sprawdza adres, duplikaty i przypisuje trenera — bez wyjątków, z komunikatem.</summary>
    Task<ClientCreateResult> AddClientAsync(CreateClientDto dto);
    /// <summary>Czy adres jest już zajęty i przez kogo (null — wolny).</summary>
    Task<EmailOwnerDto?> FindEmailOwnerAsync(string email);
    Task UpdateClientAsync(int id, UpdateClientDto dto);
    Task UpdateProfilePictureAsync(int id, string pictureUrl);
    Task<List<TrainerNoteDto>> GetNotesAsync(int clientId);
    Task AddNoteAsync(int clientId, string trainerUserId, string content);
    Task DeleteNoteAsync(int noteId);
    Task ApproveClientAsync(int clientId);
    Task<List<ClientDto>> GetPendingClientsAsync(string? trainerUserId = null);
    Task SetAllowSelfBookingAsync(int clientId, bool allow);
    Task<CsvImportResult> ImportClientsFromCsvAsync(Stream csvStream, string? trainerUserId = null);
}

public class CsvImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
}
