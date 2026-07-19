using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;

namespace PTScheduler.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedPermissionsAsync(ApplicationDbContext db)
    {
        // Idempotent: add any missing (role, permission) rows without touching
        // existing ones, so newly introduced permissions appear for management
        // on databases that were seeded before those permissions existed.
        var existing = (await db.RolePermissions
                .Select(rp => new { rp.Role, rp.Permission })
                .ToListAsync())
            .Select(e => (e.Role, e.Permission))
            .ToHashSet();

        var toAdd = new List<RolePermission>();
        foreach (var (role, permissions) in Permissions.Defaults)
        {
            foreach (var perm in Permissions.All)
            {
                if (existing.Contains((role, perm.Key))) continue;
                toAdd.Add(new RolePermission
                {
                    Role = role,
                    Permission = perm.Key,
                    IsGranted = permissions.Contains(perm.Key)
                });
            }
        }

        if (toAdd.Count > 0)
        {
            db.RolePermissions.AddRange(toAdd);
            await db.SaveChangesAsync();
        }
    }

    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = [Roles.Admin, Roles.Trainer, Roles.Subordinate, Roles.Client];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    public static async Task SeedAdminAsync(UserManager<ApplicationUser> userManager)
    {
        const string email = "root@admin.local";
        if (await userManager.FindByEmailAsync(email) is not null) return;

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = "Admin",
            LastName = "System",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        await userManager.CreateAsync(admin);
        admin.PasswordHash = userManager.PasswordHasher.HashPassword(admin, "password");
        await userManager.UpdateAsync(admin);
        await userManager.AddToRoleAsync(admin, Roles.Admin);
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
