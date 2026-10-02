using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

// Co 2 minuty sprawdza /health każdej aktywnej instancji (kilka adresów po kolei — TenantEndpoint).
// „Nie odpowiada” dopiero, gdy żaden adres nie działa przez co najmniej 3 minuty (np. restart po
// aktualizacji nie wywołuje alarmu). Przy zmianie stanu jeden e-mail do administratora.
public class HealthMonitorService(
    IServiceScopeFactory scopeFactory,
    ILogger<HealthMonitorService> logger) : BackgroundService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    /// <summary>Tyle musi trwać brak odpowiedzi, zanim oznaczymy instancję i wyślemy alarm.</summary>
    private static readonly TimeSpan DownAfter = TimeSpan.FromMinutes(3);

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
        var docker = scope.ServiceProvider.GetRequiredService<DockerService>();

        var adminEmail = config.GetValue<string>("Portal:AdminEmail") ?? "admin@ptscheduler.pl";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(ct);

        foreach (var t in tenants)
        {
            var webName = t.WebContainerName ?? $"pt-{t.Slug}-web";
            await docker.EnsureAttachedToTenantNetworkAsync(webName);
            var probe = await TenantEndpoint.ProbeAsync(config, t, ct);
            var (ok, ms, error) = (probe.Ok, probe.Ms, probe.Error);
            var restarting = false;
            if (!ok)
            {
                // Stan kontenera mówi więcej niż „connection refused”: czy aplikacja w ogóle działa i czy się restartuje.
                var info = await docker.GetContainerInfoAsync(webName);
                var state = ContainerState(info);
                error = $"{state} · {error}";
                restarting = info is { Running: true } && DateTime.TryParse(info.StartedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var started)
                    && DateTime.UtcNow - started.ToUniversalTime() < DownAfter;
            }
            var wasHealthy = t.IsHealthy;

            t.LastHealthCheckAt = DateTime.UtcNow;
            t.LastHealthResponseMs = ms;
            t.LastHealthError = ok ? null : error;
            if (ok)
            {
                t.IsHealthy = true;
                if (!probe.Base!.Contains($":{t.Port}", StringComparison.Ordinal))
                    logger.LogWarning("Instancja {Slug} odpowiada tylko przez {Base} — sprawdź Portal:ForwardHost (port {Port}).", t.Slug, probe.Base, t.Port);
            }
            else
            {
                // Pojedyncza nieudana próba (restart kontenera, chwilowe obciążenie) jeszcze nie jest awarią.
                t.UnhealthySinceUtc ??= DateTime.UtcNow;
                if (!restarting && DateTime.UtcNow - t.UnhealthySinceUtc.Value >= DownAfter) t.IsHealthy = false;
            }

            if (ok)
            {
                try
                {
                    var secret = TenantSecrets.For(t, config);
                    var activityDate = await FetchLastActivityAsync(probe.Base!, secret, ct);
                    if (activityDate.HasValue)
                    {
                        t.LastActivityAt = activityDate.Value;
                        t.LastActivityCheckedAt = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Nie pobrano ostatniej aktywności tenanta {Slug} (port {Port}).", t.Slug, t.Port);
                }
            }

            if (!ok)
            {
                // Alarm raz — gdy instancja właśnie została uznana za nieodpowiadającą.
                if (t.IsHealthy == false && !t.DownAlertSent)
                {
                    db.TenantEvents.Add(new TenantEvent
                    {
                        TenantId = t.Id,
                        EventType = TenantEventTypes.HealthDown,
                        Detail = error
                    });
                    _ = email.SendAsync(adminEmail,
                        $"Tenant {t.Slug} nie odpowiada",
                        BuildDownAlert(t, error));
                    t.DownAlertSent = true;
                }
            }
            else
            {
                if (wasHealthy == false && t.DownAlertSent)
                {
                    var downtime = t.UnhealthySinceUtc.HasValue
                        ? DateTime.UtcNow - t.UnhealthySinceUtc.Value
                        : TimeSpan.Zero;
                    db.TenantEvents.Add(new TenantEvent
                    {
                        TenantId = t.Id,
                        EventType = TenantEventTypes.HealthRecovered,
                        Detail = $"Downtime: {downtime.TotalMinutes:0} min"
                    });
                    _ = email.SendAsync(adminEmail,
                        $"Tenant {t.Slug} znow dziala",
                        BuildUpAlert(t, downtime));
                }
                t.UnhealthySinceUtc = null;
                t.DownAlertSent = false;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static string ContainerState(ContainerInfo? info)
    {
        if (info is null) return "Kontener aplikacji nie istnieje";
        var started = DateTime.TryParse(info.StartedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var s)
            ? s.ToLocalTime().ToString("dd.MM HH:mm") : "?";
        var restarts = info.RestartCount > 0 ? $", restartów: {info.RestartCount}" : "";
        return info.Running
            ? $"Kontener działa (start {started}{restarts}), ale aplikacja nie odpowiada"
            : $"Kontener zatrzymany ({info.Status}, kod wyjścia {info.ExitCode}{restarts})";
    }

    private static async Task<DateTime?> FetchLastActivityAsync(
        string baseUrl, string secret, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/internal/last-activity");
        if (!string.IsNullOrEmpty(secret))
            req.Headers.Add("X-Internal-Secret", secret);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("lastActivity", out var val) && val.ValueKind == System.Text.Json.JsonValueKind.String)
            return DateTime.Parse(val.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return null;
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
