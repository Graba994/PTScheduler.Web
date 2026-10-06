using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>Ustawienia alarmu przy fali błędów (Portal → Alarmy).</summary>
public sealed class ErrorAlertSettings
{
    public bool Enabled { get; set; } = true;
    public int Threshold { get; set; } = 20;
    public int WindowMinutes { get; set; } = 5;
    public int CooldownMinutes { get; set; } = 60;
    public bool Email { get; set; } = true;
    public bool Sms { get; set; }
}

/// <summary>Wynik sprawdzenia jednej instancji (do podglądu „teraz” w Portalu).</summary>
public sealed record ErrorScan(string Container, string Label, string? TenantSlug, int Errors, List<string> Samples);

/// <summary>
/// Pilnuje logów aplikacji trenerów i Portalu: gdy w oknie czasu pojawi się co najmniej N błędów,
/// administrator dostaje e-mail (i opcjonalnie SMS) z próbką błędów i linkiem do logów — zanim
/// zadzwoni trener. Po alarmie dla danej instancji kolejny dopiero po przerwie.
/// </summary>
public class ErrorAlertService(
    ContainerLogService logs,
    SiteSettingsService settings,
    EmailService email,
    CreditService credits,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    ILogger<ErrorAlertService> logger)
{
    private static readonly ConcurrentDictionary<string, DateTime> LastAlert = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ErrorAlertSettings> GetSettingsAsync()
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.ErrorAlertEnabled, SiteSettingsService.Keys.ErrorAlertThreshold,
            SiteSettingsService.Keys.ErrorAlertWindowMinutes, SiteSettingsService.Keys.ErrorAlertCooldownMinutes,
            SiteSettingsService.Keys.ErrorAlertEmail, SiteSettingsService.Keys.ErrorAlertSms);
        static int Int(string v, int def, int min, int max) => int.TryParse(v, out var n) ? Math.Clamp(n, min, max) : def;
        return new ErrorAlertSettings
        {
            Enabled = s[SiteSettingsService.Keys.ErrorAlertEnabled] != "false",
            Threshold = Int(s[SiteSettingsService.Keys.ErrorAlertThreshold], 20, 1, 10000),
            WindowMinutes = Int(s[SiteSettingsService.Keys.ErrorAlertWindowMinutes], 5, 1, 120),
            CooldownMinutes = Int(s[SiteSettingsService.Keys.ErrorAlertCooldownMinutes], 60, 5, 1440),
            Email = s[SiteSettingsService.Keys.ErrorAlertEmail] != "false",
            Sms = s[SiteSettingsService.Keys.ErrorAlertSms] == "true"
        };
    }

    public Task SaveSettingsAsync(ErrorAlertSettings v) => settings.SetManyAsync(new Dictionary<string, string>
    {
        [SiteSettingsService.Keys.ErrorAlertEnabled] = v.Enabled ? "true" : "false",
        [SiteSettingsService.Keys.ErrorAlertThreshold] = Math.Clamp(v.Threshold, 1, 10000).ToString(),
        [SiteSettingsService.Keys.ErrorAlertWindowMinutes] = Math.Clamp(v.WindowMinutes, 1, 120).ToString(),
        [SiteSettingsService.Keys.ErrorAlertCooldownMinutes] = Math.Clamp(v.CooldownMinutes, 5, 1440).ToString(),
        [SiteSettingsService.Keys.ErrorAlertEmail] = v.Email ? "true" : "false",
        [SiteSettingsService.Keys.ErrorAlertSms] = v.Sms ? "true" : "false"
    });

    /// <summary>Liczy błędy w oknie czasu w aplikacjach trenerów i w Portalu.</summary>
    public async Task<List<ErrorScan>> ScanAsync(int windowMinutes, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddMinutes(-windowMinutes);
        var result = new List<ErrorScan>();
        var sources = (await logs.GetSourcesAsync())
            .Where(s => s.Running && ((s.Group == ContainerLogService.GroupTenants && s.Hint == "aplikacja")
                                      || (s.Group == ContainerLogService.GroupPlatform && s.Label is "Portal" or "Guardian")));
        foreach (var src in sources)
        {
            try
            {
                var lines = await logs.ReadAsync(src.Container, 3000, since, ct);
                var errors = ContainerLogService.Parse(lines).Where(e => e.Level == LogLevelKind.Error).ToList();
                var samples = errors
                    .Select(e => (e.Category is null ? "" : e.Category + ": ") + string.Join(" ", e.Lines.Take(2)).Trim())
                    .Select(t => t.Length > 220 ? t[..220] + "…" : t)
                    .GroupBy(t => t).OrderByDescending(g => g.Count())
                    .Take(3).Select(g => g.Count() > 1 ? $"{g.Key} (×{g.Count()})" : g.Key).ToList();
                result.Add(new ErrorScan(src.Container, src.Label, src.TenantSlug, errors.Count, samples));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Nie udało się odczytać logów {Container}.", src.Container);
            }
        }
        return result;
    }

    /// <summary>Jedno sprawdzenie (co kilka minut z usługi w tle).</summary>
    public async Task<int> CheckAsync(CancellationToken ct = default)
    {
        var cfg = await GetSettingsAsync();
        if (!cfg.Enabled) return 0;
        var alerts = 0;
        foreach (var scan in await ScanAsync(cfg.WindowMinutes, ct))
        {
            if (scan.Errors < cfg.Threshold) continue;
            if (LastAlert.TryGetValue(scan.Container, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(cfg.CooldownMinutes)) continue;
            LastAlert[scan.Container] = DateTime.UtcNow;
            await SendAlertAsync(cfg, scan, test: false);
            alerts++;
        }
        return alerts;
    }

    public async Task<(bool Ok, string Message)> SendTestAsync()
    {
        var cfg = await GetSettingsAsync();
        var sent = await SendAlertAsync(cfg, new ErrorScan("pt-test-web", "Test alarmu", null, cfg.Threshold,
            ["Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432 (×12)", "System.TimeoutException: The operation has timed out"]), test: true);
        return sent ? (true, "Wysłano wiadomość testową.") : (false, "Nic nie wysłano — uzupełnij e-mail lub telefon administratora i włącz kanał.");
    }

    private async Task<bool> SendAlertAsync(ErrorAlertSettings cfg, ErrorScan scan, bool test)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail, SiteSettingsService.Keys.AdminNotificationPhone);
        var adminEmail = s[SiteSettingsService.Keys.AdminNotificationEmail];
        var phone = s[SiteSettingsService.Keys.AdminNotificationPhone];
        var link = $"{(config.GetValue<string>("Portal:PublicUrl") ?? "").TrimEnd('/')}/panel/logs?c={Uri.EscapeDataString(scan.Container)}";
        var sent = false;

        if (!test && scan.TenantSlug is not null)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var tenantId = await db.Tenants.Where(t => t.Slug == scan.TenantSlug).Select(t => (int?)t.Id).FirstOrDefaultAsync();
            if (tenantId is int id)
            {
                db.TenantEvents.Add(new TenantEvent { TenantId = id, EventType = TenantEventTypes.ErrorSpike, Detail = $"{scan.Errors} błędów w {cfg.WindowMinutes} min" });
                await db.SaveChangesAsync();
            }
        }

        if (cfg.Email && !string.IsNullOrWhiteSpace(adminEmail))
        {
            var items = string.Join("", scan.Samples.Select(x => $"<li style=\"margin-bottom:6px;font-family:monospace;font-size:12px\">{WebUtility.HtmlEncode(x)}</li>"));
            var html = $"""
                <div style="font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:600px;margin:0 auto;color:#0f172a">
                  <h2 style="color:#DC2626;margin-bottom:4px">{(test ? "[TEST] " : "")}Fala błędów: {WebUtility.HtmlEncode(scan.Label)}</h2>
                  <p><strong>{scan.Errors}</strong> błędów w ciągu ostatnich {cfg.WindowMinutes} min (próg: {cfg.Threshold}).</p>
                  <p>Najczęstsze:</p><ul>{items}</ul>
                  <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#7C3AED;color:#fff;text-decoration:none;padding:10px 20px;border-radius:8px;font-weight:600">Otwórz logi</a></p>
                  <p style="color:#64748b;font-size:12px">Kolejny alarm dla tej instancji najwcześniej za {cfg.CooldownMinutes} min. Ustawienia: Portal → Alarmy.</p>
                </div>
                """;
            var (ok, _) = await email.SendAsync(adminEmail, $"{(test ? "[TEST] " : "")}Fala błędów — {scan.Label} ({scan.Errors} w {cfg.WindowMinutes} min)", html);
            sent |= ok;
        }
        if (cfg.Sms && !string.IsNullOrWhiteSpace(phone))
        {
            var (ok, _) = await credits.SendPlatformSmsAsync(phone, $"PTScheduler{(test ? " TEST" : "")}: {scan.Errors} bledow w {cfg.WindowMinutes} min - {scan.Label}. {link}");
            sent |= ok;
        }
        if (!test) logger.LogWarning("Alarm: {Errors} błędów w {Window} min w {Container}.", scan.Errors, cfg.WindowMinutes, scan.Container);
        return sent;
    }
}

/// <summary>Co 2 minuty sprawdza logi pod kątem fali błędów.</summary>
public class ErrorAlertBackgroundService(IServiceScopeFactory scopes, ILogger<ErrorAlertBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ErrorAlertService>().CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Sprawdzanie fali błędów nie powiodło się."); }
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
