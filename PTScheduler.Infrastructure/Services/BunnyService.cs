using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Wideo przez Portal: centralna biblioteka Bunny Stream z folderem trenera. Aplikacja nie zna klucza biblioteki —
/// Portal tworzy film (sprawdzając opłacony limit) i daje podpis przesyłania, a plik idzie prosto do Bunny protokołem TUS.
/// </summary>
public class BunnyService(IWebRootPathProvider webRoot, IHttpClientFactory httpFactory, ILogger<BunnyService> logger)
    : IBunnyService
{
    private const string TusEndpoint = "https://video.bunnycdn.com/tusupload";
    private const int ChunkBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan StatusTtl = TimeSpan.FromMinutes(2);
    private static VideoHubStatus? _status;
    private static DateTime _statusAt;

    private static string? PortalUrl => Environment.GetEnvironmentVariable("PORTAL_URL");
    private static string? TenantSlug => Environment.GetEnvironmentVariable("TENANT_SLUG");
    private static string? InternalSecret => Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
    private static bool Managed => !string.IsNullOrEmpty(PortalUrl) && !string.IsNullOrEmpty(TenantSlug);

    public async Task<VideoHubStatus> GetStatusAsync(bool refresh = false)
    {
        if (!Managed) return VideoHubStatus.Off with { LegacyLibraryId = LocalLegacyLibrary() };
        if (!refresh && _status is not null && DateTime.UtcNow - _statusAt < StatusTtl) return _status;
        try
        {
            using var resp = await Http(15).SendAsync(Request(HttpMethod.Get, "video"));
            if (resp.IsSuccessStatusCode)
            {
                var e = await resp.Content.ReadFromJsonAsync<JsonElement>();
                _status = new VideoHubStatus(
                    e.GetProperty("enabled").GetBoolean(),
                    Str(e, "reason"),
                    Str(e, "libraryId"),
                    e.GetProperty("quotaBytes").GetInt64(),
                    e.GetProperty("usedBytes").GetInt64(),
                    e.GetProperty("videos").GetInt32(),
                    Str(e, "legacyLibraryId") ?? LocalLegacyLibrary())
                {
                    ImagesEnabled = e.TryGetProperty("imagesEnabled", out var im) && im.ValueKind == JsonValueKind.True
                };
                _statusAt = DateTime.UtcNow;
                return _status;
            }
            logger.LogWarning("Portal: stan wideo niedostępny ({Status}).", (int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            logger.LogWarning(ex, "Portal: stan wideo niedostępny.");
        }
        return _status ?? new VideoHubStatus(false, "Nie udało się połączyć z platformą — spróbuj za chwilę.", null, 0, 0, 0, LocalLegacyLibrary());
    }

    public async Task<(bool Ok, string? VideoRef, string? Error)> UploadAsync(string title, string kind, Stream file, long sizeBytes,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        if (!Managed) return (false, null, VideoHubStatus.Off.Reason);
        string guid, libraryId, signature;
        long expires;
        try
        {
            var create = Request(HttpMethod.Post, "video");
            create.Content = JsonContent.Create(new { title, kind, sizeBytes });
            using var resp = await Http(40).SendAsync(create, ct);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!resp.IsSuccessStatusCode)
                return (false, null, Str(body, "error") ?? "Platforma nie przyjęła filmu — spróbuj ponownie.");
            guid = body.GetProperty("videoId").GetString()!;
            libraryId = body.GetProperty("libraryId").GetString()!;
            signature = body.GetProperty("signature").GetString()!;
            expires = body.GetProperty("expires").GetInt64();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Portal: nie utworzono filmu.");
            return (false, null, "Nie udało się połączyć z platformą — spróbuj za chwilę.");
        }

        try
        {
            await TusUploadAsync(libraryId, guid, signature, expires, title, file, sizeBytes, progress, ct);
            _status = null; // zajęte miejsce się zmieniło
            return (true, VideoRef.Make(libraryId, guid), null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bunny: przesyłanie filmu {Guid} nie powiodło się.", guid);
            // Nieudany film nie może zajmować opłaconego miejsca.
            await DeleteAsync(VideoRef.Make(libraryId, guid));
            return (false, null, ex is OperationCanceledException ? "Przesyłanie przerwane." : "Przesyłanie nie powiodło się — sprawdź połączenie i spróbuj ponownie.");
        }
    }

    /// <summary>Przesyłanie TUS do Bunny: utworzenie (POST) i kolejne części (PATCH) z podpisem tylko dla tego filmu.</summary>
    private async Task TusUploadAsync(string libraryId, string guid, string signature, long expires, string title, Stream file, long size,
        IProgress<long>? progress, CancellationToken ct)
    {
        var http = Http(600);
        void Auth(HttpRequestMessage r)
        {
            r.Headers.Add("AuthorizationSignature", signature);
            r.Headers.Add("AuthorizationExpire", expires.ToString());
            r.Headers.Add("VideoId", guid);
            r.Headers.Add("LibraryId", libraryId);
            r.Headers.Add("Tus-Resumable", "1.0.0");
        }

        using var create = new HttpRequestMessage(HttpMethod.Post, TusEndpoint);
        Auth(create);
        create.Headers.Add("Upload-Length", size.ToString());
        create.Headers.Add("Upload-Metadata", $"filetype {B64("video/mp4")},title {B64(title)}");
        create.Content = new ByteArrayContent([]);
        using var created = await http.SendAsync(create, ct);
        if (!created.IsSuccessStatusCode || created.Headers.Location is null)
            throw new HttpRequestException($"TUS: utworzenie odrzucone ({(int)created.StatusCode}).");
        var location = created.Headers.Location.IsAbsoluteUri ? created.Headers.Location : new Uri(new Uri(TusEndpoint), created.Headers.Location);

        var buffer = new byte[ChunkBytes];
        long offset = 0;
        while (offset < size)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await file.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
                if (n == 0) break;
                read += n;
            }
            if (read == 0) throw new IOException("Plik skończył się wcześniej niż zapowiadany rozmiar.");

            using var patch = new HttpRequestMessage(HttpMethod.Patch, location);
            Auth(patch);
            patch.Headers.Add("Upload-Offset", offset.ToString());
            patch.Content = new ByteArrayContent(buffer, 0, read);
            patch.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
            using var resp = await http.SendAsync(patch, ct);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"TUS: część odrzucona ({(int)resp.StatusCode}).");
            offset = resp.Headers.TryGetValues("Upload-Offset", out var v) && long.TryParse(v.FirstOrDefault(), out var o) ? o : offset + read;
            progress?.Report(offset);
        }
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string videoRef)
    {
        if (!Managed) return (false, VideoHubStatus.Off.Reason);
        try
        {
            using var resp = await Http(30).SendAsync(Request(HttpMethod.Delete, $"video/{Uri.EscapeDataString(VideoRef.Guid(videoRef))}"));
            _status = null;
            if (resp.IsSuccessStatusCode) return (true, null);
            return resp.StatusCode == System.Net.HttpStatusCode.NotFound
                ? (false, "Ten film nie należy do Twojej aplikacji albo został już usunięty.")
                : (false, "Nie udało się usunąć filmu — spróbuj ponownie.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, "Nie udało się połączyć z platformą — spróbuj za chwilę.");
        }
    }

    public async Task<BunnyVideoInfo?> GetVideoInfoAsync(string videoRef)
    {
        if (!Managed) return null;
        try
        {
            using var resp = await Http(15).SendAsync(Request(HttpMethod.Get, $"video/{Uri.EscapeDataString(VideoRef.Guid(videoRef))}"));
            if (!resp.IsSuccessStatusCode) return null;
            var e = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return new BunnyVideoInfo
            {
                VideoId = e.GetProperty("videoId").GetString() ?? "",
                Title = Str(e, "title") ?? "",
                StorageSize = e.GetProperty("sizeBytes").GetInt64(),
                Length = e.GetProperty("length").GetInt32(),
                Status = e.GetProperty("status").GetInt32()
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return null; }
    }

    public async Task<List<VideoItem>> ListAsync()
    {
        if (!Managed) return [];
        try
        {
            var status = await GetStatusAsync();
            using var resp = await Http(15).SendAsync(Request(HttpMethod.Get, "video/list"));
            if (!resp.IsSuccessStatusCode) return [];
            var items = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return items.EnumerateArray().Select(e => new VideoItem(
                VideoRef.Make(status.LibraryId ?? "", e.GetProperty("videoId").GetString()!),
                e.GetProperty("kind").GetString() ?? "",
                e.GetProperty("title").GetString() ?? "",
                e.GetProperty("sizeBytes").GetInt64(),
                e.GetProperty("createdAt").GetDateTime())).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return []; }
    }

    public async Task<(string? Url, string? Error)> UploadImageAsync(string kind, byte[] data, CancellationToken ct = default)
    {
        if (!Managed) return (null, VideoHubStatus.Off.Reason);
        try
        {
            using var req = Request(HttpMethod.Post, $"images?kind={Uri.EscapeDataString(kind)}");
            req.Content = new ByteArrayContent(data);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var resp = await Http(90).SendAsync(req, ct);
            var e = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            _status = null;
            return resp.IsSuccessStatusCode && Str(e, "url") is { } url
                ? (url, null)
                : (null, Str(e, "error") ?? "Nie udało się zapisać zdjęcia — spróbuj ponownie.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Portal: zdjęcie nie zostało zapisane.");
            return (null, "Nie udało się połączyć z platformą — spróbuj za chwilę.");
        }
    }

    public async Task<bool> DeleteImageAsync(string url)
    {
        if (!Managed) return false;
        try
        {
            using var req = Request(HttpMethod.Post, "images/delete");
            req.Content = JsonContent.Create(new { url });
            using var resp = await Http(30).SendAsync(req);
            _status = null;
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public async Task<(bool Ok, int Removed, string? Error)> DeleteAllAsync()
    {
        if (!Managed) return (true, 0, null);
        try
        {
            using var resp = await Http(300).SendAsync(Request(HttpMethod.Delete, "video"));
            _status = null;
            if (!resp.IsSuccessStatusCode) return (false, 0, "Platforma nie usunęła filmów — spróbuj ponownie.");
            var e = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return (true, e.GetProperty("removed").GetInt32(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (false, 0, "Nie udało się połączyć z platformą — spróbuj za chwilę.");
        }
    }

    public async Task<string?> EmbedUrlAsync(string? videoRef)
    {
        if (string.IsNullOrWhiteSpace(videoRef)) return null;
        var (lib, _) = VideoRef.Parse(videoRef);
        return VideoRef.Embed(videoRef, lib is null ? (await GetStatusAsync()).LegacyLibraryId : null);
    }

    /// <summary>Biblioteka ze starej, ręcznej konfiguracji trenera — tylko do odtwarzania dawnych filmów.</summary>
    private string? LocalLegacyLibrary()
    {
        try
        {
            var path = PrivateStorage.SettingsFile(webRoot, "bunny-settings.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<BunnySettingsDto>(File.ReadAllText(path))?.LibraryId;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{PortalUrl!.TrimEnd('/')}/api/internal/tenants/{Uri.EscapeDataString(TenantSlug!)}/{path}");
        if (!string.IsNullOrEmpty(InternalSecret)) req.Headers.TryAddWithoutValidation("X-Internal-Secret", InternalSecret);
        return req;
    }

    private HttpClient Http(int seconds)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(seconds);
        return http;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
}
