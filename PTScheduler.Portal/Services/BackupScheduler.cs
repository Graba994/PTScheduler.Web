using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

// Wakes daily at 03:00 UTC when backup_schedule = "daily".
// Backs up all active tenants + portal DB, sends copies off the server, purges old files,
// and on Sundays restores the newest copies into a scratch database to prove they work.
public class BackupScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a moment to fully start
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var wait = TimeUntilNextRun();
                logger.LogInformation("Next scheduled backup in {H}h {M}m", wait.Hours, wait.Minutes);
                await Task.Delay(wait, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
                var schedule = await settings.GetAsync(SiteSettingsService.Keys.BackupSchedule);
                if (schedule == "off")
                {
                    logger.LogInformation("Scheduled backup skipped — schedule is 'off'");
                    continue;
                }

                await scope.ServiceProvider.GetRequiredService<BackupMaintenanceService>()
                    .RunNightlyAsync(DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup scheduler iteration failed");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }

    // Next 03:00 UTC from "now"
    private static TimeSpan TimeUntilNextRun()
    {
        var now = DateTime.UtcNow;
        var todayRun = new DateTime(now.Year, now.Month, now.Day, 3, 0, 0, DateTimeKind.Utc);
        var next = now < todayRun ? todayRun : todayRun.AddDays(1);
        return next - now;
    }
}

/// <summary>Nocna obsługa kopii: backup → kopia poza serwer → sprzątanie → (niedziela) test odtworzenia → alarm przy problemach.</summary>
public class BackupMaintenanceService(
    BackupService backups,
    OffsiteBackupService offsite,
    SiteSettingsService settings,
    EmailService email,
    CreditService credits,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    ILogger<BackupMaintenanceService> logger)
{
    public async Task RunNightlyAsync(bool weeklyVerify, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var problems = new List<string>();

        logger.LogInformation("Starting scheduled backup run");
        var (ok, fail, portalOk) = await backups.BackupAllAsync(BackupKind.Scheduled);
        logger.LogInformation("Backup finished — {Ok} tenants OK, {Fail} failed, portal={Portal}", ok, fail, portalOk ? "OK" : "FAIL");
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var failed = await db.BackupEntries.AsNoTracking()
                .Where(e => e.CreatedAt >= started && e.Status == BackupStatus.Failed)
                .Select(e => new { e.Slug, e.Error }).ToListAsync(ct);
            problems.AddRange(failed.Select(f => $"Kopia {f.Slug} nie powstała: {FirstLine(f.Error)}"));
        }

        foreach (var e in await offsite.UploadPendingAsync(ct))
            if (e.OffsiteOk == false) problems.Add($"Kopia {e.Slug} nie trafiła poza serwer — {e.OffsiteInfo}");
        try
        {
            var pruned = await offsite.PruneAsync(ct);
            if (pruned > 0) logger.LogInformation("Purged {N} old offsite backups", pruned);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Offsite backup cleanup failed");
        }

        var removed = await backups.CleanupOldAsync();
        if (removed > 0) logger.LogInformation("Purged {N} old backup files", removed);

        if (weeklyVerify && await settings.GetAsync(SiteSettingsService.Keys.BackupVerify) != "off")
        {
            foreach (var e in await backups.VerifyLatestAsync(ct))
                if (e.VerifyOk == false) problems.Add($"Kopia {e.Slug} z {e.CreatedAt:dd.MM} nie daje się odtworzyć — {e.VerifyInfo}");
        }

        if (problems.Count > 0) await NotifyAsync(problems);
    }

    public async Task<bool> NotifyAsync(IReadOnlyList<string> problems, bool test = false)
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.AdminNotificationEmail,
            SiteSettingsService.Keys.AdminNotificationPhone, SiteSettingsService.Keys.ErrorAlertSms);
        var adminEmail = s[SiteSettingsService.Keys.AdminNotificationEmail];
        // SMS tylko, gdy administrator włączył alarmy SMS w zakładce Alarmy.
        var phone = s[SiteSettingsService.Keys.ErrorAlertSms] == "true" ? s[SiteSettingsService.Keys.AdminNotificationPhone] : "";
        var link = $"{(config.GetValue<string>("Portal:PublicUrl") ?? "").TrimEnd('/')}/panel/backups";
        var prefix = test ? "[TEST] " : "";
        var sent = false;

        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var items = string.Join("", problems.Select(p => $"<li style=\"margin-bottom:6px\">{WebUtility.HtmlEncode(p)}</li>"));
            var html = $"""
                <div style="font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:600px;margin:0 auto;color:#0f172a">
                  <h2 style="color:#DC2626;margin-bottom:4px">{prefix}Problem z kopiami zapasowymi</h2>
                  <ul>{items}</ul>
                  <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#7C3AED;color:#fff;text-decoration:none;padding:10px 20px;border-radius:8px;font-weight:600">Otwórz kopie zapasowe</a></p>
                </div>
                """;
            var (ok, _) = await email.SendAsync(adminEmail, $"{prefix}Kopie zapasowe: {problems.Count} {(problems.Count == 1 ? "problem" : "problemy")}", html);
            sent |= ok;
        }
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var (ok, _) = await credits.SendPlatformSmsAsync(phone, $"PTScheduler{(test ? " TEST" : "")}: problem z kopiami zapasowymi ({problems.Count}). {link}");
            sent |= ok;
        }
        logger.LogWarning("Kopie zapasowe: {Count} problemów: {Problems}", problems.Count, string.Join(" | ", problems));
        return sent;
    }

    private static string FirstLine(string? s) =>
        (s ?? "").Replace("[stderr]", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "nieznany błąd";
}
