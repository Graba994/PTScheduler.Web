using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Harmonogram kopii (UTC): „daily” — 3:00, „12h” — 3:00 i 15:00, „6h” — 3:00, 9:00, 15:00, 21:00.
/// W niedzielę o 3:00 dodatkowo test odtworzenia najnowszych kopii.
/// </summary>
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
                string schedule;
                using (var scope = scopeFactory.CreateScope())
                    schedule = await scope.ServiceProvider.GetRequiredService<SiteSettingsService>().GetAsync(SiteSettingsService.Keys.BackupSchedule);
                if (Hours(schedule) is not { } hours)
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                    continue;
                }

                var next = NextRun(DateTime.UtcNow, hours);
                var wait = next - DateTime.UtcNow;
                // Co kwadrans sprawdzamy harmonogram od nowa — zmiana w panelu działa bez restartu.
                if (wait > TimeSpan.FromMinutes(15))
                {
                    await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
                    continue;
                }
                if (wait > TimeSpan.Zero) await Task.Delay(wait, stoppingToken);

                using var runScope = scopeFactory.CreateScope();
                await runScope.ServiceProvider.GetRequiredService<BackupMaintenanceService>().RunCycleAsync(
                    BackupKind.Scheduled, verify: next.DayOfWeek == DayOfWeek.Sunday && next.Hour == 3,
                    housekeeping: true, notify: true, stoppingToken);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup scheduler iteration failed");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }

    public static int[]? Hours(string schedule) => schedule switch
    {
        "off" => null,
        "6h" => [3, 9, 15, 21],
        "12h" => [3, 15],
        _ => [3]
    };

    public static DateTime NextRun(DateTime nowUtc, int[] hours)
    {
        for (var day = 0; day < 2; day++)
            foreach (var h in hours)
            {
                var t = nowUtc.Date.AddDays(day).AddHours(h);
                if (t > nowUtc) return t;
            }
        return nowUtc.Date.AddDays(1).AddHours(hours[0]);
    }
}

/// <summary>
/// Cykl kopii: trenerzy → wysyłka ich kopii poza serwer → Portal (jego baza wie już, co jest poza serwerem)
/// → wysyłka kopii Portalu → sprzątanie → (niedziela) test odtworzenia → alarm przy problemach.
/// </summary>
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
    public sealed record CycleResult(int TenantsOk, int TenantsFailed, bool PortalOk, int OffsiteFailed, List<string> Problems);

    public async Task<CycleResult> RunCycleAsync(BackupKind kind, bool verify, bool housekeeping, bool notify, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var problems = new List<string>();
        var offsiteProblems = new Dictionary<int, string>();

        logger.LogInformation("Starting backup cycle ({Kind})", kind);
        var (ok, fail) = await backups.BackupTenantsAsync(kind);
        foreach (var e in await offsite.UploadPendingAsync(ct)) Track(e);
        var portal = await backups.BackupPortalAsync(kind);
        foreach (var e in await offsite.UploadPendingAsync(ct)) Track(e);
        logger.LogInformation("Backup cycle — {Ok} tenants OK, {Fail} failed, portal={Portal}", ok, fail, portal.Status == BackupStatus.Completed ? "OK" : "FAIL");

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var failed = await db.BackupEntries.AsNoTracking()
                .Where(e => e.CreatedAt >= started && e.Status == BackupStatus.Failed)
                .Select(e => new { e.Slug, e.Error }).ToListAsync(ct);
            problems.AddRange(failed.Select(f => $"Kopia {f.Slug} nie powstała: {FirstLine(f.Error)}"));
        }
        problems.AddRange(offsiteProblems.Values);

        if (housekeeping)
        {
            try
            {
                var pruned = await offsite.PruneAsync(ct);
                if (pruned > 0) logger.LogInformation("Purged {N} old offsite backups", pruned);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Offsite backup cleanup failed"); }
            var removed = await backups.CleanupOldAsync();
            if (removed > 0) logger.LogInformation("Purged {N} old backup files", removed);
            await backups.SyncFilesAsync();
        }

        if (verify && await settings.GetAsync(SiteSettingsService.Keys.BackupVerify) != "off")
        {
            foreach (var e in await backups.VerifyLatestAsync(ct))
                if (e.VerifyOk == false) problems.Add($"Kopia {e.Slug} z {e.CreatedAt:dd.MM} nie daje się odtworzyć — {e.VerifyInfo}");
        }

        if (notify && problems.Count > 0) await NotifyAsync(problems);
        return new CycleResult(ok, fail, portal.Status == BackupStatus.Completed, offsiteProblems.Count, problems);

        // Ta sama kopia może być ponawiana w drugiej rundzie wysyłki — liczy się ostatni wynik.
        void Track(BackupEntry e)
        {
            if (e.OffsiteOk == false) offsiteProblems[e.Id] = $"Kopia {e.Slug} nie trafiła poza serwer — {e.OffsiteInfo}";
            else offsiteProblems.Remove(e.Id);
        }
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
