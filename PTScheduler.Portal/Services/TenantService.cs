using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public class TenantService(
    IDbContextFactory<PortalDbContext> dbFactory,
    DockerService docker,
    IConfiguration config,
    ILogger<TenantService> logger)
{
    private string TenantImage => config.GetValue<string>("Portal:TenantImage") ?? "ptscheduler-web:latest";

    public async Task<List<Tenant>> GetAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Tenants
            .AsNoTracking()
            .Include(t => t.Plan)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Tenant?> GetAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Tenants
            .AsNoTracking()
            .Include(t => t.Plan)
            .Include(t => t.Subscriptions.OrderByDescending(s => s.CreatedAt).Take(1))
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tenant?> GetBySlugAsync(string slug)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    }

    public async Task<Tenant> CreateAsync(string slug, string domain, string companyName,
        string ownerName, string ownerEmail, string? phone, string planId, string? setupMode)
    {
        await using var db = dbFactory.CreateDbContext();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
            throw new InvalidOperationException($"Tenant '{slug}' already exists.");

        const int basePort = 9001;
        var portalPort = config.GetValue<int?>("Portal:PortalPort") ?? 8081;
        var maxPort = await db.Tenants.MaxAsync(t => (int?)t.Port) ?? (basePort - 1);
        var port = Math.Max(maxPort + 1, basePort);
        while (port == portalPort) port++;

        var dbPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace("/", "").Replace("+", "").Replace("=", "");
        if (dbPassword.Length > 32) dbPassword = dbPassword[..32];

        var tenant = new Tenant
        {
            Slug = slug,
            Domain = domain,
            Port = port,
            CompanyName = companyName,
            OwnerName = ownerName,
            OwnerEmail = ownerEmail,
            Phone = phone,
            DbPassword = dbPassword,
            PlanId = planId,
            SetupMode = setupMode,
            Status = TenantStatus.Pending,
            WebContainerName = $"pt-{slug}-web",
            DbContainerName = $"pt-{slug}-db"
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public async Task<(bool Success, string Output)> ProvisionAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        tenant.Status = TenantStatus.Provisioning;
        await db.SaveChangesAsync();

        try
        {
            if (!await docker.ImageExistsAsync(TenantImage))
                throw new InvalidOperationException(
                    $"Image '{TenantImage}' not found on the Docker host. " +
                    "Build it first: docker build -t ptscheduler-web:latest <path-to-main-app-repo>");

            await docker.ProvisionTenantAsync(
                tenant.Slug,
                tenant.DbPassword,
                tenant.Port,
                TenantImage,
                tenant.Domain);

            tenant.Status = TenantStatus.Active;
            tenant.ProvisionedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return (true, $"Tenant '{tenant.Slug}' provisioned on port {tenant.Port}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioning failed for {Slug}", tenant.Slug);
            tenant.Status = TenantStatus.Pending;
            await db.SaveChangesAsync();
            return (false, ex.Message);
        }
    }

    public async Task SuspendAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var webName = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
        var dbName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
        try { await docker.StopContainerAsync(webName); } catch { }
        try { await docker.StopContainerAsync(dbName); } catch { }

        tenant.Status = TenantStatus.Suspended;
        await db.SaveChangesAsync();
    }

    public async Task ResumeAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var webName = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
        var dbName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
        try { await docker.StartContainerAsync(dbName); } catch { }
        try { await docker.StartContainerAsync(webName); } catch { }

        tenant.Status = TenantStatus.Active;
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int tenantId, bool removeContainers)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        if (removeContainers)
        {
            var webName = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
            var dbName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
            try { await docker.StopContainerAsync(webName); } catch { }
            try { await docker.StopContainerAsync(dbName); } catch { }
            try { await docker.RemoveContainerAsync(webName); } catch { }
            try { await docker.RemoveContainerAsync(dbName); } catch { }
            await docker.RemoveTenantResourcesAsync(tenant.Slug);
        }

        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();
    }

    public async Task UpdatePlanAsync(int tenantId, string planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");
        tenant.PlanId = planId;
        await db.SaveChangesAsync();
    }

    public async Task<Tenant> ImportAsync(string slug, string domain, int port,
        string companyName, string ownerName, string ownerEmail, string? phone, string planId,
        string? webContainerName, string? dbContainerName)
    {
        await using var db = dbFactory.CreateDbContext();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
            throw new InvalidOperationException($"Tenant '{slug}' już istnieje.");

        var tenant = new Tenant
        {
            Slug = slug,
            Domain = domain,
            Port = port,
            CompanyName = companyName,
            OwnerName = ownerName,
            OwnerEmail = ownerEmail,
            Phone = phone,
            DbPassword = "imported",
            PlanId = planId,
            SetupMode = "admin",
            Status = TenantStatus.Active,
            ProvisionedAt = DateTime.UtcNow,
            WebContainerName = webContainerName,
            DbContainerName = dbContainerName
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public async Task<DashboardStats> GetStatsAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return new DashboardStats
        {
            TotalTenants = await db.Tenants.CountAsync(),
            ActiveTenants = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active),
            PendingTenants = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Pending),
            SuspendedTenants = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Suspended),
            TotalRevenue = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active)
                .SumAsync(s => s.Amount),
            ActiveSubscriptions = await db.Subscriptions
                .CountAsync(s => s.Status == SubscriptionStatus.Active)
        };
    }

}

public class DashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int PendingTenants { get; set; }
    public int SuspendedTenants { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ActiveSubscriptions { get; set; }
}
