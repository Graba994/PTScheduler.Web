using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class DatabaseMaintenanceService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager) : IDatabaseMaintenanceService
{
    public async Task<ClearDataResult> ClearAllDataAsync()
    {
        var result = new ClearDataResult();

        // Collect client user IDs before deleting clients
        var clientUserIds = await db.Clients
            .Select(c => c.ApplicationUserId)
            .ToListAsync();

        // Delete business data in FK-safe order
        result.Notes = await db.TrainerNotes.ExecuteDeleteAsync();
        result.Measurements = await db.BodyMeasurements.ExecuteDeleteAsync();
        result.Sessions = await db.Sessions.ExecuteDeleteAsync();
        result.Packages = await db.SessionPackages.ExecuteDeleteAsync();
        result.IntroConfigs = await db.IntroSessionConfigs.ExecuteDeleteAsync();
        result.Clients = await db.Clients.ExecuteDeleteAsync();

        // Delete Identity accounts that had the Client role
        // (keep Admin/Trainer/Subordinate accounts intact)
        foreach (var userId in clientUserIds)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is null) continue;
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains(Roles.Client) &&
                !roles.Contains(Roles.Admin) &&
                !roles.Contains(Roles.Trainer) &&
                !roles.Contains(Roles.Subordinate))
            {
                await userManager.DeleteAsync(user);
                result.ClientUsers++;
            }
        }

        return result;
    }
}
