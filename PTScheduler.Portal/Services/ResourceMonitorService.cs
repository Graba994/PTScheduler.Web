using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Co 5 minut zapisuje zużycie zasobów serwera i każdej aktywnej instancji (procesor, pamięć),
/// co godzinę rozmiar bazy i plików trenerów. Po 2 dniach zostaje jedna próbka na godzinę, po 120 dniach nic.
/// Raz na dobę wysyła e-mail, gdy serwerowi kończy się dysk albo pamięć.
/// </summary>
public class ResourceMonitorService(IServiceScopeFactory scopes, ILogger<ResourceMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiskInterval = TimeSpan.FromMinutes(55);
    private DateTime _lastAlertCheck = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await SampleAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Pomiar zasobów nie powiódł się."); }
            try { await Task.Delay(Interval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task SampleAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var dbFactory = sp.GetRequiredService<IDbContextFactory<PortalDbContext>>();
        var docker = sp.GetRequiredService<DockerService>();
        var settings = sp.GetRequiredService<SiteSettingsService>();
        var now = DateTime.UtcNow;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.ResourceSamples.Add(await HostSampleAsync(docker, settings, now));

        var tenants = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).ToListAsync(ct);
        var lastDisk = await db.ResourceSamples.AsNoTracking()
            .Where(s => s.TenantId != null && s.DbSizeBytes != null && s.At > now.AddHours(-2))
            .GroupBy(s => s.TenantId).Select(g => new { Id = g.Key!.Value, At = g.Max(s => s.At) })
            .ToDictionaryAsync(x => x.Id, x => x.At, ct);

        foreach (var t in tenants)
        {
            var web = BackupService.WebContainer(t);
            var dbName = BackupService.DbContainer(t);
            var w = await docker.GetUsageAsync(web, ct);
            var d = await docker.GetUsageAsync(dbName, ct);
            if (w is null && d is null) continue;
            var sample = new ResourceSample
            {
                TenantId = t.Id,
                At = now,
                CpuCores = (w?.CpuCores ?? 0) + (d?.CpuCores ?? 0),
                CpuLimitCores = (w?.CpuLimitCores ?? 0) + (d?.CpuLimitCores ?? 0),
                WebMemoryBytes = w?.MemoryBytes,
                DbMemoryBytes = d?.MemoryBytes,
                MemoryBytes = (w?.MemoryBytes ?? 0) + (d?.MemoryBytes ?? 0),
                MemoryLimitBytes = (w?.MemoryLimitBytes ?? 0) + (d?.MemoryLimitBytes ?? 0)
            };
            if (!lastDisk.TryGetValue(t.Id, out var at) || now - at > DiskInterval)
            {
                if (d is not null) sample.DbSizeBytes = await DbSizeAsync(dbName);
                if (w is not null) sample.FilesBytes = await FilesSizeAsync(web);
            }
            db.ResourceSamples.Add(sample);
        }
        await db.SaveChangesAsync(ct);

        // Po 2 dniach jedna próbka na godzinę (pierwsza w danej godzinie), po 120 dniach usuwamy.
        var thinBefore = now.AddDays(-2);
        await db.ResourceSamples.Where(s => s.At < thinBefore && s.At.Minute >= 5).ExecuteDeleteAsync(ct);
        await db.ResourceSamples.Where(s => s.At < now.AddDays(-120)).ExecuteDeleteAsync(ct);

        if (now - _lastAlertCheck > TimeSpan.FromHours(24))
        {
            _lastAlertCheck = now;
            await AlertIfNeededAsync(sp, ct);
        }
    }

    private static async Task<ResourceSample> HostSampleAsync(DockerService docker, SiteSettingsService settings, DateTime now)
    {
        var sample = new ResourceSample { At = now };
        // W kontenerze /proc/meminfo i /proc/loadavg pokazują cały serwer, nie sam kontener.
        try
        {
            var mem = File.ReadAllLines("/proc/meminfo")
                .Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => long.TryParse(p[1].Trim().Split(' ')[0], out var kb) ? kb * 1024 : 0);
            if (mem.TryGetValue("MemTotal", out var total) && mem.TryGetValue("MemAvailable", out var available))
            {
                sample.MemoryLimitBytes = total;
                sample.MemoryBytes = total - available;
            }
        }
        catch { /* nie Linux */ }
        try
        {
            var load = File.ReadAllText("/proc/loadavg").Split(' ')[0];
            sample.CpuCores = double.Parse(load, CultureInfo.InvariantCulture);
        }
        catch { /* nie Linux */ }
        try { sample.CpuLimitCores = (await docker.GetSystemInfoAsync()).CpuCount; }
        catch { sample.CpuLimitCores = Environment.ProcessorCount; }

        // Główny system plików kontenera leży na dysku Dockera — jego zajętość to obrazy, kontenery i wolumeny.
        try
        {
            var root = new DriveInfo("/");
            sample.DiskTotalBytes = root.TotalSize;
            sample.DiskUsedBytes = root.TotalSize - root.AvailableFreeSpace;
        }
        catch { }
        try
        {
            var dir = await settings.GetAsync(SiteSettingsService.Keys.BackupDir);
            if (Directory.Exists(dir))
            {
                var backups = new DriveInfo(dir);
                sample.BackupDiskTotalBytes = backups.TotalSize;
                sample.BackupDiskUsedBytes = backups.TotalSize - backups.AvailableFreeSpace;
            }
        }
        catch { }
        return sample;
    }

    private static async Task<long?> DbSizeAsync(string dbContainer)
    {
        var (ok, output) = await Shell.RunAsync(
            $"docker exec {Shell.Quote(dbContainer)} psql -U ptscheduler -d ptscheduler -Atc 'select pg_database_size(current_database())'",
            TimeSpan.FromSeconds(30));
        return ok && long.TryParse(output.Split('\n')[0].Trim(), out var size) ? size : null;
    }

    private static async Task<long?> FilesSizeAsync(string webContainer)
    {
        var (ok, output) = await Shell.RunAsync(
            $"docker exec {Shell.Quote(webContainer)} du -sb {BackupService.BrandingPath}", TimeSpan.FromMinutes(2));
        return ok && long.TryParse(output.Split('\t', ' ', '\n')[0].Trim(), out var size) ? size : null;
    }

    private async Task AlertIfNeededAsync(IServiceProvider sp, CancellationToken ct)
    {
        var report = await sp.GetRequiredService<ResourceReportService>().BuildAsync(ResourceReportService.Period.Week, ct);
        var problems = report.Warnings.Where(w => w.Severity == "danger").Select(w => w.Text).ToList();
        if (problems.Count == 0) return;
        var settings = sp.GetRequiredService<SiteSettingsService>();
        var to = await settings.GetAsync(SiteSettingsService.Keys.AdminNotificationEmail);
        if (string.IsNullOrWhiteSpace(to)) return;
        var items = string.Join("", problems.Select(p => $"<li>{WebUtility.HtmlEncode(p)}</li>"));
        await sp.GetRequiredService<EmailService>().SendAsync(to, "Serwer: kończą się zasoby",
            $"<div style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:600px\"><h2 style=\"color:#b91c1c\">Kończą się zasoby serwera</h2><ul>{items}</ul><p>Szczegóły: Portal → Zasoby.</p></div>");
    }
}
