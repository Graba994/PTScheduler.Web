using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IUserManagementService
{
    Task<List<UserDto>> GetVisibleUsersAsync(string callerRole);
    Task<UserDto?> GetUserAsync(string id, string callerRole);
    Task<List<UserDto>> GetTrainersAsync();
    Task SetRoleAsync(string userId, string role, string callerRole);
    Task SetSupervisorAsync(string userId, string? supervisorId);
    Task SetLockoutAsync(string userId, bool locked, string callerRole);
    Task DeleteUserAsync(string userId, string callerRole);
    IReadOnlyList<string> GetAssignableRoles(string callerRole);
}
