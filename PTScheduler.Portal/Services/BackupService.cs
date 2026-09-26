using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public class BackupService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    IConfiguration config,
    ILogger<BackupService> logger)
{
    private async Task<string> ResolveDirAsync()
    {
        var s = await settings.GetAsync(SiteSettingsService.Keys.BackupDir);
        var dir = string.IsNullOrWhiteSpace(s) ? "/opt/ptscheduler/backups" : s;
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "tenants"));
        Directory.CreateDirectory(Path.Combine(dir, "portal"));
        return dir;
    }

    public async Task<BackupEntry> BackupTenantAsync(int tenantId, BackupKind kind = BackupKind.Manual)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var entry = new BackupEntry
        {
            Slug = tenant.Slug,
            Kind = kind,
            Status = BackupStatus.Running,
            FilePath = ""
        };
        db.BackupEntries.Add(entry);
        await db.SaveChangesAsync();

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = await ResolveDirAsync();
            var tenantDir = Path.Combine(dir, "tenants", tenant.Slug);
            Directory.CreateDirectory(tenantDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var filename = $"{tenant.Slug}_{timestamp}.sql.gz";
            var fullPath = Path.Combine(tenantDir, filename);

            var containerName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";

            // docker exec <db> pg_dump ... | gzip > file
            var script = $"docker exec {containerName} pg_dump -U ptscheduler -d ptscheduler --no-owner --no-privileges | gzip -c > {fullPath}";
            var (ok, output) = await RunShellAsync(script, TimeSpan.FromMinutes(30));

            if (!ok || !File.Exists(fullPath))
            {
                entry.Status = BackupStatus.Failed;
                entry.Error = output;
            }
            else
            {
                entry.FilePath = fullPath;
                entry.SizeBytes = new FileInfo(fullPath).Length;
                entry.Status = BackupStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            entry.Status = BackupStatus.Failed;
            entry.Error = ex.Message;
            logger.LogError(ex, "Backup failed for {Slug}", tenant.Slug);
        }

        sw.Stop();
        entry.Duration = sw.Elapsed;
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<BackupEntry> BackupPortalAsync(BackupKind kind = BackupKind.Manual)
    {
        await using var db = dbFactory.CreateDbContext();

        var entry = new BackupEntry
        {
            Slug = "portal",
            Kind = kind,
            Status = BackupStatus.Running,
            FilePath = ""
        };
        db.BackupEntries.Add(entry);
        await db.SaveChangesAsync();

        var sw = Stopwatch.StartNew();
        try
        {
            var dir = await ResolveDirAsync();
            var portalDir = Path.Combine(dir, "portal");
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var filename = $"portal_{timestamp}.sql.gz";
            var fullPath = Path.Combine(portalDir, filename);

            var conn = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("No portal connection string.");

            var (host, port, database, user, password) = ParseConnectionString(conn);

            var env = $"PGPASSWORD='{password}'";
            var script = $"{env} pg_dump -h {host} -p {port} -U {user} -d {database} --no-owner --no-privileges | gzip -c > {fullPath}";
            var (ok, output) = await RunShellAsync(script, TimeSpan.FromMinutes(30));

            if (!ok || !File.Exists(fullPath))
            {
                entry.Status = BackupStatus.Failed;
                entry.Error = output;
            }
            else
            {
                entry.FilePath = fullPath;
                entry.SizeBytes = new FileInfo(fullPath).Length;
                entry.Status = BackupStatus.Completed;
            }
        }
        catch (Exception ex)
        {
            entry.Status = BackupStatus.Failed;
            entry.Error = ex.Message;
            logger.LogError(ex, "Portal backup failed");
        }

        sw.Stop();
        entry.Duration = sw.Elapsed;
        await db.SaveChangesAsync();
        return entry;
    }

    public async Task<(int TenantsOk, int TenantsFailed, bool PortalOk)> BackupAllAsync(BackupKind kind)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenants = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync();

        int ok = 0, fail = 0;
        foreach (var t in tenants)
        {
            var entry = await BackupTenantAsync(t.Id, kind);
            if (entry.Status == BackupStatus.Completed) ok++; else fail++;
        }

        var portalEntry = await BackupPortalAsync(kind);
        return (ok, fail, portalEntry.Status == BackupStatus.Completed);
    }

    public async Task<int> CleanupOldAsync()
    {
        var s = await settings.GetAsync(SiteSettingsService.Keys.BackupRetentionDays);
        if (!int.TryParse(s, out var days) || days <= 0) days = 14;

        var cutoff = DateTime.UtcNow.AddDays(-days);
        var removed = 0;

        await using var db = dbFactory.CreateDbContext();
        var oldEntries = await db.BackupEntries
            .Where(e => e.CreatedAt < cutoff && e.Status == BackupStatus.Completed)
            .ToListAsync();

        foreach (var e in oldEntries)
        {
            try
            {
                if (!string.IsNullOrEmpty(e.FilePath) && File.Exists(e.FilePath))
                    File.Delete(e.FilePath);
                db.BackupEntries.Remove(e);
                removed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete backup {Path}", e.FilePath);
            }
        }

        await db.SaveChangesAsync();
        return removed;
    }

    public async Task<(bool Success, string Output)> RestoreTenantAsync(int backupId, string targetTenantSlug)
    {
        await using var db = dbFactory.CreateDbContext();
        var backup = await db.BackupEntries.FindAsync(backupId);
        if (backup is null) return (false, "Backup nie istnieje.");
        if (!File.Exists(backup.FilePath)) return (false, $"Plik {backup.FilePath} nie istnieje na dysku.");

        var target = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == targetTenantSlug);
        if (target is null) return (false, $"Tenant '{targetTenantSlug}' nie istnieje.");

        var containerName = target.DbContainerName ?? $"pt-{target.Slug}-db";

        // gunzip → psql, drop-and-recreate schema first so restore is clean
        var script =
            $"docker exec {containerName} psql -U ptscheduler -d ptscheduler -c \"DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;\" && " +
            $"gunzip -c {backup.FilePath} | docker exec -i {containerName} psql -U ptscheduler -d ptscheduler";

        var (ok, output) = await RunShellAsync(script, TimeSpan.FromMinutes(30));
        return (ok, output);
    }

    private static async Task<(bool Success, string Output)> RunShellAsync(string bashScript, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{bashScript.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "Process failed to start.");
            using var cts = new CancellationTokenSource(timeout);
            var stdout = await p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = await p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            var output = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n[stderr]\n" + stderr);
            return (p.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (string Host, string Port, string Database, string User, string Password) ParseConnectionString(string conn)
    {
        string get(string key)
        {
            var parts = conn.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var kv = parts.FirstOrDefault(p =>
                p.Trim().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            return kv?.Split('=', 2)[1].Trim() ?? "";
        }
        return (
            get("Host"),
            string.IsNullOrEmpty(get("Port")) ? "5432" : get("Port"),
            get("Database"),
            get("Username"),
            get("Password")
        );
    }
}
