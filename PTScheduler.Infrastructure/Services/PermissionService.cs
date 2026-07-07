using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PermissionService(IDbContextFactory<ApplicationDbContext> dbFactory) : IPermissionService
{
    public async Task<bool> HasPermissionAsync(string role, string permission)
    {
        if (role == Roles.Admin) return true;

        await using var db = dbFactory.CreateDbContext();
        var rp = await db.RolePermissions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Role == role && r.Permission == permission);

        if (rp is not null) return rp.IsGranted;

        return Permissions.Defaults.TryGetValue(role, out var defaults)
               && defaults.Contains(permission);
    }

    public async Task<List<RolePermission>> GetAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.RolePermissions.AsNoTracking().OrderBy(r => r.Role).ThenBy(r => r.Permission).ToListAsync();
    }

    public async Task<List<RolePermission>> GetForRoleAsync(string role)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.RolePermissions.AsNoTracking().Where(r => r.Role == role).ToListAsync();
    }

    public async Task SetPermissionAsync(string role, string permission, bool granted)
    {
        await using var db = dbFactory.CreateDbContext();
        var existing = await db.RolePermissions
            .FirstOrDefaultAsync(r => r.Role == role && r.Permission == permission);

        if (existing is not null)
        {
            existing.IsGranted = granted;
        }
        else
        {
            db.RolePermissions.Add(new RolePermission
            {
                Role = role,
                Permission = permission,
                IsGranted = granted
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task SeedDefaultsAsync()
    {
        await using var db = dbFactory.CreateDbContext();

        var existing = await db.RolePermissions.ToListAsync();
        if (existing.Count > 0)
            db.RolePermissions.RemoveRange(existing);

        foreach (var (role, permissions) in Permissions.Defaults)
        {
            foreach (var perm in Permissions.All)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    Role = role,
                    Permission = perm.Key,
                    IsGranted = permissions.Contains(perm.Key)
                });
            }
        }

        await db.SaveChangesAsync();
    }
}
