using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class UserManagementService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<ApplicationDbContext> dbFactory) : IUserManagementService
{
    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        var users = await userManager.Users
            .Include(u => u.Supervisor)
            .OrderBy(u => u.Email)
            .ToListAsync();

        var result = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(MapToDto(user, roles.FirstOrDefault() ?? string.Empty));
        }
        return result;
    }

    public async Task<UserDto?> GetUserAsync(string id)
    {
        var user = await userManager.Users
            .Include(u => u.Supervisor)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null) return null;
        var roles = await userManager.GetRolesAsync(user);
        return MapToDto(user, roles.FirstOrDefault() ?? string.Empty);
    }

    public async Task<List<UserDto>> GetTrainersAsync()
    {
        var trainers = await userManager.GetUsersInRoleAsync(Roles.Trainer);
        return trainers.Select(u => MapToDto(u, Roles.Trainer)).ToList();
    }

    public async Task SetRoleAsync(string userId, string role)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        var currentRoles = await userManager.GetRolesAsync(user);
        await userManager.RemoveFromRolesAsync(user, currentRoles);

        if (!string.IsNullOrEmpty(role))
            await userManager.AddToRoleAsync(user, role);

        // Clear supervisor if no longer a subordinate
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

    public async Task SetLockoutAsync(string userId, bool locked)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user,
            locked ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    public async Task DeleteUserAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");
        await userManager.DeleteAsync(user);
    }

    public async Task<(bool Success, string? Error)> CreateUserAsync(CreateUserDto dto)
    {
        if (await userManager.FindByEmailAsync(dto.Email) is not null)
            return (false, $"Email '{dto.Email}' jest już zarejestrowany.");

        var user = new ApplicationUser
        {
            UserName       = dto.Email,
            Email          = dto.Email,
            FirstName      = dto.FirstName,
            LastName       = dto.LastName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(e => e.Description)));

        if (!string.IsNullOrEmpty(dto.Role))
            await userManager.AddToRoleAsync(user, dto.Role);

        if (dto.Role == Roles.Subordinate && !string.IsNullOrEmpty(dto.SupervisorId))
        {
            user.SupervisorId = dto.SupervisorId;
            await userManager.UpdateAsync(user);
        }

        if (dto.Role == Roles.Client)
        {
            await using var db = dbFactory.CreateDbContext();
            db.Clients.Add(new Domain.Entities.Client
            {
                ApplicationUserId = user.Id,
                FirstName  = dto.FirstName,
                LastName   = dto.LastName,
                Status     = Domain.Enums.ClientStatus.Active,
                CreatedAt  = DateTime.UtcNow,
                TrainerUserId = dto.TrainerUserId
            });
            await db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ResetPasswordAsync(string userId, string newPassword)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        await userManager.RemovePasswordAsync(user);
        var result = await userManager.AddPasswordAsync(user, newPassword);

        return result.Succeeded
            ? (true, null)
            : (false, string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    public async Task<(bool Success, string? Error)> UpdateProfileAsync(string userId, string firstName, string lastName, string email)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null && existing.Id != userId)
                return (false, $"Email '{email}' jest już zajęty przez innego użytkownika.");
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.Email = email;
        user.UserName = email;
        user.NormalizedEmail = userManager.NormalizeEmail(email);
        user.NormalizedUserName = userManager.NormalizeName(email);

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return (false, string.Join(" ", result.Errors.Select(e => e.Description)));

        await using var db = dbFactory.CreateDbContext();
        var client = await db.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == userId);
        if (client is not null)
        {
            client.FirstName = firstName;
            client.LastName = lastName;
            await db.SaveChangesAsync();
        }

        return (true, null);
    }

    public async Task<List<ClientSummaryDto>> GetClientsByTrainerAsync(string trainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var clients = await db.Clients
            .AsNoTracking()
            .Where(c => c.TrainerUserId == trainerUserId)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToListAsync();

        var userIds = clients.Select(c => c.ApplicationUserId).ToList();
        var emails = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email);

        return clients.Select(c => new ClientSummaryDto
        {
            Id = c.Id,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Email = emails.GetValueOrDefault(c.ApplicationUserId),
            TrainerUserId = c.TrainerUserId
        }).ToList();
    }

    public async Task ReassignClientsAsync(IEnumerable<int> clientIds, string newTrainerUserId)
    {
        await using var db = dbFactory.CreateDbContext();
        var ids = clientIds.ToList();
        var clients = await db.Clients
            .Where(c => ids.Contains(c.Id))
            .ToListAsync();

        foreach (var client in clients)
            client.TrainerUserId = newTrainerUserId;

        await db.SaveChangesAsync();
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
