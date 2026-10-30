using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Wideo trenerów w jednej, centralnej bibliotece Bunny Stream (konfiguruje administrator platformy).
/// Każdy trener ma w niej własne kolekcje („foldery”): „{slug} · kursy” i „{slug} · cwiczenia”.
/// Klucz biblioteki nie opuszcza Portalu: aplikacja trenera prosi o utworzenie filmu, dostaje jednorazowy
/// podpis przesyłania (TUS) tylko dla tego filmu i wysyła plik prosto do Bunny. Usunąć i podejrzeć można
/// tylko własne filmy (rejestr <see cref="TenantVideo"/>). Limit = przestrzeń z pakietu + dodatki + dokupione GB —
/// dokładnie tyle, ile trener opłaca.
/// </summary>
public class VideoHubService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    CreditService credits,
    BunnyPlatformService legacy,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    ILogger<VideoHubService> logger)
{
    private const string StreamApi = "https://video.bunnycdn.com";
    public const long GB = 1024L * 1024 * 1024;
    /// <summary>Ważność podpisu przesyłania — duże pliki przez słabe łącze potrzebują czasu.</summary>
    private static readonly TimeSpan UploadWindow = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    private IDataProtector Secrets => dataProtection.CreateProtector("bunny-library-key");

    public sealed record Library(string LibraryId, string ApiKey, string? CdnHostname);

    public sealed record Status(bool Enabled, string? Reason, string? LibraryId, string? CdnHostname, long QuotaBytes, long UsedBytes,
        int Videos, string? LegacyLibraryId);

    public sealed record Upload(string VideoId, string LibraryId, string Signature, long Expires, string? CollectionId);

    public sealed record VideoInfo(string VideoId, string Title, long SizeBytes, int LengthSeconds, int State);

    // ── Konfiguracja (Panel → Konfiguracja → Usługi) ────────────────────────────────

    public async Task<Library?> LibraryAsync()
    {
        var s = await settings.GetAllAsync(K.PlatformBunnyLibraryId, K.PlatformBunnyApiKey, K.PlatformBunnyCdnHostname);
        var id = s[K.PlatformBunnyLibraryId]?.Trim();
        var key = Unprotect(s[K.PlatformBunnyApiKey]);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key)) return null;
        return new Library(id, key, string.IsNullOrWhiteSpace(s[K.PlatformBunnyCdnHostname]) ? null : s[K.PlatformBunnyCdnHostname].Trim());
    }

    public async Task SaveLibraryAsync(string libraryId, string? apiKey, string? cdnHostname)
    {
        var values = new Dictionary<string, string>
        {
            [K.PlatformBunnyLibraryId] = libraryId.Trim(),
            [K.PlatformBunnyCdnHostname] = (cdnHostname ?? "").Trim()
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) values[K.PlatformBunnyApiKey] = Secrets.Protect(apiKey.Trim());
        await settings.SetManyAsync(values);
    }

    /// <summary>Starsze wersje trzymały klucz jawnie — wtedy zwracamy go bez zmian.</summary>
    private string? Unprotect(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        try { return Secrets.Unprotect(stored); }
        catch (CryptographicException) { return stored; }
    }

    public async Task<(bool Ok, string Message)> TestAsync(string libraryId, string apiKey)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{StreamApi}/library/{libraryId.Trim()}/collections?page=1&itemsPerPage=1");
            req.Headers.Add("AccessKey", apiKey.Trim());
            using var resp = await Http(15).SendAsync(req);
            return resp.IsSuccessStatusCode
                ? (true, "Biblioteka odpowiada — klucz i Library ID są poprawne.")
                : (false, $"Bunny odpowiedział {(int)resp.StatusCode} — sprawdź Library ID i klucz API biblioteki (Stream → biblioteka → API).");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── Limit i stan ───────────────────────────────────────────────────────────────

    /// <summary>Przestrzeń, za którą trener płaci: pakiet z wideo (Bunny) + dodatki miesięczne + dokupione GB.</summary>
    public async Task<long> QuotaBytesAsync(PortalDbContext db, Tenant tenant)
    {
        var plan = await PlanFeatureCatalog.EffectiveAsync(db, tenant);
        if (plan is null || !PlanHasVideo(plan)) return 0;
        var extra = await AddonService.ExtraLimitsAsync(db, tenant.Id);
        var bought = (await credits.GetBalancesAsync(tenant.Id)).GetValueOrDefault("cdn_storage_gb", 0);
        return Quota(plan.MaxVideoStorageGB, extra.StorageGb, bought);
    }

    /// <summary>Zajęte miejsce trenera: filmy i zdjęcia razem (jeden limit z pakietu).</summary>
    public static async Task<long> UsedBytesAsync(PortalDbContext db, int tenantId, CancellationToken ct = default) =>
        (await db.TenantVideos.Where(v => v.TenantId == tenantId).SumAsync(v => (long?)v.SizeBytes, ct) ?? 0)
        + (await db.TenantImages.Where(i => i.TenantId == tenantId).SumAsync(i => (long?)i.SizeBytes, ct) ?? 0);

    /// <summary>Blokada na trenera — równoległe przesyłania (filmy i zdjęcia) nie przekroczą wspólnego limitu.</summary>
    public static SemaphoreSlim Gate(int tenantId) => Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    public static bool PlanHasVideo(Plan plan) =>
        string.Equals(plan.VideoProvider, "bunny", StringComparison.OrdinalIgnoreCase) && plan.MaxVideoStorageGB > 0;

    /// <summary>Limit w bajtach; „bez limitu” w planie (int.MaxValue) zostaje bez limitu.</summary>
    public static long Quota(int planGb, int addonGb, decimal boughtGb)
    {
        if (planGb <= 0) return 0;
        if (planGb == int.MaxValue) return long.MaxValue;
        return (planGb + Math.Max(0, addonGb)) * GB + (long)(Math.Max(0m, boughtGb) * GB);
    }

    /// <summary>Czy plik się zmieści — do limitu włącznie (ani bajta mniej, ani więcej).</summary>
    public static bool Fits(long usedBytes, long fileBytes, long quotaBytes) =>
        fileBytes > 0 && quotaBytes > 0 && (quotaBytes == long.MaxValue || usedBytes + fileBytes <= quotaBytes);

    public async Task<Status> StatusAsync(Tenant tenant)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var lib = await LibraryAsync();
        var quota = await QuotaBytesAsync(db, tenant);
        var used = await UsedBytesAsync(db, tenant.Id);
        var count = await db.TenantVideos.CountAsync(v => v.TenantId == tenant.Id);
        string? reason = lib is null ? "Hosting wideo nie jest jeszcze skonfigurowany na platformie."
            : quota <= 0 ? "Twój pakiet nie zawiera hostingu wideo." : null;
        return new Status(reason is null, reason, lib?.LibraryId, lib?.CdnHostname, quota, used, count, tenant.BunnyLibraryId);
    }

    // ── Przesyłanie, podgląd, usuwanie ─────────────────────────────────────────────

    /// <summary>
    /// Tworzy film w folderze trenera i zwraca podpis przesyłania. Odmawia, gdy plik nie mieści się w opłaconym limicie.
    /// Rozmiar trafia od razu do rejestru — równoległe przesyłania nie przekroczą limitu.
    /// </summary>
    public async Task<(Upload? Upload, string? Error)> CreateAsync(Tenant tenant, string kind, string title, long sizeBytes, CancellationToken ct = default)
    {
        if (!VideoKinds.IsValid(kind)) return (null, "Nieznany rodzaj filmu.");
        if (sizeBytes <= 0) return (null, "Pusty plik.");
        var lib = await LibraryAsync();
        if (lib is null) return (null, "Hosting wideo nie jest jeszcze skonfigurowany na platformie.");

        var gate = Gate(tenant.Id);
        await gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var quota = await QuotaBytesAsync(db, tenant);
            if (quota <= 0) return (null, "Twój pakiet nie zawiera hostingu wideo.");
            var used = await UsedBytesAsync(db, tenant.Id, ct);
            if (!Fits(used, sizeBytes, quota))
                return (null, $"Brak miejsca: zajęte {Gb(used)} z {Gb(quota)}, plik ma {Gb(sizeBytes)}. Dokup przestrzeń w Sklepie albo usuń niepotrzebne filmy i zdjęcia.");

            var collection = await EnsureFolderAsync(db, lib, tenant, kind, ct);
            var cleanTitle = string.IsNullOrWhiteSpace(title) ? "Film" : title.Trim()[..Math.Min(180, title.Trim().Length)];
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{StreamApi}/library/{lib.LibraryId}/videos")
            {
                Content = JsonContent.Create(new { title = cleanTitle, collectionId = collection })
            };
            req.Headers.Add("AccessKey", lib.ApiKey);
            using var resp = await Http(30).SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Bunny: nie utworzono filmu dla {Slug}: {Status}", tenant.Slug, (int)resp.StatusCode);
                return (null, "Bunny nie przyjął nowego filmu — spróbuj ponownie za chwilę.");
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var guid = doc.RootElement.GetProperty("guid").GetString();
            if (string.IsNullOrEmpty(guid)) return (null, "Bunny nie zwrócił identyfikatora filmu.");

            db.TenantVideos.Add(new TenantVideo { TenantId = tenant.Id, VideoGuid = guid, Kind = kind, Title = cleanTitle, SizeBytes = sizeBytes });
            await db.SaveChangesAsync(ct);

            var expires = DateTimeOffset.UtcNow.Add(UploadWindow).ToUnixTimeSeconds();
            return (new Upload(guid, lib.LibraryId, Sign(lib.LibraryId, lib.ApiKey, expires, guid), expires, collection), null);
        }
        finally { gate.Release(); }
    }

    /// <summary>Podpis TUS Bunny: SHA256(library_id + api_key + expiration_time + video_id), szesnastkowo.</summary>
    public static string Sign(string libraryId, string apiKey, long expires, string videoId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(libraryId + apiKey + expires + videoId))).ToLowerInvariant();

    private async Task<string?> EnsureFolderAsync(PortalDbContext db, Library lib, Tenant tenant, string kind, CancellationToken ct)
    {
        var folder = await db.TenantVideoFolders.FirstOrDefaultAsync(f => f.TenantId == tenant.Id && f.Kind == kind, ct);
        if (folder is not null) return folder.CollectionId;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{StreamApi}/library/{lib.LibraryId}/collections")
            {
                Content = JsonContent.Create(new { name = $"{tenant.Slug} · {kind}" })
            };
            req.Headers.Add("AccessKey", lib.ApiKey);
            using var resp = await Http(20).SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Bunny: nie utworzono folderu {Kind} dla {Slug}: {Status}", kind, tenant.Slug, (int)resp.StatusCode);
                return null; // film i tak powstanie — tylko poza folderem
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var id = doc.RootElement.GetProperty("guid").GetString();
            if (string.IsNullOrEmpty(id)) return null;
            db.TenantVideoFolders.Add(new TenantVideoFolder { TenantId = tenant.Id, Kind = kind, CollectionId = id });
            await db.SaveChangesAsync(ct);
            return id;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bunny: błąd tworzenia folderu dla {Slug}.", tenant.Slug);
            return null;
        }
    }

    public async Task<VideoInfo?> InfoAsync(Tenant tenant, string guid)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.TenantVideos.AsNoTracking().FirstOrDefaultAsync(v => v.TenantId == tenant.Id && v.VideoGuid == guid);
        var lib = await LibraryAsync();
        if (row is null || lib is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{StreamApi}/library/{lib.LibraryId}/videos/{guid}");
            req.Headers.Add("AccessKey", lib.ApiKey);
            using var resp = await Http(15).SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new VideoInfo(guid, row.Title, row.SizeBytes, 0, -1);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var e = doc.RootElement;
            return new VideoInfo(guid, row.Title, row.SizeBytes,
                e.TryGetProperty("length", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 0,
                e.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 0);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Bunny: brak informacji o filmie {Guid}.", guid);
            return new VideoInfo(guid, row.Title, row.SizeBytes, 0, -1);
        }
    }

    public async Task<IReadOnlyList<TenantVideo>> ListAsync(Tenant tenant)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TenantVideos.AsNoTracking().Where(v => v.TenantId == tenant.Id).OrderByDescending(v => v.CreatedAt).ToListAsync();
    }

    /// <summary>Usuwa własny film trenera (z Bunny i z rejestru — miejsce wraca od razu).</summary>
    public async Task<bool> DeleteAsync(Tenant tenant, string guid)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var row = await db.TenantVideos.FirstOrDefaultAsync(v => v.TenantId == tenant.Id && v.VideoGuid == guid);
        if (row is null) return false;
        var lib = await LibraryAsync();
        if (lib is not null && !await DeleteRemoteAsync(lib, $"videos/{guid}")) return false;
        db.TenantVideos.Remove(row);
        // Ćwiczenia społeczności z tym filmem tracą wideo — nie zostawiamy martwych odnośników.
        await db.CommunityExercises.Where(c => c.TenantId == tenant.Id && c.VideoType == "Bunny" && c.VideoRef != null && c.VideoRef.EndsWith(guid))
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.VideoType, "None").SetProperty(c => c.VideoRef, (string?)null));
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// RODO — „usuń wszystkie moje dane wideo”: filmy trenera, jego foldery w Bunny, ćwiczenia udostępnione społeczności
    /// i starą osobną bibliotekę (jeśli była). Zwraca liczbę usuniętych filmów.
    /// </summary>
    public async Task<int> DeleteAllAsync(Tenant tenant)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var lib = await LibraryAsync();
        var rows = await db.TenantVideos.Where(v => v.TenantId == tenant.Id).ToListAsync();
        var removed = 0;
        foreach (var row in rows)
        {
            if (lib is not null && !await DeleteRemoteAsync(lib, $"videos/{row.VideoGuid}")) continue;
            db.TenantVideos.Remove(row);
            removed++;
        }
        var folders = await db.TenantVideoFolders.Where(f => f.TenantId == tenant.Id).ToListAsync();
        foreach (var f in folders)
            if (lib is null || await DeleteRemoteAsync(lib, $"collections/{f.CollectionId}"))
                db.TenantVideoFolders.Remove(f);
        await db.CommunityExercises.Where(c => c.TenantId == tenant.Id).ExecuteDeleteAsync();
        db.TenantEvents.Add(new TenantEvent { TenantId = tenant.Id, EventType = "video_data_deleted", Detail = $"{removed} filmów" });
        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(tenant.BunnyLibraryId))
        {
            await legacy.DeleteLibraryAsync(tenant);
            await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteUpdateAsync(u => u
                .SetProperty(t => t.BunnyLibraryId, (string?)null)
                .SetProperty(t => t.BunnyLibraryApiKey, (string?)null)
                .SetProperty(t => t.BunnyCdnHostname, (string?)null));
        }
        return removed;
    }

    private async Task<bool> DeleteRemoteAsync(Library lib, string path)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{StreamApi}/library/{lib.LibraryId}/{path}");
            req.Headers.Add("AccessKey", lib.ApiKey);
            using var resp = await Http(20).SendAsync(req);
            // 404 = już nie ma — cel osiągnięty.
            if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound) return true;
            logger.LogWarning("Bunny: nie usunięto {Path}: {Status}", path, (int)resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bunny: błąd usuwania {Path}.", path);
            return false;
        }
    }

    public static string Gb(long bytes) => bytes == long.MaxValue ? "bez limitu"
        : bytes >= GB ? $"{bytes / (double)GB:0.##} GB" : $"{bytes / (1024.0 * 1024):0.#} MB";

    private HttpClient Http(int seconds)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(seconds);
        return http;
    }
}
