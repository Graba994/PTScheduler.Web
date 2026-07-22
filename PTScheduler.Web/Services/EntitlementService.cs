using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Services;

// Singleton — parses TENANT_ENTITLEMENTS once at startup and answers
// entitlement questions for the rest of the process lifetime. Container
// restart (triggered by portal on plan change) reloads it.
public class EntitlementService
{
    private readonly Entitlements _current;
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

        if (string.IsNullOrWhiteSpace(json))
        {
            _current = Entitlements.Unlimited();
            _logger.LogWarning("TENANT_ENTITLEMENTS not set — running in unlimited mode.");
            return;
        }

        try
        {
            _current = JsonSerializer.Deserialize<Entitlements>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? Entitlements.Unlimited();

            _logger.LogInformation("Loaded entitlements for plan '{Plan}'.", _current.Name);
        }
        catch (Exception ex)
        {
            _current = Entitlements.Unlimited();
            _logger.LogError(ex, "Failed to parse TENANT_ENTITLEMENTS — falling back to unlimited.");
        }
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
}
