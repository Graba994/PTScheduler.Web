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
    /// <summary>
    /// Jak dostać się do bazy: <c>Psql</c>/<c>PgDump</c> to początek polecenia (bez nazwy bazy),
    /// <c>Env</c> — zmienne środowiska (hasło nie trafia do linii poleceń).
    /// </summary>
    private sealed record PgAccess(string Psql, string PgDump, string Database, Dictionary<string, string> Env);

    private async Task<string> ResolveDirAsync()
    {
        var s = await settings.GetAsync(SiteSettingsService.Keys.BackupDir);
        var dir = string.IsNullOrWhiteSpace(s) ? "/opt/ptscheduler/backups" : s;
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "tenants"));
        Directory.CreateDirectory(Path.Combine(dir, "portal"));
        return dir;
    }

    private static PgAccess TenantPg(Tenant tenant)
    {
        var container = Shell.Quote(tenant.DbContainerName ?? $"pt-{tenant.Slug}-db");
        return new PgAccess($"docker exec -i {container} psql -U ptscheduler", $"docker exec {container} pg_dump -U ptscheduler", "ptscheduler", []);
    }

    /// <summary>
    /// Baza Portalu: lokalne pg_dump/psql, jeśli są (instalacja bez Dockera), inaczej przez kontener
    /// bazy Portalu — obraz Portalu nie ma klienta PostgreSQL, a w kontenerze bazy wersje zawsze pasują.
    /// </summary>
    private async Task<PgAccess> PortalPgAsync()
    {
        var conn = config.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No portal connection string.");
        var (host, port, database, user, password) = ParseConnectionString(conn);

        var (local, _) = await Shell.RunAsync("command -v pg_dump && command -v psql", TimeSpan.FromSeconds(10));
        if (local)
        {
            var target = $"-h {Shell.Quote(host)} -p {Shell.Quote(port)} -U {Shell.Quote(user)}";
            return new PgAccess($"psql {target}", $"pg_dump {target}", database, new() { ["PGPASSWORD"] = password });
        }
        var container = Shell.Quote(config["Portal:DbContainerName"] ?? "ptportal-db");
        return new PgAccess($"docker exec -i {container} psql -U {Shell.Quote(user)}",
            $"docker exec {container} pg_dump -U {Shell.Quote(user)}", database, []);
    }

    public async Task<BackupEntry> BackupTenantAsync(int tenantId, BackupKind kind = BackupKind.Manual)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");
        var dir = Path.Combine(await ResolveDirAsync(), "tenants", tenant.Slug);
        return await DumpAsync(tenant.Slug, kind, dir, TenantPg(tenant));
    }

    public async Task<BackupEntry> BackupPortalAsync(BackupKind kind = BackupKind.Manual)
    {
        var dir = Path.Combine(await ResolveDirAsync(), "portal");
        PgAccess pg;
        try { pg = await PortalPgAsync(); }
        catch (Exception ex)
        {
            await using var db = dbFactory.CreateDbContext();
            var failed = new BackupEntry { Slug = "portal", Kind = kind, Status = BackupStatus.Failed, Error = ex.Message };
            db.BackupEntries.Add(failed);
            await db.SaveChangesAsync();
            return failed;
        }
        return await DumpAsync("portal", kind, dir, pg);
    }

    private async Task<BackupEntry> DumpAsync(string slug, BackupKind kind, string dir, PgAccess pg)
    {
        await using var db = dbFactory.CreateDbContext();
        var entry = new BackupEntry { Slug = slug, Kind = kind, Status = BackupStatus.Running, FilePath = "" };
        db.BackupEntries.Add(entry);
        await db.SaveChangesAsync();

        var sw = Stopwatch.StartNew();
        var fullPath = "";
        try
        {
            Directory.CreateDirectory(dir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            fullPath = Path.Combine(dir, $"{slug}_{timestamp}.sql.gz");

            // pipefail: bez niego błąd pg_dump ginął za gzipem i powstawała pusta „udana” kopia.
            var script = $"set -o pipefail; {pg.PgDump} -d {Shell.Quote(pg.Database)} --no-owner --no-privileges | gzip -c > {Shell.Quote(fullPath)}";
            var (ok, output) = await Shell.RunAsync(script, TimeSpan.FromMinutes(30), pg.Env);

            if (!ok || !File.Exists(fullPath))
            {
                entry.Status = BackupStatus.Failed;
                entry.Error = string.IsNullOrWhiteSpace(output) ? "pg_dump zakończył się błędem." : output;
                TryDelete(fullPath);
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
            TryDelete(fullPath);
            logger.LogError(ex, "Backup failed for {Slug}", slug);
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

        var pg = TenantPg(target);
        // gunzip → psql, drop-and-recreate schema first so restore is clean
        var script =
            $"{pg.Psql} -d {pg.Database} -c 'DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;' && " +
            $"gunzip -c {Shell.Quote(backup.FilePath)} | {pg.Psql} -d {pg.Database}";
        return await Shell.RunAsync(script, TimeSpan.FromMinutes(30));
    }

    // ── Test odtworzenia ────────────────────────────────────────────────────

    /// <summary>
    /// Odtwarza kopię do tymczasowej bazy obok oryginału (ta sama wersja PostgreSQL), sprawdza
    /// schemat i historię migracji, po czym bazę usuwa. Produkcyjnej bazy nic nie dotyka.
    /// </summary>
    public async Task<BackupEntry?> VerifyAsync(int backupId)
    {
        await using var db = dbFactory.CreateDbContext();
        var entry = await db.BackupEntries.FindAsync(backupId);
        if (entry is null || entry.Status != BackupStatus.Completed) return entry;

        var (ok, info) = await RunVerifyAsync(db, entry);
        entry.VerifiedAt = DateTime.UtcNow;
        entry.VerifyOk = ok;
        entry.VerifyInfo = Trim(info, 1000);

        if (entry.Slug != "portal" && await db.Tenants.FirstOrDefaultAsync(t => t.Slug == entry.Slug) is { } tenant)
        {
            db.TenantEvents.Add(new TenantEvent
            {
                TenantId = tenant.Id,
                EventType = ok ? TenantEventTypes.BackupVerified : TenantEventTypes.BackupVerifyFailed,
                Detail = Trim(info, 500)
            });
        }
        await db.SaveChangesAsync();
        if (!ok) logger.LogWarning("Test odtworzenia kopii {Id} ({Slug}) nie powiódł się: {Info}", entry.Id, entry.Slug, info);
        return entry;
    }

    /// <summary>Najnowsza udana kopia każdej aktywnej instancji i Portalu.</summary>
    public async Task<List<BackupEntry>> VerifyLatestAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var slugs = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).Select(t => t.Slug).ToListAsync(ct);
        slugs.Add("portal");
        var ids = new List<int>();
        foreach (var slug in slugs)
        {
            var id = await db.BackupEntries.AsNoTracking()
                .Where(e => e.Slug == slug && e.Status == BackupStatus.Completed && e.FilePath != "")
                .OrderByDescending(e => e.CreatedAt).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
            if (id is int i) ids.Add(i);
        }

        var results = new List<BackupEntry>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (await VerifyAsync(id) is { } e) results.Add(e);
        }
        return results;
    }

    private async Task<(bool Ok, string Info)> RunVerifyAsync(PortalDbContext db, BackupEntry entry)
    {
        if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
            return (false, "Plik kopii nie istnieje na dysku.");

        PgAccess pg;
        if (entry.Slug == "portal")
        {
            try { pg = await PortalPgAsync(); }
            catch (Exception ex) { return (false, ex.Message); }
        }
        else
        {
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == entry.Slug);
            if (tenant is null) return (false, "Instancja już nie istnieje — nie ma gdzie odtworzyć kopii.");
            pg = TenantPg(tenant);
        }

        var file = Shell.Quote(entry.FilePath);
        var (gzOk, gzOut) = await Shell.RunAsync($"gzip -t {file}", TimeSpan.FromMinutes(10));
        if (!gzOk) return (false, "Plik kopii jest uszkodzony: " + FirstLine(gzOut));

        var tmp = $"pts_verify_{entry.Id}";
        var drop = $"{pg.Psql} -d postgres -q -c 'DROP DATABASE IF EXISTS {tmp} WITH (FORCE)'";
        try
        {
            var (created, createOut) = await Shell.RunAsync(
                $"{drop} && {pg.Psql} -d postgres -q -v ON_ERROR_STOP=1 -c 'CREATE DATABASE {tmp}'", TimeSpan.FromMinutes(2), pg.Env);
            if (!created) return (false, "Nie udało się utworzyć bazy testowej: " + FirstLine(createOut));

            var (_, restoreOut) = await Shell.RunAsync(
                $"set -o pipefail; gunzip -c {file} | {pg.Psql} -d {tmp} -q -o /dev/null", TimeSpan.FromMinutes(60), pg.Env);
            var errors = restoreOut.Split('\n').Where(l => l.Contains("ERROR:", StringComparison.Ordinal)).ToList();

            var sql = Shell.Quote("select count(*) from information_schema.tables where table_schema = current_schema()");
            var sqlMig = Shell.Quote("select count(*) || ' ' || coalesce(max(\"MigrationId\"), '-') from \"__EFMigrationsHistory\"");
            var (queried, queryOut) = await Shell.RunAsync($"{pg.Psql} -d {tmp} -At -c {sql} -c {sqlMig}", TimeSpan.FromMinutes(2), pg.Env);
            var lines = queryOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!queried || lines.Length < 2 || !int.TryParse(lines[0], out var tables))
                return (false, "Kopia odtworzyła się bez tabel aplikacji. " + (errors.Count > 0 ? FirstLine(errors[0]) : FirstLine(queryOut)));

            var mig = lines[1].Split(' ', 2);
            var migrations = int.TryParse(mig[0], out var m) ? m : 0;
            var info = $"tabele: {tables} · migracje: {migrations} · ostatnia: {(mig.Length > 1 ? mig[1] : "-")}";
            if (errors.Count > 0) return (false, $"{errors.Count} błędów przy odtwarzaniu, np. {FirstLine(errors[0]).Trim()} ({info})");
            if (tables < 5 || migrations == 0) return (false, "Kopia jest niekompletna — " + info);
            return (true, info);
        }
        finally
        {
            await Shell.RunAsync(drop, TimeSpan.FromMinutes(2), pg.Env);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string FirstLine(string s) =>
        s.Replace("[stderr]", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

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

/// <summary>Uruchamia skrypt bash (argumenty bez sklejania w jedną linię — cudzysłowy w SQL przechodzą bez zmian).</summary>
internal static class Shell
{
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public static async Task<(bool Success, string Output)> RunAsync(string bashScript, TimeSpan timeout, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(bashScript);
        if (env is not null)
            foreach (var (k, v) in env) psi.Environment[k] = v;

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (false, "Process failed to start.");
            using var cts = new CancellationTokenSource(timeout);
            var stdoutTask = p.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return (false, $"Przekroczono limit czasu ({timeout.TotalMinutes:0} min).");
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n[stderr]\n" + stderr);
            return (p.ExitCode == 0, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
