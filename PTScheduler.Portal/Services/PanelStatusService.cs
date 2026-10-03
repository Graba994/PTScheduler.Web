using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Dane do paska statusu nad panelem — „jak na pasku telefonu”: czy instancje działają,
/// co czeka na decyzję, czy jest aktualizacja, stan kopii, dzisiejsze wpłaty i bezpieczeństwo.
/// Wynik trzymamy 30 s, żeby pasek na każdej karcie przeglądarki nie odpytywał bazy co chwilę.
/// </summary>
public class PanelStatusService(
    IDbContextFactory<PortalDbContext> dbFactory,
    DockerService docker,
    UpdateNotifier updates,
    ILogger<PanelStatusService> logger)
{
    public sealed record Snapshot(
        int ActiveTenants,
        int DownTenants,
        int PendingRegistrations,
        int PendingOrders,
        bool UpdateAvailable,
        string? UpdateCommit,
        DateTime? LastBackupAt,
        bool LastBackupFailed,
        bool BackupRunning,
        decimal PaidToday,
        int PaymentsFailedToday,
        int FailedLogins24h,
        bool DockerReachable,
        int ContainersRunning,
        int ContainersTotal,
        DateTime TakenAt);

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Snapshot? _cached;

    public async Task<Snapshot> GetAsync(bool refresh = false)
    {
        if (!refresh && _cached is { } c && DateTime.UtcNow - c.TakenAt < CacheFor) return c;
        await _lock.WaitAsync();
        try
        {
            if (!refresh && _cached is { } c2 && DateTime.UtcNow - c2.TakenAt < CacheFor) return c2;
            return _cached = await BuildAsync();
        }
        finally { _lock.Release(); }
    }

    /// <summary>Północ dziś w Polsce (kontener zwykle działa w UTC).</summary>
    private static DateTime StartOfTodayUtc(DateTime utcNow)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch { tz = TimeZoneInfo.Local; }
        var localMidnight = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz).Date;
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), tz);
    }

    private async Task<Snapshot> BuildAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var todayUtc = StartOfTodayUtc(now);

        var active = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active);
        var down = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active && t.IsHealthy == false);
        var pendingRegs = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Pending);
        var pendingOrders = await db.ServiceOrders.CountAsync(o => o.Status == ServiceOrderStatus.Pending);

        var lastBackup = await db.BackupEntries.AsNoTracking()
            .Where(b => b.Status != BackupStatus.Running)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new { b.CreatedAt, b.Status })
            .FirstOrDefaultAsync();
        var lastGoodBackup = await db.BackupEntries.AsNoTracking()
            .Where(b => b.Status == BackupStatus.Completed)
            .MaxAsync(b => (DateTime?)b.CreatedAt);
        var backupRunning = await db.BackupEntries.AnyAsync(b => b.Status == BackupStatus.Running && b.CreatedAt > now.AddHours(-3));

        var paidToday = await db.PaymentRecords
            .Where(p => p.Status == PaymentRecordStatus.Paid && p.CreatedAt >= todayUtc)
            .SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var failedToday = await db.PaymentRecords.CountAsync(p => p.Status == PaymentRecordStatus.Failed && p.CreatedAt >= todayUtc);
        var failedLogins = await db.LoginLogs.CountAsync(l => !l.Success && l.CreatedAt >= now.AddHours(-24));

        bool dockerOk = false;
        int running = 0, total = 0;
        try
        {
            var sys = await docker.GetSystemInfoAsync().WaitAsync(TimeSpan.FromSeconds(4));
            (dockerOk, running, total) = (true, sys.RunningContainers, sys.TotalContainers);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Pasek statusu: Docker niedostępny."); }

        return new Snapshot(active, down, pendingRegs, pendingOrders, updates.UpgradeAvailable, updates.RemoteCommitShort,
            lastGoodBackup, lastBackup?.Status == BackupStatus.Failed, backupRunning,
            paidToday, failedToday, failedLogins, dockerOk, running, total, now);
    }
}
