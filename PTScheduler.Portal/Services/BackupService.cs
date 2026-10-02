using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Kopie zapasowe. Kopia to jeden plik <c>.tar</c> („pakiet”):
/// <list type="bullet">
/// <item><c>manifest.json</c> — co to za kopia (trener/Portal, domena, plan, wersja aplikacji),</item>
/// <item><c>database.sql.gz</c> — zrzut bazy,</item>
/// <item><c>files.tar.gz</c> — pliki trenera (logo, zdjęcia klientów, dokumenty), tylko u trenerów,</item>
/// <item><c>env/</c> — konfiguracja platformy (.env), tylko w kopii Portalu.</item>
/// </list>
/// Starsze kopie <c>.sql.gz</c> (sama baza) dalej da się sprawdzić i odtworzyć.
/// </summary>
public partial class BackupService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    DockerService docker,
    IConfiguration config,
    ILogger<BackupService> logger)
{
    public const string HelperImage = "postgres:17-alpine";
    public const string BrandingPath = "/app/wwwroot/branding";

    /// <summary>
    /// Jak dostać się do bazy: <c>Psql</c>/<c>PgDump</c> to początek polecenia (bez nazwy bazy),
    /// <c>Env</c> — zmienne środowiska (hasło nie trafia do linii poleceń).
    /// </summary>
    private sealed record PgAccess(string Psql, string PgDump, string Database, Dictionary<string, string> Env);

    public static bool IsBundle(string path) => path.EndsWith(".tar", StringComparison.Ordinal);

    /// <summary>Polecenie powłoki wypisujące spakowany gzipem zrzut bazy z kopii (pakietu albo starego .sql.gz).</summary>
    public static string DbStream(string path) =>
        IsBundle(path) ? $"tar -xOf {Shell.Quote(path)} database.sql.gz" : $"cat {Shell.Quote(path)}";

    public async Task<string> ResolveDirAsync()
    {
        var s = await settings.GetAsync(SiteSettingsService.Keys.BackupDir);
        var dir = string.IsNullOrWhiteSpace(s) ? "/opt/ptscheduler/backups" : s;
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "tenants"));
        Directory.CreateDirectory(Path.Combine(dir, "portal"));
        return dir;
    }

    public static string WebContainer(Tenant t) => t.WebContainerName ?? $"pt-{t.Slug}-web";
    public static string DbContainer(Tenant t) => t.DbContainerName ?? $"pt-{t.Slug}-db";

    private static PgAccess TenantPg(Tenant tenant)
    {
        var container = Shell.Quote(DbContainer(tenant));
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

    /// <summary>Skąd brać pliki trenera: wolumen (albo katalog) podpięty pod branding w kontenerze aplikacji.</summary>
    private async Task<string> BrandingMountAsync(Tenant t)
    {
        var info = await docker.InspectAsync(WebContainer(t));
        var mount = info?.Mounts?.FirstOrDefault(m => m.Destination == BrandingPath);
        if (mount is not null) return mount.Type == "volume" ? mount.Name : mount.Source;
        return $"pt-{t.Slug}-branding";
    }

    // ── Tworzenie kopii ─────────────────────────────────────────────────────

    public async Task<BackupEntry> BackupTenantAsync(int tenantId, BackupKind kind = BackupKind.Manual)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");
        var dir = Path.Combine(await ResolveDirAsync(), "tenants", tenant.Slug);
        var pg = TenantPg(tenant);
        var manifest = new BackupManifest
        {
            Kind = "tenant",
            Slug = tenant.Slug,
            CompanyName = tenant.CompanyName,
            Domain = tenant.Domain,
            Port = tenant.Port,
            PlanId = tenant.PlanId,
            OwnerEmail = tenant.OwnerEmail,
            AppCommit = await settings.GetAsync(SiteSettingsService.Keys.LastTenantBuildCommit)
        };

        return await CreateBundleAsync(tenant.Slug, kind, dir, pg, manifest, async tmp =>
        {
            var mount = await BrandingMountAsync(tenant);
            try { await docker.EnsureImagePulledAsync(HelperImage); } catch (Exception ex) { logger.LogDebug(ex, "Nie pobrano obrazu {Image}.", HelperImage); }
            var (ok, output) = await Shell.RunAsync(
                $"set -o pipefail; docker run --rm -v {Shell.Quote(mount + ":/data:ro")} --entrypoint sh {HelperImage} -c 'cd /data && tar czf - .' > {Shell.Quote(Path.Combine(tmp, "files.tar.gz"))}",
                TimeSpan.FromMinutes(30));
            if (!ok) throw new InvalidOperationException("Nie udało się spakować plików trenera: " + FirstLine(output));
        });
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
        var manifest = new BackupManifest { Kind = "portal", Slug = "portal", AppCommit = Environment.GetEnvironmentVariable("PTS_BUILD_COMMIT") };
        return await CreateBundleAsync("portal", kind, dir, pg, manifest, async tmp =>
        {
            // Konfiguracja platformy: plik .env z repozytorium (jeśli jest) i odtworzona z działających kontenerów
            // (instalacja przez Portainer/Unraid nie ma pliku) — bez niej nowy serwer nie wystartuje.
            var envDir = Path.Combine(tmp, "env");
            Directory.CreateDirectory(envDir);
            var repo = config["Portal:RepoDir"] ?? "/opt/ptscheduler/repo";
            foreach (var name in new[] { ".env.prod", ".env" })
            {
                var path = Path.Combine(repo, name);
                if (File.Exists(path)) File.Copy(path, Path.Combine(envDir, name.TrimStart('.')));
            }
            try { await File.WriteAllTextAsync(Path.Combine(envDir, "containers.env"), await PlatformEnv.BuildAsync(docker, config)); }
            catch (Exception ex) { logger.LogWarning(ex, "Nie udało się odczytać konfiguracji kontenerów do kopii Portalu."); }
        });
    }

    private async Task<BackupEntry> CreateBundleAsync(string slug, BackupKind kind, string dir, PgAccess pg,
        BackupManifest manifest, Func<string, Task>? extra)
    {
        await using var db = dbFactory.CreateDbContext();
        var entry = new BackupEntry { Slug = slug, Kind = kind, Status = BackupStatus.Running, FilePath = "" };
        db.BackupEntries.Add(entry);
        await db.SaveChangesAsync();

        var sw = Stopwatch.StartNew();
        var fullPath = "";
        var tmp = "";
        try
        {
            Directory.CreateDirectory(dir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            fullPath = Path.Combine(dir, $"{slug}_{timestamp}{(kind == BackupKind.PreRestore ? "_przed-odtworzeniem" : "")}.tar");
            tmp = Path.Combine(dir, $".tmp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmp);

            // pipefail: bez niego błąd pg_dump ginął za gzipem i powstawała pusta „udana” kopia.
            var (ok, output) = await Shell.RunAsync(
                $"set -o pipefail; {pg.PgDump} -d {Shell.Quote(pg.Database)} --no-owner --no-privileges | gzip -c > {Shell.Quote(Path.Combine(tmp, "database.sql.gz"))}",
                TimeSpan.FromMinutes(30), pg.Env);
            if (!ok) throw new InvalidOperationException(string.IsNullOrWhiteSpace(output) ? "pg_dump zakończył się błędem." : output);

            if (extra is not null) await extra(tmp);

            manifest.CreatedAtUtc = DateTime.UtcNow;
            await File.WriteAllTextAsync(Path.Combine(tmp, "manifest.json"), JsonSerializer.Serialize(manifest, BackupManifest.Json));
            var parts = new[] { "manifest.json", "database.sql.gz", "files.tar.gz", "env" }.Where(p => Path.Exists(Path.Combine(tmp, p)));
            var (tarOk, tarOut) = await Shell.RunAsync(
                $"tar cf {Shell.Quote(fullPath)} -C {Shell.Quote(tmp)} {string.Join(' ', parts)}", TimeSpan.FromMinutes(30));
            if (!tarOk) throw new InvalidOperationException("Nie udało się złożyć kopii: " + FirstLine(tarOut));

            entry.FilePath = fullPath;
            entry.SizeBytes = new FileInfo(fullPath).Length;
            entry.Status = BackupStatus.Completed;
        }
        catch (Exception ex)
        {
            entry.Status = BackupStatus.Failed;
            entry.Error = ex.Message;
            TryDelete(fullPath);
            logger.LogError(ex, "Backup failed for {Slug}", slug);
        }
        finally
        {
            if (tmp.Length > 0) try { Directory.Delete(tmp, recursive: true); } catch { }
        }

        sw.Stop();
        entry.Duration = sw.Elapsed;
        await db.SaveChangesAsync();
        return entry;
    }

    /// <summary>Kopie wszystkich aktywnych trenerów (bez Portalu — ten po wysłaniu ich poza serwer).</summary>
    public async Task<(int Ok, int Failed)> BackupTenantsAsync(BackupKind kind)
    {
        await using var db = dbFactory.CreateDbContext();
        var ids = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).Select(t => t.Id).ToListAsync();
        int ok = 0, fail = 0;
        foreach (var id in ids)
        {
            var entry = await BackupTenantAsync(id, kind);
            if (entry.Status == BackupStatus.Completed) ok++; else fail++;
        }
        return (ok, fail);
    }

    // ── Sprzątanie ──────────────────────────────────────────────────────────

    /// <summary>
    /// Kopie starsze niż retencja znikają. Po 2 dniach zostaje jedna kopia dziennie (przy kopiach co 6 h).
    /// Najnowszej udanej kopii danej instancji nie usuwamy nigdy — nawet gdy nowe przestały powstawać.
    /// </summary>
    public async Task<int> CleanupOldAsync()
    {
        var s = await settings.GetAsync(SiteSettingsService.Keys.BackupRetentionDays);
        if (!int.TryParse(s, out var days) || days <= 0) days = 14;
        var now = DateTime.UtcNow;

        await using var db = dbFactory.CreateDbContext();
        var entries = await db.BackupEntries.Where(e => e.Status != BackupStatus.Running).ToListAsync();
        var keep = entries.Where(e => e.Status == BackupStatus.Completed)
            .GroupBy(e => e.Slug).Select(g => g.MaxBy(e => e.CreatedAt)!.Id).ToHashSet();
        var dailyNewest = entries.Where(e => e.Status == BackupStatus.Completed)
            .GroupBy(e => (e.Slug, e.CreatedAt.Date)).Select(g => g.MaxBy(e => e.CreatedAt)!.Id).ToHashSet();

        var removed = 0;
        foreach (var e in entries)
        {
            if (keep.Contains(e.Id)) continue;
            var expired = e.CreatedAt < now.AddDays(-days);
            var thinned = e.Status == BackupStatus.Completed && e.Kind != BackupKind.PreRestore
                && e.CreatedAt < now.AddDays(-2) && !dailyNewest.Contains(e.Id);
            if (!expired && !thinned) continue;
            try
            {
                if (!string.IsNullOrEmpty(e.FilePath) && File.Exists(e.FilePath)) File.Delete(e.FilePath);
                db.BackupEntries.Remove(e);
                removed++;
            }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete backup {Path}", e.FilePath); }
        }
        await db.SaveChangesAsync();
        return removed;
    }

    /// <summary>
    /// Dopisuje do historii pliki kopii, których Portal nie zna: zapasowe kopie Guardiana, pliki skopiowane
    /// ręcznie albo pobrane spoza serwera (np. po przeniesieniu na nowy serwer).
    /// </summary>
    public async Task<int> SyncFilesAsync()
    {
        var dir = await ResolveDirAsync();
        await using var db = dbFactory.CreateDbContext();
        var known = (await db.BackupEntries.Select(e => e.FilePath).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        var found = Directory.EnumerateFiles(Path.Combine(dir, "portal")).Select(f => (Slug: "portal", Path: f))
            .Concat(Directory.EnumerateDirectories(Path.Combine(dir, "tenants"))
                .SelectMany(d => Directory.EnumerateFiles(d).Select(f => (Slug: Path.GetFileName(d), Path: f))));
        var added = 0;
        foreach (var (slug, path) in found)
        {
            var name = Path.GetFileName(path);
            if (known.Contains(path) || name.StartsWith('.') || !(IsBundle(path) || name.EndsWith(".sql.gz", StringComparison.Ordinal))) continue;
            var ts = FileStamp(name) ?? File.GetLastWriteTimeUtc(path);
            db.BackupEntries.Add(new BackupEntry
            {
                Slug = slug,
                FilePath = path,
                SizeBytes = new FileInfo(path).Length,
                Kind = name.Contains("_guardian", StringComparison.Ordinal) ? BackupKind.Guardian
                    : name.Contains("_przed-", StringComparison.Ordinal) ? BackupKind.PreRestore : BackupKind.Imported,
                Status = BackupStatus.Completed,
                CreatedAt = ts
            });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync();
        return added;
    }

    /// <summary>Data z nazwy pliku kopii (<c>slug_yyyyMMdd_HHmmss…</c>).</summary>
    public static DateTime? FileStamp(string fileName)
    {
        var m = StampRegex().Match(fileName);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    }

    [GeneratedRegex(@"_(\d{8}_\d{6})")]
    private static partial Regex StampRegex();

    // ── Odtwarzanie ─────────────────────────────────────────────────────────

    /// <summary>
    /// Wgrywa kopię do istniejących kontenerów trenera: zatrzymuje aplikację, odtwarza bazę i pliki,
    /// uruchamia aplikację. Kopię bieżącego stanu robi wcześniej <see cref="RestoreService"/>.
    /// </summary>
    public async Task<(bool Ok, string Message)> RestoreIntoTenantAsync(string backupPath, Tenant target, Action<string> log)
    {
        if (!File.Exists(backupPath)) return (false, $"Plik {backupPath} nie istnieje.");
        var pg = TenantPg(target);
        var web = WebContainer(target);

        log("Czekam, aż baza trenera będzie gotowa…");
        if (!await WaitForDbAsync(pg, TimeSpan.FromSeconds(120)))
            return (false, $"Baza {DbContainer(target)} nie odpowiada.");

        log($"Zatrzymuję aplikację {web}…");
        try { await docker.StopContainerAsync(web); } catch (Exception ex) { logger.LogDebug(ex, "Kontener {Web} nie był uruchomiony.", web); }

        var warnings = new List<string>();
        try
        {
            log("Odtwarzam bazę danych…");
            var (wiped, wipeOut) = await Shell.RunAsync(
                $"{pg.Psql} -d {pg.Database} -q -v ON_ERROR_STOP=1 -c 'DROP SCHEMA IF EXISTS public CASCADE' -c 'CREATE SCHEMA public'",
                TimeSpan.FromMinutes(5));
            if (!wiped) return (false, "Nie udało się wyczyścić bazy: " + FirstLine(wipeOut));
            var (_, restoreOut) = await Shell.RunAsync(
                $"set -o pipefail; {DbStream(backupPath)} | gunzip -c | {pg.Psql} -d {pg.Database} -q -o /dev/null", TimeSpan.FromMinutes(60));
            var errors = restoreOut.Split('\n').Where(l => l.Contains("ERROR:", StringComparison.Ordinal)).ToList();
            if (errors.Count > 0) warnings.Add($"{errors.Count} błędów przy odtwarzaniu bazy, np. {errors[0].Trim()}");

            if (IsBundle(backupPath) && (await Shell.RunAsync($"tar -tf {Shell.Quote(backupPath)} files.tar.gz", TimeSpan.FromMinutes(2))).Success)
            {
                log("Odtwarzam pliki (logo, zdjęcia, dokumenty)…");
                var mount = await BrandingMountAsync(target);
                try { await docker.EnsureImagePulledAsync(HelperImage); } catch { }
                var (filesOk, filesOut) = await Shell.RunAsync(
                    $"set -o pipefail; tar -xOf {Shell.Quote(backupPath)} files.tar.gz | docker run --rm -i -v {Shell.Quote(mount + ":/data")} --entrypoint sh {HelperImage} -c 'find /data -mindepth 1 -delete && tar xzf - -C /data'",
                    TimeSpan.FromMinutes(30));
                if (!filesOk) warnings.Add("Pliki nie zostały odtworzone: " + FirstLine(filesOut));
            }
            else log("Ta kopia zawiera tylko bazę — pliki zostają bez zmian.");
        }
        finally
        {
            log($"Uruchamiam aplikację {web}…");
            try { await docker.StartContainerAsync(web); }
            catch (Exception ex) { warnings.Add($"Nie udało się uruchomić {web}: {ex.Message}"); }
        }

        return warnings.Count == 0 ? (true, "Odtworzono bazę i pliki.") : (false, string.Join(" ", warnings));
    }

    private static async Task<bool> WaitForDbAsync(PgAccess pg, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if ((await Shell.RunAsync($"{pg.Psql} -d postgres -Atq -c 'select 1'", TimeSpan.FromSeconds(15), pg.Env)).Success) return true;
            await Task.Delay(3000);
        }
        return false;
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

        var src = DbStream(entry.FilePath);
        var (gzOk, gzOut) = await Shell.RunAsync($"set -o pipefail; {src} | gzip -t", TimeSpan.FromMinutes(10));
        if (!gzOk) return (false, "Plik kopii jest uszkodzony: " + FirstLine(gzOut));

        var filesInfo = "";
        if (IsBundle(entry.FilePath) && entry.Slug != "portal")
        {
            var (filesOk, filesOut) = await Shell.RunAsync(
                $"set -o pipefail; tar -xOf {Shell.Quote(entry.FilePath)} files.tar.gz | tar -tzf - | grep -vc '/$'", TimeSpan.FromMinutes(10));
            var count = filesOut.Split('\n', StringSplitOptions.TrimEntries).FirstOrDefault(l => int.TryParse(l, out _));
            // grep -c zwraca 1, gdy nie znalazł żadnego pliku — pusty katalog trenera to nie błąd.
            if (count is null && !filesOk) return (false, "Archiwum plików trenera jest uszkodzone: " + FirstLine(filesOut));
            filesInfo = $" · pliki: {count ?? "0"}";
        }

        var tmp = $"pts_verify_{entry.Id}";
        var drop = $"{pg.Psql} -d postgres -q -c 'DROP DATABASE IF EXISTS {tmp} WITH (FORCE)'";
        try
        {
            var (created, createOut) = await Shell.RunAsync(
                $"{drop} && {pg.Psql} -d postgres -q -v ON_ERROR_STOP=1 -c 'CREATE DATABASE {tmp}'", TimeSpan.FromMinutes(2), pg.Env);
            if (!created) return (false, "Nie udało się utworzyć bazy testowej: " + FirstLine(createOut));

            var (_, restoreOut) = await Shell.RunAsync(
                $"set -o pipefail; {src} | gunzip -c | {pg.Psql} -d {tmp} -q -o /dev/null", TimeSpan.FromMinutes(60), pg.Env);
            var errors = restoreOut.Split('\n').Where(l => l.Contains("ERROR:", StringComparison.Ordinal)).ToList();

            var sql = Shell.Quote("select count(*) from information_schema.tables where table_schema = current_schema()");
            var sqlMig = Shell.Quote("select count(*) || ' ' || coalesce(max(\"MigrationId\"), '-') from \"__EFMigrationsHistory\"");
            var (queried, queryOut) = await Shell.RunAsync($"{pg.Psql} -d {tmp} -At -c {sql} -c {sqlMig}", TimeSpan.FromMinutes(2), pg.Env);
            var lines = queryOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!queried || lines.Length < 2 || !int.TryParse(lines[0], out var tables))
                return (false, "Kopia odtworzyła się bez tabel aplikacji. " + (errors.Count > 0 ? FirstLine(errors[0]) : FirstLine(queryOut)));

            var mig = lines[1].Split(' ', 2);
            var migrations = int.TryParse(mig[0], out var m) ? m : 0;
            var info = $"tabele: {tables} · migracje: {migrations} · ostatnia: {(mig.Length > 1 ? mig[1] : "-")}{filesInfo}";
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

    public static string FirstLine(string? s) =>
        (s ?? "").Replace("[stderr]", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

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

/// <summary>Opis kopii zapisany w pakiecie — da się z niego odczytać, czego i skąd jest kopia, bez Portalu.</summary>
public sealed class BackupManifest
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Format { get; set; } = "ptscheduler-backup";
    public int Version { get; set; } = 1;
    /// <summary>„tenant” albo „portal”.</summary>
    public string Kind { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? Domain { get; set; }
    public int? Port { get; set; }
    public string? PlanId { get; set; }
    public string? OwnerEmail { get; set; }
    public string? AppCommit { get; set; }
    public string CreatedBy { get; set; } = "portal";
    public DateTime CreatedAtUtc { get; set; }
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
