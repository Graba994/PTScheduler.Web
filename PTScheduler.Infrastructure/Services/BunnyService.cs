using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace PTScheduler.Infrastructure.Services;

public class BunnyService(IWebRootPathProvider webRoot, IHttpClientFactory httpFactory, ILogger<BunnyService> logger)
    : IBunnyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HttpClient PortalHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private string? PortalUrl => Environment.GetEnvironmentVariable("PORTAL_URL");
    private string? TenantSlug => Environment.GetEnvironmentVariable("TENANT_SLUG");
    private string? InternalSecret => Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");

    private string FilePath =>
        Path.Combine(webRoot.WebRootPath, "branding", "bunny-settings.json");

    public async Task<BunnySettingsDto> GetSettingsAsync()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                await using var fs = File.OpenRead(FilePath);
                var dto = await JsonSerializer.DeserializeAsync<BunnySettingsDto>(fs);
                if (dto is not null && !string.IsNullOrWhiteSpace(dto.ApiKey))
                    return dto;
            }
        }
        catch { }

        var platform = await FetchPlatformBunnySettingsAsync();
        if (platform is not null)
            return platform;

        return new BunnySettingsDto();
    }

    public async Task SaveSettingsAsync(BunnySettingsDto dto)
    {
        var dir = Path.Combine(webRoot.WebRootPath, "branding");
        Directory.CreateDirectory(dir);
        await using var fs = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(fs, dto, JsonOptions);
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(BunnySettingsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ApiKey) || string.IsNullOrWhiteSpace(dto.LibraryId))
            return (false, "Uzupełnij API key i Library ID.");

        try
        {
            var http = httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://video.bunnycdn.com/library/{dto.LibraryId}/videos?itemsPerPage=1");
            req.Headers.Add("AccessKey", dto.ApiKey);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)resp.StatusCode}: {body}");
            }
            return (true, "Połączenie OK.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, string? VideoId, string? Error)> CreateAndUploadAsync(
        string title, Stream file, CancellationToken ct = default)
    {
        var s = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(s.ApiKey) || string.IsNullOrWhiteSpace(s.LibraryId))
            return (false, null, "Bunny.net nie skonfigurowany.");

        try
        {
            var http = httpFactory.CreateClient();

            var createReq = new HttpRequestMessage(HttpMethod.Post,
                $"https://video.bunnycdn.com/library/{s.LibraryId}/videos")
            {
                Content = JsonContent.Create(new { title })
            };
            createReq.Headers.Add("AccessKey", s.ApiKey);
            using var createResp = await http.SendAsync(createReq, ct);
            if (!createResp.IsSuccessStatusCode)
            {
                var b = await createResp.Content.ReadAsStringAsync(ct);
                return (false, null, $"Create failed: {b}");
            }

            var created = await createResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var guid = created.GetProperty("guid").GetString();
            if (string.IsNullOrEmpty(guid)) return (false, null, "Brak GUID w odpowiedzi.");

            var uploadReq = new HttpRequestMessage(HttpMethod.Put,
                $"https://video.bunnycdn.com/library/{s.LibraryId}/videos/{guid}")
            {
                Content = new StreamContent(file)
            };
            uploadReq.Headers.Add("AccessKey", s.ApiKey);
            using var uploadResp = await http.SendAsync(uploadReq, ct);
            if (!uploadResp.IsSuccessStatusCode)
            {
                var b = await uploadResp.Content.ReadAsStringAsync(ct);
                return (false, guid, $"Upload failed: {b}");
            }

            return (true, guid, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string videoId)
    {
        var s = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(s.ApiKey) || string.IsNullOrWhiteSpace(s.LibraryId))
            return (false, "Bunny.net nie skonfigurowany.");

        try
        {
            var http = httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Delete,
                $"https://video.bunnycdn.com/library/{s.LibraryId}/videos/{videoId}");
            req.Headers.Add("AccessKey", s.ApiKey);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var b = await resp.Content.ReadAsStringAsync();
                return (false, $"HTTP {(int)resp.StatusCode}: {b}");
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<BunnyVideoInfo?> GetVideoInfoAsync(string videoId)
    {
        var s = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(s.ApiKey) || string.IsNullOrWhiteSpace(s.LibraryId))
            return null;

        try
        {
            var http = httpFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://video.bunnycdn.com/library/{s.LibraryId}/videos/{videoId}");
            req.Headers.Add("AccessKey", s.ApiKey);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var e = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return new BunnyVideoInfo
            {
                VideoId = videoId,
                Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                StorageSize = e.TryGetProperty("storageSize", out var ss) ? ss.GetInt64() : 0,
                Length = e.TryGetProperty("length", out var l) ? l.GetInt32() : 0,
                Status = e.TryGetProperty("status", out var st) ? st.GetInt32() : 0
            };
        }
        catch { return null; }
    }

    public string BuildEmbedUrl(string videoId)
    {
        var settings = GetSettingsAsync().GetAwaiter().GetResult();
        return $"https://iframe.mediadelivery.net/embed/{settings.LibraryId}/{videoId}";
    }

    public async Task<CentralizedCdnStatus?> GetCentralizedStatusAsync()
    {
        if (string.IsNullOrEmpty(PortalUrl) || string.IsNullOrEmpty(TenantSlug))
            return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{PortalUrl.TrimEnd('/')}/api/credits/{TenantSlug}");
            if (!string.IsNullOrEmpty(InternalSecret))
                req.Headers.TryAddWithoutValidation("X-Internal-Secret", InternalSecret);

            var resp = await PortalHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            return new CentralizedCdnStatus(
                PlatformCdnEnabled: root.GetProperty("platformCdnEnabled").GetBoolean(),
                StorageCredits: root.GetProperty("cdnStorageGb").GetDecimal(),
                BandwidthCredits: root.GetProperty("cdnBandwidthGb").GetDecimal());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch centralized CDN status from portal");
            return null;
        }
    }

    private async Task<BunnySettingsDto?> FetchPlatformBunnySettingsAsync()
    {
        if (string.IsNullOrEmpty(PortalUrl) || string.IsNullOrEmpty(TenantSlug))
            return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{PortalUrl.TrimEnd('/')}/api/credits/{TenantSlug}/bunny");
            if (!string.IsNullOrEmpty(InternalSecret))
                req.Headers.TryAddWithoutValidation("X-Internal-Secret", InternalSecret);

            var resp = await PortalHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.GetProperty("enabled").GetBoolean()) return null;

            return new BunnySettingsDto
            {
                Enabled = true,
                ApiKey = root.GetProperty("apiKey").GetString(),
                LibraryId = root.GetProperty("libraryId").GetString(),
                CdnHostname = root.TryGetProperty("cdnHostname", out var h) ? h.GetString() : null
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch platform Bunny settings from portal");
            return null;
        }
    }
}
