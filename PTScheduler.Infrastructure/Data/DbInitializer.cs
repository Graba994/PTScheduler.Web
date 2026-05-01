using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;

namespace PTScheduler.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = [Roles.Admin, Roles.Trainer, Roles.Subordinate, Roles.Client];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    public static async Task SeedSessionTypesAsync(ApplicationDbContext db)
    {
        if (await db.SessionTypes.AnyAsync()) return;

        db.SessionTypes.AddRange(
            new SessionType { Name = "45 minut", DurationMinutes = 45, IsActive = true },
            new SessionType { Name = "60 minut (standard)", DurationMinutes = 60, IsActive = true },
            new SessionType { Name = "90 minut", DurationMinutes = 90, IsActive = true },
            new SessionType { Name = "Trening grupowy", DurationMinutes = 60, IsGroup = true, IsActive = true }
        );
        await db.SaveChangesAsync();
    }
}
