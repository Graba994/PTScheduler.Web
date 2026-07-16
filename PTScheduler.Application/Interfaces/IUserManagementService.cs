using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IUserManagementService
{
    Task<List<UserDto>> GetAllUsersAsync();
    Task<UserDto?> GetUserAsync(string id);
    Task<List<UserDto>> GetTrainersAsync();
    Task SetRoleAsync(string userId, string role);
    Task SetSupervisorAsync(string userId, string? supervisorId);
    Task SetLockoutAsync(string userId, bool locked);
    Task DeleteUserAsync(string userId);
    Task<(bool Success, string? Error)> CreateUserAsync(CreateUserDto dto);
    Task<(bool Success, string? Error)> ResetPasswordAsync(string userId, string newPassword);
    Task<(bool Success, string? Error)> UpdateProfileAsync(string userId, string firstName, string lastName, string email);
    Task<List<ClientSummaryDto>> GetClientsByTrainerAsync(string trainerUserId);
    Task ReassignClientsAsync(IEnumerable<int> clientIds, string newTrainerUserId);
}
