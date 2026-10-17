using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IUserManagementService
{
    Task<List<UserDto>> GetAllUsersAsync();
    Task<UserDto?> GetUserAsync(string id);
    Task<List<UserDto>> GetTrainersAsync();
    Task SetRoleAsync(string userId, string role);
    /// <summary>„Pracuję sam”: jeden trener w studiu, bez asystentów — jego profil może mieć też uprawnienia administratora.</summary>
    Task<SoloModeState> GetSoloModeAsync();
    /// <summary>Włącza/wyłącza tryb „pracuję sam”. Wyłączenie wymaga drugiego konta administratora (żeby się nie zablokować).</summary>
    Task<(bool Ok, string? Error)> SetSoloModeAsync(bool on);
    Task SetSupervisorAsync(string userId, string? supervisorId);
    Task SetLockoutAsync(string userId, bool locked);
    Task DeleteUserAsync(string userId);
    Task<(bool Success, string? Error)> CreateUserAsync(CreateUserDto dto);
    Task<(bool Success, string? Error)> ResetPasswordAsync(string userId, string newPassword);
    Task<(bool Success, string? Error)> UpdateProfileAsync(string userId, string firstName, string lastName, string email);
    Task<List<ClientSummaryDto>> GetClientsByTrainerAsync(string trainerUserId);
    Task ReassignClientsAsync(IEnumerable<int> clientIds, string newTrainerUserId);
}

public sealed record SoloModeState(bool Possible, bool Enabled, string? TrainerId, string? TrainerName, string? TrainerEmail);
