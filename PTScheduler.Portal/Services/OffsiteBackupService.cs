using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using Renci.SshNet;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Kopia poza serwerem: po nocnym backupie pliki trafiają na inny serwer (SFTP) albo na Google Drive.
/// Z hasłem szyfrowania wysyłamy plik <c>.enc</c> zgodny z <c>openssl enc -aes-256-cbc -pbkdf2</c>,
/// więc da się go odszyfrować bez Portalu.
/// </summary>
public class OffsiteBackupService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    GoogleOAuthBroker google,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    ILogger<OffsiteBackupService> logger)
{
    public const string TargetOff = "off", TargetSftp = "sftp", TargetGdrive = "gdrive";
    public const string GdriveStatePrefix = "drive.";
    public const int EncryptIterations = 200_000;
    private const string DriveScopes = "openid email https://www.googleapis.com/auth/drive.file";
    private const string DriveFolderName = "PTScheduler — kopie zapasowe";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static (string Token, DateTime ExpiresUtc)? _driveAccess;
    private static readonly ConcurrentDictionary<int, byte> InFlight = new();

    private IDataProtector Secrets => dataProtection.CreateProtector("backup-offsite-secrets");
    private ITimeLimitedDataProtector StateProtector =>
        dataProtection.CreateProtector("backup-gdrive-oauth-state").ToTimeLimitedDataProtector();

    public static string TargetLabel(string target) => target switch
    {
        TargetSftp => "inny serwer (SFTP)",
        TargetGdrive => "Google Drive",
        _ => "wyłączona"
    };

    // ── Ustawienia i sekrety ────────────────────────────────────────────────

    public async Task<string> GetTargetAsync()
    {
        var t = await settings.GetAsync(SiteSettingsService.Keys.BackupOffsiteTarget);
        return t is TargetSftp or TargetGdrive ? t : TargetOff;
    }

    public async Task<string?> GetSecretAsync(string key)
    {
        var raw = await settings.GetAsync(key);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); }
        catch (CryptographicException)
        {
            logger.LogWarning("Nie da się odszyfrować ustawienia {Key} — klucze Data Protection się zmieniły.", key);
            return null;
        }
    }

    public Task SetSecretAsync(string key, string? value) =>
        settings.SetAsync(key, string.IsNullOrEmpty(value) ? "" : Secrets.Protect(value));

    public async Task<bool> HasSecretAsync(string key) => !string.IsNullOrEmpty(await settings.GetAsync(key));

    // ── Wysyłka ─────────────────────────────────────────────────────────────

    /// <summary>Wysyła jedną kopię. Zwraca wpis z wynikiem (OffsiteOk / OffsiteInfo).</summary>
    public async Task<BackupEntry?> UploadAsync(int backupId, CancellationToken ct = default)
    {
        if (!InFlight.TryAdd(backupId, 0)) return null;
        try
        {
            await using var db = dbFactory.CreateDbContext();
            var entry = await db.BackupEntries.FindAsync([backupId], ct);
            if (entry is null || entry.Status != BackupStatus.Completed) return entry;

            var target = await GetTargetAsync();
            if (target == TargetOff) return entry;

            var temp = "";
            try
            {
                if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
                    throw new InvalidOperationException("Plik kopii nie istnieje na dysku.");

                var path = entry.FilePath;
                var name = Path.GetFileName(path);
                if (await GetSecretAsync(SiteSettingsService.Keys.BackupOffsitePassword) is { Length: > 0 } password)
                {
                    temp = path + ".enc.tmp";
                    await using (var input = File.OpenRead(path))
                    await using (var output = File.Create(temp))
                        await EncryptAsync(input, output, password, ct);
                    path = temp;
                    name += ".enc";
                }

                var where = target == TargetSftp
                    ? await UploadSftpAsync(path, entry.Slug, name, ct)
                    : await UploadDriveAsync(path, name, ct);
                entry.OffsiteOk = true;
                entry.OffsiteInfo = $"{TargetLabel(target)}: {where}";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                entry.OffsiteOk = false;
                entry.OffsiteInfo = Trim($"{TargetLabel(target)}: {ex.Message}", 1000);
                logger.LogWarning(ex, "Wysyłka kopii {Id} poza serwer nie powiodła się.", backupId);
            }
            finally
            {
                if (temp.Length > 0) try { File.Delete(temp); } catch { }
            }

            entry.OffsiteAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            return entry;
        }
        finally { InFlight.TryRemove(backupId, out _); }
    }

    /// <summary>Wysyła udane kopie z ostatnich 2 dni, których jeszcze nie ma poza serwerem.</summary>
    public async Task<List<BackupEntry>> UploadPendingAsync(CancellationToken ct = default)
    {
        if (await GetTargetAsync() == TargetOff) return [];
        await using var db = dbFactory.CreateDbContext();
        var since = DateTime.UtcNow.AddDays(-2);
        var ids = await db.BackupEntries.AsNoTracking()
            .Where(e => e.Status == BackupStatus.Completed && e.CreatedAt >= since && e.OffsiteOk != true)
            .OrderBy(e => e.CreatedAt).Select(e => e.Id).ToListAsync(ct);
        var results = new List<BackupEntry>();
        foreach (var id in ids)
            if (await UploadAsync(id, ct) is { } e) results.Add(e);
        return results;
    }

    /// <summary>
    /// Usuwa poza serwerem kopie starsze niż retencja; po 2 dniach zostaje jedna kopia dziennie.
    /// Najnowsza kopia każdej instancji zostaje zawsze. Zwraca liczbę usuniętych plików.
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var target = await GetTargetAsync();
        if (target == TargetOff) return 0;
        var s = await settings.GetAsync(SiteSettingsService.Keys.BackupOffsiteRetentionDays);
        if (!int.TryParse(s, out var days) || days <= 0) days = 30;
        var now = DateTime.UtcNow;
        var files = await ListRemoteAsync(ct);
        var keep = files.GroupBy(f => f.Slug).Select(g => g.MaxBy(f => f.CreatedUtc)!.Ref).ToHashSet();
        var daily = files.GroupBy(f => (f.Slug, f.CreatedUtc.Date)).Select(g => g.MaxBy(f => f.CreatedUtc)!.Ref).ToHashSet();
        var doomed = files.Where(f => !keep.Contains(f.Ref)
            && (f.CreatedUtc < now.AddDays(-days) || (f.CreatedUtc < now.AddDays(-2) && !daily.Contains(f.Ref)))).ToList();
        if (doomed.Count == 0) return 0;

        var removed = 0;
        if (target == TargetSftp)
        {
            using var client = await ConnectSftpAsync(ct);
            foreach (var f in doomed)
            {
                try { client.DeleteFile(f.Ref); removed++; }
                catch (Exception ex) { logger.LogWarning(ex, "Nie usunięto {File} z serwera SFTP.", f.Ref); }
            }
        }
        else
        {
            var (token, _) = await DriveAccessAsync(ct);
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            foreach (var f in doomed)
                if ((await http.DeleteAsync($"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(f.Ref)}", ct)).IsSuccessStatusCode) removed++;
        }
        return removed;
    }

    /// <summary>Kopia leżąca poza serwerem. <c>Ref</c> — ścieżka na serwerze SFTP albo identyfikator pliku na Dysku.</summary>
    public sealed record RemoteBackup(string Slug, string Name, string Ref, DateTime CreatedUtc, long Size);

    /// <summary>Wszystkie kopie poza serwerem (pliki o nazwach <c>slug_yyyyMMdd_HHmmss…</c>).</summary>
    public async Task<List<RemoteBackup>> ListRemoteAsync(CancellationToken ct = default)
    {
        var target = await GetTargetAsync();
        var result = new List<RemoteBackup>();
        if (target == TargetSftp)
        {
            using var client = await ConnectSftpAsync(ct);
            var root = await SftpDirAsync();
            if (!client.Exists(root)) return result;
            foreach (var dir in client.ListDirectory(root).Where(d => d.IsDirectory && d.Name is not "." and not ".." and not "test"))
                foreach (var f in client.ListDirectory(dir.FullName).Where(f => f.IsRegularFile && IsBackupName(f.Name)))
                    result.Add(new RemoteBackup(dir.Name, f.Name, f.FullName, BackupService.FileStamp(f.Name) ?? f.LastWriteTimeUtc, f.Length));
        }
        else if (target == TargetGdrive)
        {
            var (token, _) = await DriveAccessAsync(ct);
            var folder = await EnsureDriveFolderAsync(token, ct);
            using var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var q = $"'{folder}' in parents and trashed = false";
            string? page = null;
            do
            {
                var url = $"https://www.googleapis.com/drive/v3/files?q={Uri.EscapeDataString(q)}&fields=nextPageToken,files(id,name,createdTime,size)&pageSize=500"
                    + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
                var list = await http.GetAsync(url, ct);
                await EnsureDriveOk(list, "odczytać listy plików na Dysku", ct);
                using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct));
                foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
                {
                    var name = f.GetProperty("name").GetString() ?? "";
                    var slug = SlugFromName(name);
                    if (slug is null || !IsBackupName(name)) continue;
                    var created = f.TryGetProperty("createdTime", out var c) && c.TryGetDateTime(out var dt) ? dt.ToUniversalTime() : DateTime.MinValue;
                    var size = f.TryGetProperty("size", out var sz) && long.TryParse(sz.GetString(), out var n) ? n : 0;
                    result.Add(new RemoteBackup(slug, name, f.GetProperty("id").GetString()!, BackupService.FileStamp(name) ?? created, size));
                }
                page = doc.RootElement.TryGetProperty("nextPageToken", out var np) ? np.GetString() : null;
            } while (page is not null);
        }
        return result;
    }

    /// <summary>Najnowsza kopia instancji poza serwerem (null, gdy brak albo kopia poza serwerem wyłączona).</summary>
    public async Task<RemoteBackup?> FindLatestAsync(string slug, CancellationToken ct = default) =>
        (await ListRemoteAsync(ct)).Where(f => f.Slug == slug).MaxBy(f => f.CreatedUtc);

    /// <summary>Pobiera kopię do katalogu i odszyfrowuje ją hasłem z ustawień. Zwraca ścieżkę gotowego pliku.</summary>
    public async Task<string> DownloadAsync(RemoteBackup remote, string localDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(localDir);
        var encrypted = remote.Name.EndsWith(".enc", StringComparison.Ordinal);
        var finalPath = Path.Combine(localDir, encrypted ? remote.Name[..^4] : remote.Name);
        var partial = Path.Combine(localDir, "." + remote.Name + ".part");
        string? password = null;
        if (encrypted && (password = await GetSecretAsync(SiteSettingsService.Keys.BackupOffsitePassword)) is not { Length: > 0 })
            throw new InvalidOperationException("Kopia jest zaszyfrowana, a w ustawieniach nie ma hasła szyfrowania — wpisz je w „Kopia poza serwerem”.");
        try
        {
            await using (var output = File.Create(partial))
            {
                if (await GetTargetAsync() == TargetSftp)
                {
                    using var client = await ConnectSftpAsync(ct);
                    await Task.Run(() => client.DownloadFile(remote.Ref, output), ct);
                }
                else
                {
                    var (token, _) = await DriveAccessAsync(ct);
                    using var http = httpFactory.CreateClient("backup-offsite");
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(remote.Ref)}?alt=media");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    await EnsureDriveOk(resp, "pobrać pliku z Dysku", ct);
                    await resp.Content.CopyToAsync(output, ct);
                }
            }
            if (encrypted)
            {
                try
                {
                    await using var input = File.OpenRead(partial);
                    await using var output = File.Create(finalPath);
                    await DecryptAsync(input, output, password!, ct);
                }
                catch (CryptographicException)
                {
                    try { File.Delete(finalPath); } catch { }
                    throw new InvalidOperationException("Nie da się odszyfrować kopii — hasło szyfrowania w ustawieniach jest inne niż w chwili wysyłki.");
                }
            }
            else File.Move(partial, finalPath, overwrite: true);
            return finalPath;
        }
        finally { try { File.Delete(partial); } catch { } }
    }

    private static bool IsBackupName(string name) =>
        name.EndsWith(".tar", StringComparison.Ordinal) || name.EndsWith(".tar.enc", StringComparison.Ordinal)
        || name.EndsWith(".sql.gz", StringComparison.Ordinal) || name.EndsWith(".sql.gz.enc", StringComparison.Ordinal);

    private static string? SlugFromName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.+?)_\d{8}_\d{6}");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Próba połączenia + zapis i usunięcie małego pliku — sprawdza też uprawnienia do zapisu.</summary>
    public async Task<(bool Ok, string Message)> TestAsync(CancellationToken ct = default)
    {
        var target = await GetTargetAsync();
        if (target == TargetOff) return (false, "Najpierw wybierz, dokąd wysyłać kopie, i zapisz ustawienia.");
        var probe = Path.Combine(Path.GetTempPath(), $"ptscheduler-test-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(probe, $"Test połączenia z Portalu PTScheduler, {DateTime.UtcNow:u}. Ten plik można usunąć.", ct);
            if (target == TargetSftp)
            {
                using var client = await ConnectSftpAsync(ct);
                var dir = await SftpDirAsync("test");
                EnsureSftpDir(client, dir);
                var remote = $"{dir}/{Path.GetFileName(probe)}";
                await using (var fs = File.OpenRead(probe)) client.UploadFile(fs, remote);
                client.DeleteFile(remote);
                client.DeleteDirectory(dir);
                var fp = await settings.GetAsync(SiteSettingsService.Keys.BackupSftpFingerprint);
                return (true, $"Połączono z {client.ConnectionInfo.Host} i zapisano plik testowy. Odcisk klucza serwera: {fp}");
            }
            var (token, email) = await DriveAccessAsync(ct);
            var folder = await EnsureDriveFolderAsync(token, ct);
            var id = await DriveUploadAsync(token, folder, probe, Path.GetFileName(probe), ct);
            using var http = httpFactory.CreateClient();
            using var del = new HttpRequestMessage(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{id}");
            del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await http.SendAsync(del, ct);
            return (true, $"Połączono z Google Drive{(email is null ? "" : $" ({email})")} i zapisano plik testowy w folderze „{DriveFolderName}”.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
        finally { try { File.Delete(probe); } catch { } }
    }

    // ── Szyfrowanie (format openssl enc -aes-256-cbc -pbkdf2 -iter 200000) ─

    public static async Task EncryptAsync(Stream input, Stream output, string password, CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(8);
        var keyIv = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, EncryptIterations, HashAlgorithmName.SHA256, 48);
        await output.WriteAsync("Salted__"u8.ToArray(), ct);
        await output.WriteAsync(salt, ct);
        using var aes = Aes.Create();
        aes.Key = keyIv[..32];
        aes.IV = keyIv[32..];
        await using var cs = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
        await input.CopyToAsync(cs, ct);
        await cs.FlushFinalBlockAsync(ct);
    }

    public static async Task DecryptAsync(Stream input, Stream output, string password, CancellationToken ct = default)
    {
        var header = new byte[16];
        await input.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 8).SequenceEqual("Salted__"u8)) throw new InvalidDataException("To nie jest zaszyfrowana kopia.");
        var keyIv = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), header[8..], EncryptIterations, HashAlgorithmName.SHA256, 48);
        using var aes = Aes.Create();
        aes.Key = keyIv[..32];
        aes.IV = keyIv[32..];
        await using var cs = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read, leaveOpen: true);
        await cs.CopyToAsync(output, ct);
    }

    // ── SFTP ────────────────────────────────────────────────────────────────

    private async Task<SftpClient> ConnectSftpAsync(CancellationToken ct)
    {
        var s = await settings.GetAllAsync(
            SiteSettingsService.Keys.BackupSftpHost, SiteSettingsService.Keys.BackupSftpPort,
            SiteSettingsService.Keys.BackupSftpUser, SiteSettingsService.Keys.BackupSftpFingerprint);
        var host = s[SiteSettingsService.Keys.BackupSftpHost].Trim();
        var user = s[SiteSettingsService.Keys.BackupSftpUser].Trim();
        if (host.Length == 0 || user.Length == 0) throw new InvalidOperationException("Uzupełnij adres serwera i użytkownika SFTP.");
        var port = int.TryParse(s[SiteSettingsService.Keys.BackupSftpPort], out var p) && p is > 0 and < 65536 ? p : 22;
        var known = s[SiteSettingsService.Keys.BackupSftpFingerprint];

        var methods = new List<AuthenticationMethod>();
        if (await GetSecretAsync(SiteSettingsService.Keys.BackupSftpPrivateKey) is { Length: > 0 } key)
        {
            try { methods.Add(new PrivateKeyAuthenticationMethod(user, new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(key.Trim() + "\n"))))); }
            catch (Exception ex) { throw new InvalidOperationException("Nie da się odczytać klucza prywatnego (obsługiwane: OpenSSH, PEM, bez hasła). " + ex.Message); }
        }
        if (await GetSecretAsync(SiteSettingsService.Keys.BackupSftpPassword) is { Length: > 0 } password)
            methods.Add(new PasswordAuthenticationMethod(user, password));
        if (methods.Count == 0) throw new InvalidOperationException("Podaj hasło albo klucz prywatny SFTP.");

        var client = new SftpClient(new Renci.SshNet.ConnectionInfo(host, port, user, [.. methods]) { Timeout = TimeSpan.FromSeconds(30) });
        string? seen = null;
        client.HostKeyReceived += (_, e) =>
        {
            seen = e.FingerPrintSHA256;
            // Zaufanie przy pierwszym połączeniu, potem tylko ten sam klucz — chroni przed podstawionym serwerem.
            e.CanTrust = string.IsNullOrEmpty(known) || string.Equals(known, seen, StringComparison.Ordinal);
        };
        try { await client.ConnectAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            if (!string.IsNullOrEmpty(known) && seen is not null && seen != known)
                throw new InvalidOperationException($"Klucz serwera SFTP się zmienił (było {known}, jest {seen}). Jeśli to zamierzone, kliknij „Zapomnij odcisk” i połącz ponownie.");
            throw new InvalidOperationException($"Nie udało się połączyć z {host}:{port} — {ex.Message}");
        }
        if (string.IsNullOrEmpty(known) && seen is not null)
            await settings.SetAsync(SiteSettingsService.Keys.BackupSftpFingerprint, seen);
        return client;
    }

    private async Task<string> SftpDirAsync(string? sub = null)
    {
        var dir = (await settings.GetAsync(SiteSettingsService.Keys.BackupSftpDir)).Trim().TrimEnd('/');
        if (dir.Length == 0) dir = "ptscheduler-backups";
        return sub is null ? dir : $"{dir}/{sub}";
    }

    private static void EnsureSftpDir(SftpClient client, string dir)
    {
        var current = dir.StartsWith('/') ? "" : ".";
        foreach (var part in dir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? "/" + part : $"{current}/{part}";
            if (!client.Exists(current)) client.CreateDirectory(current);
        }
    }

    private async Task<string> UploadSftpAsync(string localPath, string slug, string name, CancellationToken ct)
    {
        using var client = await ConnectSftpAsync(ct);
        var dir = await SftpDirAsync(slug);
        EnsureSftpDir(client, dir);
        var final = $"{dir}/{name}";
        var partial = final + ".part";
        await using (var fs = File.OpenRead(localPath))
            await Task.Run(() => client.UploadFile(fs, partial), ct);
        if (client.Exists(final)) client.DeleteFile(final);
        client.RenameFile(partial, final);
        return final;
    }

    public Task ForgetSftpFingerprintAsync() => settings.SetAsync(SiteSettingsService.Keys.BackupSftpFingerprint, "");

    // ── Google Drive ────────────────────────────────────────────────────────

    /// <summary>
    /// Ten sam klient OAuth i adres powrotu co kalendarz trenerów — zakres drive.file daje dostęp
    /// wyłącznie do plików utworzonych przez Portal, nie do reszty dysku.
    /// </summary>
    public async Task<(string? Url, string? Error)> BuildDriveAuthorizeUrlAsync()
    {
        var cfg = await google.GetConfigAsync();
        if (cfg is null) return (null, "Najpierw skonfiguruj klienta Google w Konfiguracja platformy → Usługi.");
        var state = GdriveStatePrefix + StateProtector.Protect(Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(15));
        var url = "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(cfg.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(cfg.RedirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(DriveScopes)}"
            + "&access_type=offline&prompt=consent"
            + $"&state={Uri.EscapeDataString(state)}";
        return (url, null);
    }

    public async Task<string> HandleDriveCallbackAsync(string? code, string state, string? error)
    {
        const string back = "/panel/backups?gdrive=";
        try { StateProtector.Unprotect(state[GdriveStatePrefix.Length..]); }
        catch { return back + "error"; }
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return back + "denied";

        var cfg = await google.GetConfigAsync();
        if (cfg is null) return back + "error";
        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["redirect_uri"] = cfg.RedirectUri,
            ["grant_type"] = "authorization_code"
        }));
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Google Drive: wymiana kodu nie powiodła się: {Status} {Body}", (int)resp.StatusCode, body);
            return back + "error";
        }
        var token = JsonSerializer.Deserialize<TokenResponse>(body, Json);
        if (string.IsNullOrEmpty(token?.RefreshToken)) return back + "error";
        if (token.Scope is not null && !token.Scope.Contains("drive.file", StringComparison.Ordinal)) return back + "scope";

        await SetSecretAsync(SiteSettingsService.Keys.BackupGdriveRefreshToken, token.RefreshToken);
        await settings.SetManyAsync(new Dictionary<string, string>
        {
            [SiteSettingsService.Keys.BackupGdriveEmail] = EmailFromIdToken(token.IdToken) ?? "",
            [SiteSettingsService.Keys.BackupGdriveFolderId] = "",
            [SiteSettingsService.Keys.BackupOffsiteTarget] = TargetGdrive
        });
        _driveAccess = null;
        return back + "connected";
    }

    public async Task DisconnectDriveAsync()
    {
        if (await GetSecretAsync(SiteSettingsService.Keys.BackupGdriveRefreshToken) is { } refresh)
        {
            try
            {
                using var http = httpFactory.CreateClient();
                await http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }));
            }
            catch (Exception ex) { logger.LogInformation(ex, "Google Drive: cofnięcie zgody nie powiodło się."); }
        }
        await settings.SetManyAsync(new Dictionary<string, string>
        {
            [SiteSettingsService.Keys.BackupGdriveRefreshToken] = "",
            [SiteSettingsService.Keys.BackupGdriveEmail] = "",
            [SiteSettingsService.Keys.BackupGdriveFolderId] = ""
        });
        if (await GetTargetAsync() == TargetGdrive) await settings.SetAsync(SiteSettingsService.Keys.BackupOffsiteTarget, TargetOff);
        _driveAccess = null;
    }

    private async Task<(string Token, string? Email)> DriveAccessAsync(CancellationToken ct)
    {
        var email = await settings.GetAsync(SiteSettingsService.Keys.BackupGdriveEmail);
        if (_driveAccess is { } c && c.ExpiresUtc > DateTime.UtcNow.AddMinutes(5)) return (c.Token, email);

        var cfg = await google.GetConfigAsync() ?? throw new InvalidOperationException("Brak klienta Google w Konfiguracja platformy → Usługi.");
        var refresh = await GetSecretAsync(SiteSettingsService.Keys.BackupGdriveRefreshToken)
            ?? throw new InvalidOperationException("Google Drive nie jest połączony — kliknij „Połącz z Google Drive”.");
        using var http = httpFactory.CreateClient();
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["refresh_token"] = refresh,
            ["grant_type"] = "refresh_token"
        }), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(body.Contains("invalid_grant", StringComparison.Ordinal)
                ? "Google cofnął dostęp do Dysku (zgoda usunięta albo aplikacja Google w trybie testowym — token ważny 7 dni). Połącz ponownie."
                : $"Google odrzucił odświeżenie tokenu ({(int)resp.StatusCode}).");
        var token = JsonSerializer.Deserialize<TokenResponse>(body, Json)!;
        _driveAccess = (token.AccessToken!, DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn)));
        return (token.AccessToken!, email);
    }

    private async Task<string> EnsureDriveFolderAsync(string token, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var id = await settings.GetAsync(SiteSettingsService.Keys.BackupGdriveFolderId);
        if (!string.IsNullOrEmpty(id))
        {
            var check = await http.GetAsync($"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}?fields=id,trashed", ct);
            if (check.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await check.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("trashed", out var t) || !t.GetBoolean()) return id;
            }
        }
        var create = await http.PostAsync("https://www.googleapis.com/drive/v3/files?fields=id",
            JsonContent(new { name = DriveFolderName, mimeType = "application/vnd.google-apps.folder" }), ct);
        await EnsureDriveOk(create, "utworzyć folderu na Dysku", ct);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync(ct));
        id = created.RootElement.GetProperty("id").GetString()!;
        await settings.SetAsync(SiteSettingsService.Keys.BackupGdriveFolderId, id);
        return id;
    }

    private async Task<string> DriveUploadAsync(string token, string folderId, string localPath, string name, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("backup-offsite");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var length = new FileInfo(localPath).Length;
        using var start = new HttpRequestMessage(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id")
        {
            Content = JsonContent(new { name, parents = new[] { folderId } })
        };
        start.Headers.Add("X-Upload-Content-Type", "application/octet-stream");
        start.Headers.Add("X-Upload-Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var session = await http.SendAsync(start, ct);
        await EnsureDriveOk(session, "rozpocząć wysyłki na Dysk", ct);
        var location = session.Headers.Location ?? throw new InvalidOperationException("Google Drive nie zwrócił adresu wysyłki.");

        await using var fs = File.OpenRead(localPath);
        using var put = new HttpRequestMessage(HttpMethod.Put, location) { Content = new StreamContent(fs, 1024 * 1024) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        put.Content.Headers.ContentLength = length;
        var done = await http.SendAsync(put, ct);
        await EnsureDriveOk(done, "wysłać pliku na Dysk", ct);
        using var doc = JsonDocument.Parse(await done.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> UploadDriveAsync(string localPath, string name, CancellationToken ct)
    {
        var (token, _) = await DriveAccessAsync(ct);
        var folder = await EnsureDriveFolderAsync(token, ct);
        await DriveUploadAsync(token, folder, localPath, name, ct);
        return $"{DriveFolderName}/{name}";
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json");

    private static async Task EnsureDriveOk(HttpResponseMessage resp, string what, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch { }
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden && body.Contains("storageQuotaExceeded", StringComparison.Ordinal))
            message = "brak miejsca na Dysku Google";
        throw new InvalidOperationException($"Nie udało się {what} ({(int)resp.StatusCode}{(message is null ? "" : $": {message}")}).");
    }

    private static string? EmailFromIdToken(string? idToken)
    {
        var parts = idToken?.Split('.');
        if (parts is not { Length: 3 }) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
}
