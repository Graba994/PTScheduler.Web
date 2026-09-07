using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

// Wakes daily at 03:00 UTC when backup_schedule = "daily".
// Backs up all active tenants + portal DB, then purges old files.
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
                var backup = scope.ServiceProvider.GetRequiredService<BackupService>();

                var schedule = await settings.GetAsync(SiteSettingsService.Keys.BackupSchedule);
                if (schedule == "off")
                {
                    logger.LogInformation("Scheduled backup skipped — schedule is 'off'");
                    continue;
                }

                logger.LogInformation("Starting scheduled backup run");
                var (ok, fail, portalOk) = await backup.BackupAllAsync(BackupKind.Scheduled);
                logger.LogInformation("Backup finished — {Ok} tenants OK, {Fail} failed, portal={Portal}",
                    ok, fail, portalOk ? "OK" : "FAIL");

                var removed = await backup.CleanupOldAsync();
                if (removed > 0) logger.LogInformation("Purged {N} old backup files", removed);
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
