using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Zdjęcia trenerów (np. ćwiczeń) w jednej strefie Bunny Storage platformy, wydawane przez CDN (pull zone).
/// Hasło strefy nie opuszcza Portalu: aplikacja trenera wysyła tu gotowe, zmniejszone zdjęcie, a Portal sprawdza
/// limit (wspólny z filmami), zapisuje plik w folderze „{slug}/{rodzaj}/” i zwraca publiczny adres.
/// Usunąć można tylko własne zdjęcia (rejestr <see cref="TenantImage"/>).
/// </summary>
public class ImageHubService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    VideoHubService videos,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    ILogger<ImageHubService> logger)
{
    /// <summary>Aplikacja wysyła zdjęcia już zmniejszone (WebP ~1600 px) — 10 MB to z dużym zapasem.</summary>
    public const long MaxBytes = 10L * 1024 * 1024;
    public const string DefaultHost = "storage.bunnycdn.com";

    private IDataProtector Secrets => dataProtection.CreateProtector("bunny-storage-key");

    public sealed record Storage(string Zone, string Key, string Host, string Cdn);

    // ── Konfiguracja (Panel → Konfiguracja → Usługi) ────────────────────────────────

    public async Task<Storage?> StorageAsync()
    {
        var s = await settings.GetAllAsync(K.PlatformBunnyStorageZone, K.PlatformBunnyStorageKey, K.PlatformBunnyStorageHost, K.PlatformBunnyStorageCdn);
        var zone = s[K.PlatformBunnyStorageZone]?.Trim();
        var key = Unprotect(s[K.PlatformBunnyStorageKey]);
        var cdn = CleanHost(s[K.PlatformBunnyStorageCdn]);
        if (string.IsNullOrWhiteSpace(zone) || string.IsNullOrWhiteSpace(key) || cdn is null) return null;
        return new Storage(zone, key, CleanHost(s[K.PlatformBunnyStorageHost]) ?? DefaultHost, cdn);
    }

    public async Task SaveStorageAsync(string zone, string? key, string? host, string cdn)
    {
        var values = new Dictionary<string, string>
        {
            [K.PlatformBunnyStorageZone] = zone.Trim(),
            [K.PlatformBunnyStorageHost] = CleanHost(host) ?? DefaultHost,
            [K.PlatformBunnyStorageCdn] = CleanHost(cdn) ?? ""
        };
        if (!string.IsNullOrWhiteSpace(key)) values[K.PlatformBunnyStorageKey] = Secrets.Protect(key.Trim());
        await settings.SetManyAsync(values);
    }

    public async Task<string?> SavedKeyAsync() => Unprotect(await settings.GetAsync(K.PlatformBunnyStorageKey));

    private string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try { return Secrets.Unprotect(stored); }
        catch (CryptographicException) { return stored; }
    }

    /// <summary>„https://x.b-cdn.net/” → „x.b-cdn.net”; puste albo z niedozwolonymi znakami → null.</summary>
    public static string? CleanHost(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) v = v[8..];
        else if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) v = v[7..];
        v = v.TrimEnd('/').ToLowerInvariant();
        return v.Length is > 0 and <= 120 && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && v.Contains('.') ? v : null;
    }

    /// <summary>Host regionu musi być Bunny Storage — hasło strefy nie poleci pod inny adres.</summary>
    public static bool IsStorageHost(string host) =>
        host == DefaultHost || host.EndsWith("." + DefaultHost, StringComparison.Ordinal);

    public async Task<(bool Ok, string Message)> TestAsync(string zone, string key, string? host)
    {
        var h = CleanHost(host) ?? DefaultHost;
        if (!IsStorageHost(h)) return (false, $"Host regionu musi kończyć się na {DefaultHost} (np. {DefaultHost} albo uk.{DefaultHost}).");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{h}/{Uri.EscapeDataString(zone.Trim())}/");
            req.Headers.Add("AccessKey", key.Trim());
            using var resp = await Http(15).SendAsync(req);
            return resp.IsSuccessStatusCode
                ? (true, "Strefa odpowiada — nazwa, hasło i region są poprawne.")
                : (false, $"Bunny odpowiedział {(int)resp.StatusCode} — sprawdź nazwę strefy, hasło (Storage → strefa → FTP & API Access) i region.");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── Zdjęcia ─────────────────────────────────────────────────────────────────────

    /// <summary>Rozpoznaje format po pierwszych bajtach (nie po nagłówku, który przysyła klient).</summary>
    public static string? Extension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "webp";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        return null;
    }

    public static string PublicUrl(Storage s, string path) => $"https://{s.Cdn}/{path}";

    public async Task<bool> EnabledAsync() => await StorageAsync() is not null;

    public async Task<(string? Url, long Size, string? Error)> UploadAsync(Tenant tenant, string kind, byte[] data, CancellationToken ct = default)
    {
        if (!VideoKinds.IsValid(kind)) return (null, 0, "Nieznany rodzaj zdjęcia.");
        if (data.Length == 0) return (null, 0, "Pusty plik.");
        if (data.Length > MaxBytes) return (null, 0, "Zdjęcie jest za duże (najwięcej 10 MB).");
        if (Extension(data) is not { } ext) return (null, 0, "To nie jest zdjęcie (JPG, PNG albo WebP).");
        var storage = await StorageAsync();
        if (storage is null || !IsStorageHost(storage.Host)) return (null, 0, "Zdjęcia na platformie nie są jeszcze skonfigurowane.");

        var gate = VideoHubService.Gate(tenant.Id);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var quota = await videos.QuotaBytesAsync(db, tenant);
            if (quota <= 0) return (null, 0, "Twój pakiet nie zawiera miejsca na pliki.");
            var used = await VideoHubService.UsedBytesAsync(db, tenant.Id, ct);
            if (!VideoHubService.Fits(used, data.Length, quota))
                return (null, 0, $"Brak miejsca: zajęte {VideoHubService.Gb(used)} z {VideoHubService.Gb(quota)}. Dokup przestrzeń w Sklepie albo usuń niepotrzebne pliki.");

            var path = $"{tenant.Slug}/{kind}/{Guid.NewGuid():N}.{ext}";
            using var req = new HttpRequestMessage(HttpMethod.Put, $"https://{storage.Host}/{Uri.EscapeDataString(storage.Zone)}/{path}")
            {
                Content = new ByteArrayContent(data)
            };
            req.Content.Headers.ContentType = new("application/octet-stream");
            req.Headers.Add("AccessKey", storage.Key);
            req.Headers.Add("Checksum", Convert.ToHexString(SHA256.HashData(data)));
            using var resp = await Http(60).SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Bunny Storage: nie zapisano zdjęcia dla {Slug}: {Status}", tenant.Slug, (int)resp.StatusCode);
                return (null, 0, "Nie udało się zapisać zdjęcia — spróbuj ponownie za chwilę.");
            }
            db.TenantImages.Add(new TenantImage { TenantId = tenant.Id, Path = path, Kind = kind, SizeBytes = data.Length });
            await db.SaveChangesAsync(ct);
            return (PublicUrl(storage, path), data.Length, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Bunny Storage: błąd zapisu zdjęcia dla {Slug}.", tenant.Slug);
            return (null, 0, "Nie udało się zapisać zdjęcia — spróbuj ponownie za chwilę.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Usuwa własne zdjęcie trenera (adres z CDN platformy). Cudze albo nieznane — false.</summary>
    public async Task<bool> DeleteAsync(Tenant tenant, string url)
    {
        var storage = await StorageAsync();
        if (storage is null) return false;
        var prefix = $"https://{storage.Cdn}/";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var path = url[prefix.Length..];
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.TenantImages.FirstOrDefaultAsync(i => i.TenantId == tenant.Id && i.Path == path);
        if (row is null) return false;
        if (!await DeleteRemoteAsync(storage, path)) return false;
        db.TenantImages.Remove(row);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>RODO — wszystkie zdjęcia trenera. Zwraca liczbę usuniętych.</summary>
    public async Task<int> DeleteAllAsync(Tenant tenant)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var storage = await StorageAsync();
        var rows = await db.TenantImages.Where(i => i.TenantId == tenant.Id).ToListAsync();
        var removed = 0;
        foreach (var row in rows)
        {
            if (storage is not null && !await DeleteRemoteAsync(storage, row.Path)) continue;
            db.TenantImages.Remove(row);
            removed++;
        }
        await db.SaveChangesAsync();
        return removed;
    }

    private async Task<bool> DeleteRemoteAsync(Storage s, string path)
    {
        if (!IsStorageHost(s.Host)) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"https://{s.Host}/{Uri.EscapeDataString(s.Zone)}/{path}");
            req.Headers.Add("AccessKey", s.Key);
            using var resp = await Http(20).SendAsync(req);
            if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound) return true;
            logger.LogWarning("Bunny Storage: nie usunięto {Path}: {Status}", path, (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bunny Storage: błąd usuwania {Path}.", path);
            return false;
        }
    }

    private HttpClient Http(int seconds)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(seconds);
        return http;
    }
}
