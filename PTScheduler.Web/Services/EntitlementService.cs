using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

// Singleton — parses TENANT_ENTITLEMENTS once at startup and answers
// entitlement questions for the rest of the process lifetime. Container
// restart (triggered by portal on plan change) reloads it.
public class EntitlementService
{
    private Entitlements _current;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(
        IConfiguration config,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<EntitlementService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;

        var json = Environment.GetEnvironmentVariable("TENANT_ENTITLEMENTS")
                   ?? config.GetValue<string>("Tenant:Entitlements");

        _current = ParseOrUnlimited(json);
    }

    private Entitlements ParseOrUnlimited(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("TENANT_ENTITLEMENTS not set — running in unlimited mode.");
            return Entitlements.Unlimited();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Entitlements>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null) return Entitlements.Unlimited();
            _logger.LogInformation("Loaded entitlements for plan '{Plan}'.", parsed.Name);
            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse entitlements JSON — falling back to unlimited.");
            return Entitlements.Unlimited();
        }
    }

    // Replace the in-memory plan without a restart. The portal calls the tenant's
    // POST /internal/entitlements/reload endpoint (shared secret) on plan changes.
    public void ReplaceFromJson(string json)
    {
        _current = ParseOrUnlimited(json);
    }

    public Entitlements Current => _current;

    public bool IsAllowed(string flag)
    {
        var prop = typeof(Entitlements).GetProperty(flag);
        if (prop is null || prop.PropertyType != typeof(bool)) return true;
        return (bool)(prop.GetValue(_current) ?? false);
    }

    public int Limit(string limitName)
    {
        var prop = typeof(Entitlements).GetProperty(limitName);
        if (prop is null || prop.PropertyType != typeof(int)) return int.MaxValue;
        return (int)(prop.GetValue(_current) ?? int.MaxValue);
    }

    // ── Runtime enforcement helpers ─────────────────────────────────

    public async Task<(bool Allowed, int Current, int Max)> CheckClientLimitAsync()
    {
        var max = _current.MaxClients;
        if (max == int.MaxValue) return (true, 0, int.MaxValue);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var count = await db.Clients.CountAsync();
        return (count < max, count, max);
    }

    // Counts users in a given role and compares to a plan limit.
    // Used before allowing admin to add another trainer/subordinate.
    public async Task<(bool Allowed, int Current, int Max)> CheckRoleLimitAsync(
        UserManager<ApplicationUser> userManager, string role, int max)
    {
        if (max == int.MaxValue) return (true, 0, int.MaxValue);
        var users = await userManager.GetUsersInRoleAsync(role);
        var count = users.Count;
        return (count < max, count, max);
    }

    public Task<(bool Allowed, int Current, int Max)> CheckTrainerLimitAsync(
        UserManager<ApplicationUser> userManager)
        => CheckRoleLimitAsync(userManager, Roles.Trainer, _current.MaxTrainers);

    public Task<(bool Allowed, int Current, int Max)> CheckSubordinateLimitAsync(
        UserManager<ApplicationUser> userManager)
        => CheckRoleLimitAsync(userManager, Roles.Subordinate, _current.MaxSubordinates);

    public async Task<(bool Allowed, int Current, int Max)> CheckMonthlySessionsAsync()
    {
        var max = _current.MaxSessionsPerMonth;
        if (max == int.MaxValue) return (true, 0, int.MaxValue);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var since = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var count = await db.Sessions.CountAsync(s => s.StartTime >= since);
        return (count < max, count, max);
    }

    public async Task<(bool Allowed, long CurrentBytes, long MaxBytes)> CheckVideoStorageAsync(
        Application.Interfaces.ICourseService courseService)
    {
        var maxGB = _current.MaxVideoStorageGB;
        if (maxGB == int.MaxValue) return (true, 0, long.MaxValue);
        if (maxGB <= 0) return (false, 0, 0);
        long maxBytes = (long)maxGB * 1024 * 1024 * 1024;
        var used = await courseService.GetVideoStorageUsedBytesAsync();
        return (used < maxBytes, used, maxBytes);
    }

    // Reads the size of the branding uploads directory. Cheap because it
    // just enumerates FileInfos once — we don't need real-time accuracy.
    public (bool Allowed, long CurrentBytes, long MaxBytes) CheckStorage(string uploadsPath)
    {
        var maxGB = _current.MaxStorageGB;
        if (maxGB == int.MaxValue) return (true, 0, long.MaxValue);
        long maxBytes = (long)maxGB * 1024 * 1024 * 1024;

        try
        {
            if (!Directory.Exists(uploadsPath)) return (true, 0, maxBytes);
            var used = new DirectoryInfo(uploadsPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            return (used < maxBytes, used, maxBytes);
        }
        catch { return (true, 0, maxBytes); }
    }
}
