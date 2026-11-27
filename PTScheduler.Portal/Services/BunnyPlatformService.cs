using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Hosting wideo trenerów na jednym koncie Bunny platformy: każda instancja dostaje
/// własną bibliotekę Stream (osobny klucz), więc trener widzi i usuwa tylko swoje filmy.
/// Trener niczego nie konfiguruje — jedynie dokupuje przestrzeń i transfer.
/// </summary>
public class BunnyPlatformService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    IHttpClientFactory httpFactory,
    ILogger<BunnyPlatformService> logger)
{
    private const string Api = "https://api.bunny.net";

    public record LibraryInfo(string LibraryId, string ApiKey, string? CdnHostname);

    public async Task<bool> IsConfiguredAsync() =>
        !string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.PlatformBunnyAccountKey));

    /// <summary>Biblioteka instancji — zakładana przy pierwszym użyciu.</summary>
    public async Task<LibraryInfo?> EnsureLibraryAsync(int tenantId, CancellationToken ct = default)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return null;
        if (!string.IsNullOrEmpty(tenant.BunnyLibraryId) && !string.IsNullOrEmpty(tenant.BunnyLibraryApiKey))
            return new LibraryInfo(tenant.BunnyLibraryId, tenant.BunnyLibraryApiKey, tenant.BunnyCdnHostname);

        var accountKey = await settings.GetAsync(SiteSettingsService.Keys.PlatformBunnyAccountKey);
        if (string.IsNullOrWhiteSpace(accountKey)) return null;

        try
        {
            using var http = httpFactory.CreateClient();
            using var create = new HttpRequestMessage(HttpMethod.Post, $"{Api}/videolibrary")
            {
                Content = JsonContent.Create(new { Name = $"pt-{tenant.Slug}" })
            };
            create.Headers.Add("AccessKey", accountKey);
            using var resp = await http.SendAsync(create, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Bunny: nie utworzono biblioteki dla {Slug}: {Status} {Body}", tenant.Slug, (int)resp.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var libraryId = root.GetProperty("Id").GetRawText().Trim('"');
            var apiKey = root.GetProperty("ApiKey").GetString() ?? "";
            string? hostname = null;
            if (root.TryGetProperty("PullZoneId", out var pz) && pz.ValueKind == JsonValueKind.Number)
                hostname = await GetPullZoneHostnameAsync(http, accountKey, pz.GetInt64(), ct);

            tenant.BunnyLibraryId = libraryId;
            tenant.BunnyLibraryApiKey = apiKey;
            tenant.BunnyCdnHostname = hostname;
            db.TenantEvents.Add(new TenantEvent { TenantId = tenant.Id, EventType = "bunny_library_created", Detail = libraryId });
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Bunny: biblioteka {Library} dla {Slug}.", libraryId, tenant.Slug);
            return new LibraryInfo(libraryId, apiKey, hostname);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bunny: błąd tworzenia biblioteki dla {Slug}.", tenant.Slug);
            return null;
        }
    }

    private async Task<string?> GetPullZoneHostnameAsync(HttpClient http, string accountKey, long pullZoneId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}/pullzone/{pullZoneId}");
        req.Headers.Add("AccessKey", accountKey);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("Hostnames", out var hosts)) return null;
        string? first = null;
        foreach (var h in hosts.EnumerateArray())
        {
            var value = h.TryGetProperty("Value", out var v) ? v.GetString() : null;
            if (value is null) continue;
            first ??= value;
            if (h.TryGetProperty("IsSystemHostname", out var sys) && sys.ValueKind == JsonValueKind.True) return value;
        }
        return first;
    }

    /// <summary>Test klucza konta (lista bibliotek).</summary>
    public async Task<(bool Ok, string Message)> TestAccountKeyAsync(string accountKey)
    {
        try
        {
            using var http = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}/videolibrary?page=1&perPage=1");
            req.Headers.Add("AccessKey", accountKey);
            using var resp = await http.SendAsync(req);
            return resp.IsSuccessStatusCode
                ? (true, "Klucz konta działa.")
                : (false, $"Bunny odpowiedział {(int)resp.StatusCode} — sprawdź klucz (Account → API key).");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>Usuwa bibliotekę instancji (przy usuwaniu trenera). Filmy znikają bezpowrotnie.</summary>
    public async Task DeleteLibraryAsync(Tenant tenant)
    {
        if (string.IsNullOrEmpty(tenant.BunnyLibraryId)) return;
        var accountKey = await settings.GetAsync(SiteSettingsService.Keys.PlatformBunnyAccountKey);
        if (string.IsNullOrWhiteSpace(accountKey)) return;
        try
        {
            using var http = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{Api}/videolibrary/{tenant.BunnyLibraryId}");
            req.Headers.Add("AccessKey", accountKey);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                logger.LogWarning("Bunny: nie usunięto biblioteki {Library}: {Status}", tenant.BunnyLibraryId, (int)resp.StatusCode);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Bunny: błąd usuwania biblioteki {Library}.", tenant.BunnyLibraryId); }
    }
}
