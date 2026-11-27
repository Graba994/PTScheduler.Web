using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docker.DotNet.Models;

namespace PTScheduler.Guardian.Services;

/// <summary>
/// Kopie bazy Portalu po stronie Guardiana. Guardian działa niezależnie od Portalu, więc:
/// robi zapasową kopię, gdy Portal przestaje robić własne, i przywraca bazę Portalu z kopii,
/// gdy Portal nie może tego zrobić sam (bo leży albo jego baza jest uszkodzona).
/// Format kopii jest ten sam co w Portalu: <c>.tar</c> z <c>manifest.json</c>, <c>database.sql.gz</c> i <c>env/</c>.
/// </summary>
public sealed partial class UpgradeOrchestrator
{
    private const int EncryptIterations = 200_000;
    private readonly SemaphoreSlim _backupLock = new(1, 1);

    public string BackupDir => Cfg("GUARDIAN_BACKUP_DIR", null, "/opt/ptscheduler/backups");
    /// <summary>Kontener bazy Portalu: z GUARDIAN_PORTAL_DB_CONTAINER, a bez tego znaleziony (zob. <see cref="ResolvePortalDbContainerAsync"/>).</summary>
    public string PortalDbContainer => _portalDbResolved ?? Cfg("GUARDIAN_PORTAL_DB_CONTAINER", null, "ptportal-db");
    private string? _portalDbResolved;
    private string PortalBackupDir => Path.Combine(BackupDir, "portal");

    public sealed record PortalBackupFile(string Name, long Size, DateTime CreatedAt, string Source, bool Encrypted, bool HasEnv);

    public List<PortalBackupFile> ListPortalBackups()
    {
        if (!Directory.Exists(PortalBackupDir)) return [];
        return Directory.EnumerateFiles(PortalBackupDir)
            .Select(f => new FileInfo(f))
            .Where(f => !f.Name.StartsWith('.') && IsBackupName(f.Name))
            .Select(f => new PortalBackupFile(
                f.Name, f.Length, StampFromName(f.Name) ?? f.LastWriteTimeUtc,
                f.Name.Contains("_guardian", StringComparison.Ordinal) ? "guardian"
                    : f.Name.Contains("_przed-", StringComparison.Ordinal) ? "przed-przywróceniem"
                    : f.Name.Contains("_wgrana", StringComparison.Ordinal) ? "wgrana" : "portal",
                f.Name.EndsWith(".enc", StringComparison.Ordinal),
                f.Name.Contains(".tar", StringComparison.Ordinal)))
            .OrderByDescending(f => f.CreatedAt)
            .ToList();
    }

    /// <summary>Kopia bazy Portalu (i konfiguracji platformy) zrobiona przez Guardiana.</summary>
    public async Task<(bool Ok, string Message, string? File)> CreatePortalBackupAsync(string suffix, string reason, Action<string>? log = null)
    {
        if (!await _backupLock.WaitAsync(TimeSpan.FromSeconds(5))) return (false, "Trwa już inna kopia.", null);
        var tmp = "";
        try
        {
            var creds = await PortalDbCredsAsync();
            if (creds is null) return (false, $"Nie ma kontenera bazy Portalu '{PortalDbContainer}' albo nie działa.", null);
            var (user, db) = creds.Value;

            Directory.CreateDirectory(PortalBackupDir);
            tmp = Path.Combine(PortalBackupDir, $".tmp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tmp, "env"));
            log?.Invoke("Zrzucam bazę Portalu…");
            var (dumped, dumpOut) = await Bash(
                $"set -o pipefail; docker exec {Q(PortalDbContainer)} pg_dump -U {Q(user)} -d {Q(db)} --no-owner --no-privileges | gzip -c > {Q(Path.Combine(tmp, "database.sql.gz"))}", 30);
            if (!dumped) return (false, "pg_dump nie powiódł się: " + FirstLine(dumpOut), null);

            foreach (var name in new[] { ".env.prod", ".env" })
            {
                var path = Path.Combine(_repoDir, name);
                if (File.Exists(path)) File.Copy(path, Path.Combine(tmp, "env", name.TrimStart('.')));
            }
            try { await File.WriteAllTextAsync(Path.Combine(tmp, "env", "containers.env"), await BuildPlatformEnvAsync()); }
            catch (Exception ex) { _logger.LogWarning(ex, "Nie odczytano konfiguracji kontenerów do kopii."); }

            var manifest = new
            {
                format = "ptscheduler-backup", version = 1, kind = "portal", slug = "portal",
                createdBy = "guardian", reason, createdAtUtc = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(Path.Combine(tmp, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var file = $"portal_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}.tar";
            var (packed, packOut) = await Bash($"tar cf {Q(Path.Combine(PortalBackupDir, file))} -C {Q(tmp)} manifest.json database.sql.gz env", 10);
            if (!packed) return (false, "Nie udało się złożyć kopii: " + FirstLine(packOut), null);
            _logger.LogInformation("Kopia bazy Portalu: {File} ({Reason})", file, reason);
            return (true, $"Zapisano {file}.", file);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kopia bazy Portalu nie powiodła się.");
            return (false, ex.Message, null);
        }
        finally
        {
            if (tmp.Length > 0) try { Directory.Delete(tmp, true); } catch { /* sprzątnie następna kopia */ }
            _backupLock.Release();
        }
    }

    /// <summary>Usuwa stare kopie zrobione przez Guardiana (zostaje 7 najnowszych) — Portal sprząta resztę.</summary>
    public void PruneGuardianBackups()
    {
        foreach (var f in ListPortalBackups().Where(f => f.Source == "guardian").Skip(7))
            try { File.Delete(Path.Combine(PortalBackupDir, f.Name)); } catch { /* następnym razem */ }
    }

    /// <summary>Zapisuje wgrany plik kopii (np. pobrany z Dysku Google) w katalogu kopii Portalu.</summary>
    public async Task<(bool Ok, string Message)> SaveUploadAsync(Stream body, string fileName, CancellationToken ct)
    {
        var name = Path.GetFileName(fileName ?? "");
        if (!IsBackupName(name) || name.StartsWith('.') || name.Any(char.IsControl))
            return (false, "Oczekuję pliku kopii Portalu: .tar, .tar.enc, .sql.gz albo .sql.gz.enc.");
        Directory.CreateDirectory(PortalBackupDir);
        var stamp = StampFromName(name) is { } t ? t.ToString("yyyyMMdd_HHmmss") : DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var ext = name.EndsWith(".tar.enc") ? ".tar.enc" : name.EndsWith(".tar") ? ".tar" : name.EndsWith(".sql.gz.enc") ? ".sql.gz.enc" : ".sql.gz";
        var target = Path.Combine(PortalBackupDir, $"portal_{stamp}_wgrana{ext}");
        var partial = target + ".part";
        await using (var fs = File.Create(partial)) await body.CopyToAsync(fs, ct);
        File.Move(partial, target, overwrite: true);
        return (true, $"Wgrano jako {Path.GetFileName(target)}.");
    }

    // ── Przywracanie ────────────────────────────────────────────────

    public async Task<(bool Started, string JobId, string? Error)> StartPortalRestoreAsync(string fileName, string? password, bool skipSafetyBackup, string? requestedBy)
    {
        var name = Path.GetFileName(fileName ?? "");
        var path = Path.Combine(PortalBackupDir, name);
        if (!IsBackupName(name) || !File.Exists(path)) return (false, "", "Nie ma takiego pliku kopii.");
        if (name.EndsWith(".enc", StringComparison.Ordinal) && string.IsNullOrEmpty(password))
            return (false, "", "Ta kopia jest zaszyfrowana — podaj hasło szyfrowania kopii.");
        if (!await _semaphore.WaitAsync(StartWait)) return (false, "", BusyMessage());

        var job = NewJob("restore", UpgradeTarget.PortalRestore, requestedBy);
        _ = RunInBackground(job, j => ExecutePortalRestoreAsync(j, path, password, skipSafetyBackup));
        return (true, job.Id, null);
    }

    private async Task ExecutePortalRestoreAsync(UpgradeJob job, string path, string? password, bool skipSafety)
    {
        SetStage(job, UpgradeStage.Testing);
        var work = path;
        var decrypted = "";
        try
        {
            if (path.EndsWith(".enc", StringComparison.Ordinal))
            {
                Log(job, "info", "Testing", "Odszyfrowuję kopię…");
                decrypted = Path.Combine(PortalBackupDir, $".restore-{Guid.NewGuid():N}{(path.Contains(".tar") ? ".tar" : ".sql.gz")}");
                try
                {
                    await using var input = File.OpenRead(path);
                    await using var output = File.Create(decrypted);
                    await DecryptAsync(input, output, password!);
                }
                catch (CryptographicException)
                {
                    Fail(job, "Testing", "Złe hasło szyfrowania — kopii nie da się odszyfrować.");
                    return;
                }
                work = decrypted;
            }

            var src = work.EndsWith(".tar", StringComparison.Ordinal) ? $"tar -xOf {Q(work)} database.sql.gz" : $"cat {Q(work)}";
            Log(job, "info", "Testing", "Sprawdzam, czy plik kopii jest cały…");
            var (intact, intactOut) = await Bash($"set -o pipefail; {src} | gzip -t", 10);
            if (!intact) { Fail(job, "Testing", "Plik kopii jest uszkodzony: " + FirstLine(intactOut)); return; }

            var creds = await PortalDbCredsAsync(startIfStopped: true);
            if (creds is null) { Fail(job, "Testing", $"Nie ma kontenera bazy Portalu '{PortalDbContainer}' — uruchom najpierw: docker compose up -d portal-db."); return; }
            var (user, db) = creds.Value;
            if (!SafeIdent().IsMatch(user) || !SafeIdent().IsMatch(db)) { Fail(job, "Testing", "Nietypowa nazwa bazy albo użytkownika Portalu — przywróć ręcznie."); return; }

            SetStage(job, UpgradeStage.Swapping);
            if (!skipSafety)
            {
                Log(job, "info", "Swapping", "Zapisuję kopię obecnej bazy Portalu (żeby dało się cofnąć)…");
                var (safeOk, safeMsg, _) = await CreatePortalBackupAsync("przed-przywroceniem", "przed przywróceniem kopii");
                if (!safeOk)
                {
                    Fail(job, "Swapping", $"Nie udało się zrobić kopii obecnej bazy ({safeMsg}). Jeśli baza jest uszkodzona, zaznacz „Pomiń kopię obecnej bazy” i spróbuj ponownie.");
                    return;
                }
                Log(job, "success", "Swapping", safeMsg);
            }
            else Log(job, "warn", "Swapping", "Pomijam kopię obecnej bazy (na życzenie).");

            var portal = await InspectOrNull(_portalContainer);
            if (portal is not null)
            {
                Log(job, "info", "Swapping", "Zatrzymuję Portal…");
                try { await _docker.Containers.StopContainerAsync(portal.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 20 }); } catch { /* już stoi */ }
            }

            Log(job, "info", "Swapping", "Czyszczę bazę Portalu i wgrywam kopię…");
            var (recreated, recreateOut) = await Bash(
                $"docker exec {Q(PortalDbContainer)} psql -U {user} -d postgres -q -v ON_ERROR_STOP=1 -c 'DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)' -c 'CREATE DATABASE \"{db}\" OWNER \"{user}\"'", 5);
            if (!recreated)
            {
                if (portal is not null) await SafeStart(portal.ID);
                Fail(job, "Swapping", "Nie udało się utworzyć bazy od nowa: " + FirstLine(recreateOut));
                return;
            }
            var (_, loadOut) = await Bash($"set -o pipefail; {src} | gunzip -c | docker exec -i {Q(PortalDbContainer)} psql -U {user} -d {db} -q -o /dev/null", 60);
            var errors = loadOut.Split('\n').Where(l => l.Contains("ERROR:", StringComparison.Ordinal)).ToList();
            if (errors.Count > 0) Log(job, "warn", "Swapping", $"{errors.Count} błędów przy wgrywaniu, np. {errors[0].Trim()}");
            else Log(job, "success", "Swapping", "Baza Portalu wgrana.");

            SetStage(job, UpgradeStage.Verifying);
            if (portal is null)
            {
                job.Status = UpgradeStatus.PartialSuccess;
                Log(job, "warn", "Done", $"Nie ma kontenera '{_portalContainer}'. Uruchom Portal: docker compose -f docker-compose.prod.yml --env-file .env.prod up -d portal");
                job.Stage = UpgradeStage.Done;
                return;
            }
            Log(job, "info", "Verifying", "Uruchamiam Portal…");
            await SafeStart(portal.ID);
            var healthy = await WaitForPortalHealthAsync(TimeSpan.FromSeconds(150));
            job.Stage = UpgradeStage.Done;
            if (healthy)
            {
                job.Status = errors.Count > 0 ? UpgradeStatus.PartialSuccess : UpgradeStatus.Success;
                Log(job, "success", "Done", "Portal działa na przywróconej bazie. Sprawdź w Portalu → Kopie zapasowe, czy trenerzy działają.");
            }
            else
            {
                var logs = await GetContainerLogs(portal.ID, 40);
                Fail(job, "Done", $"Portal nie odpowiada po przywróceniu. Ostatnie logi:\n{logs}");
            }
        }
        finally
        {
            if (decrypted.Length > 0) try { File.Delete(decrypted); } catch { /* plik tymczasowy */ }
        }
    }

    private async Task<bool> WaitForPortalHealthAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                using var resp = await _healthHttp.GetAsync($"{_portalUrl}/health");
                if (resp.IsSuccessStatusCode) return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* jeszcze wstaje */ }
            await Task.Delay(3000);
        }
        return false;
    }

    // ── Pomocnicze ──────────────────────────────────────────────────

    private async Task<ContainerInspectResponse?> InspectOrNull(string name)
    {
        try { return await _docker.Containers.InspectContainerAsync(name); }
        catch { return null; }
    }

    /// <summary>
    /// Nazwa kontenera bazy Portalu bywa różna (compose, Portainer, Unraid). Bez GUARDIAN_PORTAL_DB_CONTAINER szukamy:
    /// „ptportal-db”, potem host z connection stringa Portalu jako nazwa kontenera albo usługa compose,
    /// na końcu kontener postgres z bazą o tej nazwie (POSTGRES_DB).
    /// </summary>
    private async Task ResolvePortalDbContainerAsync()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GUARDIAN_PORTAL_DB_CONTAINER"))) return;
        if (_portalDbResolved is not null && await InspectOrNull(_portalDbResolved) is not null) return;
        if (await InspectOrNull("ptportal-db") is not null) { _portalDbResolved = "ptportal-db"; return; }

        var conn = (await PortalContainerEnvAsync()).GetValueOrDefault("ConnectionStrings__DefaultConnection") ?? "";
        string Part(string key) => conn.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
            .FirstOrDefault(p => p.Length == 2 && p[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))?[1].Trim() ?? "";
        var host = Part("Host");
        var database = Part("Database");
        if (host.Length > 0 && await InspectOrNull(host) is not null) { _portalDbResolved = host; return; }
        try
        {
            var all = await _docker.Containers.ListContainersAsync(new ContainersListParameters { All = true });
            string Name(ContainerListResponse c) => c.Names?.FirstOrDefault()?.TrimStart('/') ?? c.ID;
            var bySvc = all.FirstOrDefault(c => host.Length > 0 && c.Labels is { } l && l.TryGetValue("com.docker.compose.service", out var svc) && svc == host);
            if (bySvc is not null) { _portalDbResolved = Name(bySvc); return; }
            foreach (var c in all.Where(c => (c.Image ?? "").Contains("postgres", StringComparison.OrdinalIgnoreCase)))
            {
                var info = await InspectOrNull(c.ID);
                if (database.Length > 0 && (info?.Config?.Env ?? []).Contains($"POSTGRES_DB={database}")) { _portalDbResolved = Name(c); return; }
            }
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Nie udało się wyszukać kontenera bazy Portalu."); }
    }

    /// <summary>Użytkownik i nazwa bazy Portalu z konfiguracji kontenera bazy (POSTGRES_USER / POSTGRES_DB).</summary>
    public async Task<(string User, string Db)?> PortalDbCredsAsync(bool startIfStopped = false)
    {
        await ResolvePortalDbContainerAsync();
        var info = await InspectOrNull(PortalDbContainer);
        if (info is null) return null;
        if (!info.State.Running)
        {
            if (!startIfStopped) return null;
            await SafeStart(info.ID);
            for (var i = 0; i < 30 && !(await Bash($"docker exec {Q(PortalDbContainer)} pg_isready", 1)).Ok; i++) await Task.Delay(2000);
        }
        var env = (info.Config.Env ?? []).Select(e => e.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        var user = env.GetValueOrDefault("POSTGRES_USER") is { Length: > 0 } u ? u : "postgres";
        var db = env.GetValueOrDefault("POSTGRES_DB") is { Length: > 0 } d ? d : user;
        return (user, db);
    }

    /// <summary>Odczyt ustawień z bazy Portalu (np. SMTP) — działa, dopóki działa kontener bazy.</summary>
    public async Task<Dictionary<string, string>> ReadPortalSettingsAsync(params string[] keys)
    {
        var result = new Dictionary<string, string>();
        var creds = await PortalDbCredsAsync();
        if (creds is null || keys.Any(k => !SafeIdent().IsMatch(k))) return result;
        var list = string.Join(",", keys.Select(k => $"'{k}'"));
        var (ok, output) = await Bash(
            $"docker exec {Q(PortalDbContainer)} psql -U {Q(creds.Value.User)} -d {Q(creds.Value.Db)} -At -F '\t' -c {Q($"select \"Key\", \"Value\" from \"SiteSettings\" where \"Key\" in ({list})")}", 1);
        if (!ok) return result;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2) result[parts[0]] = parts[1];
        }
        return result;
    }

    public async Task<Dictionary<string, string>> PortalContainerEnvAsync()
    {
        var info = await InspectOrNull(_portalContainer);
        return (info?.Config?.Env ?? []).Select(e => e.Split('=', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
    }

    /// <summary>To samo co PlatformEnv w Portalu: .env.prod odtworzony z działających kontenerów.</summary>
    private async Task<string> BuildPlatformEnvAsync()
    {
        (string Container, string From, string To)[] map =
        [
            (PortalDbContainer, "POSTGRES_DB", "PORTAL_DB_NAME"), (PortalDbContainer, "POSTGRES_USER", "PORTAL_DB_USER"),
            (PortalDbContainer, "POSTGRES_PASSWORD", "PORTAL_DB_PASSWORD"),
            (_portalContainer, "Portal__TenantInternalSecret", "TENANT_INTERNAL_SECRET"), (_portalContainer, "Portal__ForwardHost", "FORWARD_HOST"),
            (_portalContainer, "Portal__PublicUrl", "PORTAL_PUBLIC_URL"), (_portalContainer, "Portal__ContactEmail", "CONTACT_EMAIL"),
            (_portalContainer, "Portal__SiteName", "SITE_NAME"), (_portalContainer, "Portal__NpmUrl", "NPM_URL"),
            (_portalContainer, "Portal__NpmUser", "NPM_USER"), (_portalContainer, "Portal__NpmPassword", "NPM_PASSWORD"),
            (_portalContainer, "Stripe__SecretKey", "STRIPE_SECRET_KEY"), (_portalContainer, "Stripe__WebhookSecret", "STRIPE_WEBHOOK_SECRET"),
            (_portalContainer, "Stripe__PublishableKey", "STRIPE_PUBLISHABLE_KEY"), (_portalContainer, "Email__SmtpHost", "SMTP_HOST"),
            (_portalContainer, "Email__SmtpPort", "SMTP_PORT"), (_portalContainer, "Email__SmtpUser", "SMTP_USER"),
            (_portalContainer, "Email__SmtpPassword", "SMTP_PASSWORD"), (_portalContainer, "Email__FromAddress", "SMTP_FROM"),
        ];
        var sb = new StringBuilder();
        sb.AppendLine("# Konfiguracja platformy odczytana z działających kontenerów przez Guardiana.");
        sb.AppendLine($"# Utworzono {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. Użyj jako .env.prod na nowym serwerze.");
        var cache = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (container, from, to) in map)
        {
            if (!cache.TryGetValue(container, out var env))
            {
                var info = await InspectOrNull(container);
                cache[container] = env = (info?.Config?.Env ?? []).Select(e => e.Split('=', 2)).Where(p => p.Length == 2)
                    .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
            }
            if (env.GetValueOrDefault(from) is { Length: > 0 } value) sb.AppendLine($"{to}={EnvValue(value)}");
        }
        if (_guardianSecret.Length > 0) sb.AppendLine($"GUARDIAN_SECRET={EnvValue(_guardianSecret)}");
        sb.AppendLine($"BUILD_BRANCH={_branch}");
        return sb.ToString();
    }

    private static string EnvValue(string v) =>
        v.IndexOfAny([' ', '#', '"', '\'', '$', '\\']) < 0 ? v
        : !v.Contains('\'') ? "'" + v + "'"
        : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Odszyfrowanie formatu openssl enc -aes-256-cbc -pbkdf2 -iter 200000 (tak szyfruje Portal).</summary>
    private static async Task DecryptAsync(Stream input, Stream output, string password)
    {
        var header = new byte[16];
        await input.ReadExactlyAsync(header);
        if (!header.AsSpan(0, 8).SequenceEqual("Salted__"u8)) throw new CryptographicException("To nie jest zaszyfrowana kopia.");
        var keyIv = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), header[8..], EncryptIterations, HashAlgorithmName.SHA256, 48);
        using var aes = Aes.Create();
        aes.Key = keyIv[..32];
        aes.IV = keyIv[32..];
        await using var cs = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
        await cs.CopyToAsync(output);
    }

    private static Task<(bool Ok, string Output)> Bash(string script, int timeoutMin) => Cli("bash", ["-c", script], timeoutMin: timeoutMin);

    private static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static string FirstLine(string? s) =>
        (s ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static bool IsBackupName(string name) =>
        name.EndsWith(".tar", StringComparison.Ordinal) || name.EndsWith(".tar.enc", StringComparison.Ordinal)
        || name.EndsWith(".sql.gz", StringComparison.Ordinal) || name.EndsWith(".sql.gz.enc", StringComparison.Ordinal);

    private static DateTime? StampFromName(string name)
    {
        var m = StampRegex().Match(name);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : null;
    }

    [GeneratedRegex(@"_(\d{8}_\d{6})")]
    private static partial Regex StampRegex();
    [GeneratedRegex("^[A-Za-z0-9_]{1,63}$")]
    private static partial Regex SafeIdent();
}
