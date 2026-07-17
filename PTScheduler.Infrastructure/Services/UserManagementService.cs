using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class UserManagementService(UserManager<ApplicationUser> userManager) : IUserManagementService
{
    public async Task<List<UserDto>> GetVisibleUsersAsync(string callerRole)
    {
        var users = await userManager.Users
            .Include(u => u.Supervisor)
            .OrderBy(u => u.Email)
            .ToListAsync();

        var result = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? string.Empty;
            if (callerRole != Roles.Admin && role == Roles.Admin) continue;
            result.Add(MapToDto(user, role));
        }
        return result;
    }

    public async Task<UserDto?> GetUserAsync(string id, string callerRole)
    {
        var user = await userManager.Users
            .Include(u => u.Supervisor)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null) return null;
        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        if (callerRole != Roles.Admin && role == Roles.Admin) return null;
        return MapToDto(user, role);
    }

    public async Task<List<UserDto>> GetTrainersAsync()
    {
        var trainers = await userManager.GetUsersInRoleAsync(Roles.Trainer);
        return trainers.Select(u => MapToDto(u, Roles.Trainer)).ToList();
    }

    public async Task SetRoleAsync(string userId, string role, string callerRole)
    {
        var assignable = GetAssignableRoles(callerRole);
        if (!string.IsNullOrEmpty(role) && !assignable.Contains(role))
            throw new UnauthorizedAccessException(
                $"Rola '{callerRole}' nie może przypisywać roli '{role}'.");

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault() ?? string.Empty;

        EnsureCallerCanModify(callerRole, currentRole);

        await userManager.RemoveFromRolesAsync(user, currentRoles);

        if (!string.IsNullOrEmpty(role))
            await userManager.AddToRoleAsync(user, role);

        if (role != Roles.Subordinate && user.SupervisorId is not null)
        {
            user.SupervisorId = null;
            await userManager.UpdateAsync(user);
        }
    }

    public async Task SetSupervisorAsync(string userId, string? supervisorId)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        user.SupervisorId = supervisorId;
        await userManager.UpdateAsync(user);
    }

    public async Task SetLockoutAsync(string userId, bool locked, string callerRole)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var currentRole = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        EnsureCallerCanModify(callerRole, currentRole);

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user,
            locked ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    public async Task DeleteUserAsync(string userId, string callerRole)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var currentRole = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? string.Empty;
        EnsureCallerCanModify(callerRole, currentRole);

        await userManager.DeleteAsync(user);
    }

    public IReadOnlyList<string> GetAssignableRoles(string callerRole) => callerRole switch
    {
        Roles.Admin   => [Roles.Admin, Roles.Trainer, Roles.Subordinate, Roles.Client],
        Roles.Trainer => [Roles.Subordinate, Roles.Client],
        _             => []
    };

    private static void EnsureCallerCanModify(string callerRole, string targetRole)
    {
        if (callerRole == Roles.Admin) return;
        if (callerRole == Roles.Trainer && targetRole != Roles.Admin && targetRole != Roles.Trainer) return;
        throw new UnauthorizedAccessException("Nie masz uprawnień do modyfikacji tego użytkownika.");
    }

    private static UserDto MapToDto(ApplicationUser user, string role) => new()
    {
        Id = user.Id,
        Email = user.Email ?? string.Empty,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Role = role,
        SupervisorId = user.SupervisorId,
        SupervisorEmail = user.Supervisor?.Email,
        IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow
    };
}
