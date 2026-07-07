using PTScheduler.Domain.Entities;

namespace PTScheduler.Application.Interfaces;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(string role, string permission);
    Task<List<RolePermission>> GetAllAsync();
    Task<List<RolePermission>> GetForRoleAsync(string role);
    Task SetPermissionAsync(string role, string permission, bool granted);
    Task SeedDefaultsAsync();
}
