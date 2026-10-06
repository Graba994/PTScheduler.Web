using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Odtwarzanie trenerów z kopii: jednego (w miejsce obecnej instancji albo do innej) i wszystkich,
/// którym brakuje kontenerów (nowy serwer). Brakujące pliki kopii pobiera spoza serwera.
/// Przed nadpisaniem działającej instancji zawsze robi kopię jej obecnego stanu.
/// Bazę samego Portalu przywraca Guardian — Portal nie może bezpiecznie podmienić własnej bazy.
/// </summary>
public class RestoreService(
    IDbContextFactory<PortalDbContext> dbFactory,
    BackupService backups,
    OffsiteBackupService offsite,
    TenantService tenants,
    DockerService docker,
    ILogger<RestoreService> logger)
{
    public async Task<(bool Ok, string Message)> RestoreTenantAsync(int backupId, int targetTenantId, Action<string> log, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.BackupEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == backupId, ct);
        if (entry is null || entry.Status != BackupStatus.Completed) return (false, "Ta kopia nie istnieje albo się nie udała.");
        if (entry.Slug == "portal") return (false, "Bazę Portalu przywraca Guardian (panel Guardiana → Kopie Portalu).");
        var target = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == targetTenantId, ct);
        if (target is null) return (false, "Nie ma takiej instancji.");

        var path = entry.FilePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            log("Pliku kopii nie ma na tym serwerze — szukam go poza serwerem…");
            var name = Path.GetFileName(path);
            var remote = (await offsite.ListRemoteAsync(ct))
                .FirstOrDefault(r => r.Slug == entry.Slug && (r.Name == name || r.Name == name + ".enc"));
            if (remote is null) return (false, "Pliku kopii nie ma ani na tym serwerze, ani poza nim.");
            log($"Pobieram {remote.Name}…");
            path = await offsite.DownloadAsync(remote, Path.Combine(await backups.ResolveDirAsync(), "tenants", entry.Slug), ct);
        }

        log($"Odtwarzam kopię {entry.Slug} z {entry.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} do instancji {target.Slug}.");
        return await RestoreFileAsync(path, target, log);
    }

    /// <summary>Nowy serwer: każda aktywna instancja bez kontenera bazy dostaje kontenery i najnowszą kopię.</summary>
    public async Task<(bool Ok, string Message)> RestoreMissingAsync(Action<string> log, CancellationToken ct)
    {
        await backups.SyncFilesAsync();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var active = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).OrderBy(t => t.Slug).ToListAsync(ct);
        var dir = await backups.ResolveDirAsync();
        int restored = 0, skipped = 0;
        var failed = new List<string>();

        foreach (var t in active)
        {
            ct.ThrowIfCancellationRequested();
            if (await docker.GetContainerInfoAsync(BackupService.DbContainer(t)) is not null)
            {
                log($"{t.Slug}: instancja istnieje — pomijam (pojedynczą odtworzysz z historii kopii).");
                skipped++;
                continue;
            }

            var local = (await db.BackupEntries.AsNoTracking()
                    .Where(e => e.Slug == t.Slug && e.Status == BackupStatus.Completed && e.FilePath != "" && e.Kind != BackupKind.PreRestore)
                    .OrderByDescending(e => e.CreatedAt).Take(10).ToListAsync(ct))
                .FirstOrDefault(e => File.Exists(e.FilePath));
            OffsiteBackupService.RemoteBackup? remote = null;
            try
            {
                // Kopia „przed odtworzeniem” to stan sprzed ręcznego cofnięcia — nie traktujemy jej jako najnowszych danych.
                remote = (await offsite.ListRemoteAsync(ct))
                    .Where(r => r.Slug == t.Slug && !r.Name.Contains("_przed-", StringComparison.Ordinal)).MaxBy(r => r.CreatedUtc);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log($"{t.Slug}: nie udało się sprawdzić kopii poza serwerem — {ex.Message}"); }

            string? path = local?.FilePath;
            if (remote is not null && (local is null || remote.CreatedUtc > local.CreatedAt.AddMinutes(1)))
            {
                log($"{t.Slug}: pobieram {remote.Name} spoza serwera…");
                try { path = await offsite.DownloadAsync(remote, Path.Combine(dir, "tenants", t.Slug), ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log($"{t.Slug}: pobranie nie powiodło się — {ex.Message}");
                    path = local?.FilePath;
                }
            }
            if (path is null)
            {
                failed.Add($"{t.Slug}: brak kopii");
                log($"{t.Slug}: nie ma żadnej kopii — pomijam.");
                continue;
            }

            var (ok, msg) = await RestoreFileAsync(path, t, m => log($"{t.Slug}: {m}"));
            if (ok) restored++; else failed.Add($"{t.Slug}: {msg}");
            log($"{t.Slug}: {(ok ? "gotowe" : "problem")} — {msg}");
        }
        await backups.SyncFilesAsync();

        var summary = $"Odtworzono: {restored}, pominięto działających: {skipped}" + (failed.Count > 0 ? $", problemy: {string.Join("; ", failed)}" : ".");
        return (failed.Count == 0, summary);
    }

    private async Task<(bool Ok, string Message)> RestoreFileAsync(string path, Tenant target, Action<string> log)
    {
        var dbName = BackupService.DbContainer(target);
        var dbInfo = await docker.GetContainerInfoAsync(dbName);
        var webInfo = await docker.GetContainerInfoAsync(BackupService.WebContainer(target));

        if (dbInfo is not null)
        {
            if (!dbInfo.Running)
            {
                log($"Uruchamiam bazę {dbName}…");
                try { await docker.StartContainerAsync(dbName); } catch (Exception ex) { return (false, $"Baza {dbName} nie chce wystartować: {ex.Message}"); }
            }
            log("Robię kopię obecnego stanu (na wypadek, gdyby trzeba było cofnąć)…");
            var safety = await backups.BackupTenantAsync(target.Id, BackupKind.PreRestore);
            if (safety.Status != BackupStatus.Completed)
                return (false, "Nie udało się zrobić kopii obecnego stanu — przerywam, żeby niczego nie stracić. " + BackupService.FirstLine(safety.Error));
            log($"Kopia obecnego stanu: {Path.GetFileName(safety.FilePath)}.");
        }
        if (dbInfo is null || webInfo is null)
        {
            log("Zakładam kontenery instancji…");
            var (provisioned, msg) = await tenants.ProvisionAsync(target.Id);
            if (!provisioned) return (false, "Nie udało się założyć kontenerów: " + msg);
            log(msg.Replace('\n', ' '));
        }

        var (ok, result) = await backups.RestoreIntoTenantAsync(path, target, log);
        await using var db = await dbFactory.CreateDbContextAsync();
        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = target.Id,
            EventType = ok ? TenantEventTypes.BackupRestored : TenantEventTypes.BackupVerifyFailed,
            Detail = Trim($"{Path.GetFileName(path)}: {result}", 500)
        });
        await db.SaveChangesAsync();
        logger.LogWarning("Odtworzono {Slug} z {File}: {Result}", target.Slug, path, result);
        return (ok, result);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Jedno długie zadanie na raz (odtwarzanie), z dziennikiem widocznym na stronie kopii — także po jej odświeżeniu.</summary>
public sealed class BackupJobs(IServiceScopeFactory scopes, ILogger<BackupJobs> logger)
{
    public sealed class Job
    {
        public string Title { get; init; } = "";
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public bool? Ok { get; set; }
        public string? Result { get; set; }
        private readonly List<string> _lines = [];
        public IReadOnlyList<string> Lines { get { lock (_lines) return [.. _lines]; } }
        public void Add(string line) { lock (_lines) _lines.Add($"{DateTime.Now:HH:mm:ss}  {line}"); }
    }

    private readonly object _gate = new();
    private Job? _current;
    private Job? _last;

    public Job? Current { get { lock (_gate) return _current; } }
    public Job? Last { get { lock (_gate) return _last; } }

    public (bool Started, string? Error) Start(string title, Func<IServiceProvider, Action<string>, CancellationToken, Task<(bool Ok, string Message)>> work)
    {
        Job job;
        lock (_gate)
        {
            if (_current is not null) return (false, $"Trwa już: {_current.Title}. Poczekaj na koniec.");
            job = _current = new Job { Title = title };
        }
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                using var cts = new CancellationTokenSource(TimeSpan.FromHours(4));
                var (ok, message) = await work(scope.ServiceProvider, job.Add, cts.Token);
                job.Ok = ok;
                job.Result = message;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Zadanie {Title} nie powiodło się.", title);
                job.Ok = false;
                job.Result = ex.Message;
            }
            finally
            {
                job.CompletedAt = DateTime.UtcNow;
                job.Add(job.Ok == true ? "Zakończono." : "Zakończono z problemami.");
                lock (_gate) { _last = job; _current = null; }
            }
        });
        return (true, null);
    }
}
