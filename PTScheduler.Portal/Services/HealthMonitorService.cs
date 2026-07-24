using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

// Every 2 min, ping http://localhost:{port}/health for each Active tenant.
// Updates the health columns on the Tenant. When a tenant flips from
// healthy to unhealthy, send one email to platform admin (subsequent
// checks stay quiet until the tenant recovers).
public class HealthMonitorService(
    IServiceScopeFactory scopeFactory,
    ILogger<HealthMonitorService> logger) : BackgroundService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private const string HealthCheckPath = "/health";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAllAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Health monitor iteration failed"); }

            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private async Task CheckAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var email = scope.ServiceProvider.GetRequiredService<EmailService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var forwardHost = config.GetValue<string>("Portal:ForwardHost") ?? "192.168.0.220";
        var adminEmail = config.GetValue<string>("Portal:AdminEmail") ?? "admin@ptscheduler.pl";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(ct);

        foreach (var t in tenants)
        {
            var (ok, ms, error) = await ProbeAsync(forwardHost, t.Port, ct);
            var wasHealthy = t.IsHealthy;

            t.IsHealthy = ok;
            t.LastHealthCheckAt = DateTime.UtcNow;
            t.LastHealthResponseMs = ms;
            t.LastHealthError = ok ? null : error;

            if (!ok)
            {
                t.UnhealthySinceUtc ??= DateTime.UtcNow;

                // Alert on first flip to unhealthy after being healthy (or on first-ever failure)
                if ((wasHealthy == true || wasHealthy is null) && !t.DownAlertSent)
                {
                    _ = email.SendAsync(adminEmail,
                        $"⚠ Tenant {t.Slug} nie odpowiada",
                        BuildDownAlert(t, error));
                    t.DownAlertSent = true;
                }
            }
            else
            {
                if (wasHealthy == false)
                {
                    // Recovery — clear flags and notify
                    var downtime = t.UnhealthySinceUtc.HasValue
                        ? DateTime.UtcNow - t.UnhealthySinceUtc.Value
                        : TimeSpan.Zero;
                    _ = email.SendAsync(adminEmail,
                        $"✓ Tenant {t.Slug} znów działa",
                        BuildUpAlert(t, downtime));
                }
                t.UnhealthySinceUtc = null;
                t.DownAlertSent = false;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<(bool Ok, int Ms, string? Error)> ProbeAsync(
        string host, int port, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await _http.GetAsync($"http://{host}:{port}{HealthCheckPath}", ct);
            sw.Stop();
            if (resp.IsSuccessStatusCode) return (true, (int)sw.ElapsedMilliseconds, null);
            return (false, (int)sw.ElapsedMilliseconds, $"HTTP {(int)resp.StatusCode}");
        }
        catch (TaskCanceledException) { return (false, (int)sw.ElapsedMilliseconds, "timeout"); }
        catch (Exception ex) { return (false, (int)sw.ElapsedMilliseconds, ex.Message); }
    }

    private static string BuildDownAlert(Tenant t, string? error) =>
        $"""
        <p><strong>{t.CompanyName}</strong> (<code>{t.Slug}</code>) nie odpowiada na health checki.</p>
        <p>Port: {t.Port}<br>Domena: {t.Domain}<br>Błąd: {error ?? "brak"}<br>Kontener web: {t.WebContainerName}<br>Kontener db: {t.DbContainerName}</p>
        <p>Wejdź w portal → /panel/tenants/{t.Id} żeby sprawdzić logi i zrestartować kontener.</p>
        """;

    private static string BuildUpAlert(Tenant t, TimeSpan downtime) =>
        $"""
        <p><strong>{t.CompanyName}</strong> (<code>{t.Slug}</code>) znów odpowiada.</p>
        <p>Downtime: <strong>{downtime.TotalMinutes:0} min</strong> ({downtime:hh\:mm\:ss})</p>
        """;
}
