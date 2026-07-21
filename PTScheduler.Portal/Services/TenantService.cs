using System.Diagnostics;
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
    private string DeployDir => config.GetValue<string>("Portal:DeployDir") ?? "/opt/ptscheduler/deploy";

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

        var maxPort = await db.Tenants.MaxAsync(t => (int?)t.Port) ?? 9000;
        var port = maxPort + 1;
        var dbPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace("/", "").Replace("+", "").Replace("=", "")[..32];

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
            Status = TenantStatus.Pending
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
            var scriptPath = Path.Combine(DeployDir, "provision.sh");
            var result = await RunScriptAsync(scriptPath, $"{tenant.Slug} {tenant.Domain}");

            tenant.Status = TenantStatus.Active;
            tenant.ProvisionedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return (true, result);
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

        await docker.StopContainerAsync($"pt-{tenant.Slug}-web");
        await docker.StopContainerAsync($"pt-{tenant.Slug}-db");

        tenant.Status = TenantStatus.Suspended;
        await db.SaveChangesAsync();
    }

    public async Task ResumeAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        await docker.StartContainerAsync($"pt-{tenant.Slug}-db");
        await docker.StartContainerAsync($"pt-{tenant.Slug}-web");

        tenant.Status = TenantStatus.Active;
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

    private static async Task<string> RunScriptAsync(string script, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"{script} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Script failed (exit {process.ExitCode}): {error}");

        return output;
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
