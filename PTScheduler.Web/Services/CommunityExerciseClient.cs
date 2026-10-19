using System.Net.Http.Json;
using System.Text.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// „Społeczność” w katalogu ćwiczeń: wspólna baza ćwiczeń trenerów platformy, trzymana w Portalu.
/// Trener udostępnia własne ćwiczenie tylko za zgodą; inni kopiują je do siebie. Dostęp — pakiet z wideo (Bunny).
/// </summary>
public class CommunityExerciseClient(IHttpClientFactory httpFactory, ILogger<CommunityExerciseClient> logger)
{
    public sealed record Item(int Id, string NamePl, string? NameEn, string? Description, string PrimaryMuscles, string SecondaryMuscles,
        string Category, string Level, string? Equipment, string? Force, string? Mechanic, string? Tracking, string ImageUrls,
        string VideoType, string? VideoRef, string? AuthorName, bool Mine, int Copies, DateTime UpdatedAt)
    {
        public string? Thumbnail => ImageUrls.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    public sealed record Page(bool Allowed, List<Item> Items, List<int> Shared);

    public sealed record ShareInput(int SourceExerciseId, string NamePl, string? NameEn, string? Description, string PrimaryMuscles,
        string? SecondaryMuscles, string? Category, string? Level, string? Equipment, string? Force, string? Mechanic, string? Tracking,
        List<string>? ImageUrls, string? VideoType, string? VideoRef, bool ShowAuthor, bool Consent);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool Available => PlatformInfoService.Configured;

    public async Task<Page> SearchAsync(string? q, string? muscle)
    {
        if (!Available) return new Page(false, [], []);
        try
        {
            var query = $"community/exercises?q={Uri.EscapeDataString(q ?? "")}&muscle={Uri.EscapeDataString(muscle ?? "")}";
            using var resp = await Client(15).SendAsync(Request(HttpMethod.Get, query));
            if (resp.IsSuccessStatusCode && await resp.Content.ReadFromJsonAsync<Page>(Json) is { } page) return page;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Społeczność ćwiczeń niedostępna.");
        }
        return new Page(false, [], []);
    }

    public async Task<(bool Ok, string? Error)> ShareAsync(ShareInput input)
    {
        if (!Available) return (false, "Społeczność działa w aplikacjach na platformie Hantly.");
        try
        {
            var req = Request(HttpMethod.Post, "community/exercises");
            req.Content = JsonContent.Create(input, options: Json);
            using var resp = await Client(20).SendAsync(req);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return (false, body.TryGetProperty("error", out var e) ? e.GetString() : "Nie udało się udostępnić ćwiczenia.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (false, "Nie udało się połączyć z platformą — spróbuj za chwilę.");
        }
    }

    public async Task<bool> UnshareAsync(int sourceExerciseId)
    {
        if (!Available) return false;
        try
        {
            using var resp = await Client(15).SendAsync(Request(HttpMethod.Delete, $"community/exercises/{sourceExerciseId}"));
            return resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public async Task<Item?> CopyAsync(int id)
    {
        if (!Available) return null;
        try
        {
            var req = Request(HttpMethod.Post, "community/exercises/copy");
            req.Content = JsonContent.Create(new { id });
            using var resp = await Client(15).SendAsync(req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<Item>(Json) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    private HttpClient Client(int seconds)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(seconds);
        return http;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        var portal = Environment.GetEnvironmentVariable("PORTAL_URL")!.TrimEnd('/');
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG")!;
        var req = new HttpRequestMessage(method, $"{portal}/api/internal/tenants/{Uri.EscapeDataString(slug)}/{path}");
        req.Headers.Add("X-Internal-Secret", Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));
        return req;
    }
}
