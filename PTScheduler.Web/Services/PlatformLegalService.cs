using System.Net.Http.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// Dokumenty platformy (regulamin, umowa powierzenia, polityka prywatności), które właściciel studia
/// musi zaakceptować — np. po zmianie regulaminu albo gdy aplikację założył administrator Portalu.
/// Gdy nic nie czeka, nie pytamy Portalu częściej niż raz na godzinę.
/// </summary>
public class PlatformLegalService(IHttpClientFactory httpFactory, ILogger<PlatformLegalService> logger)
{
    public sealed record Doc(string Key, string Title, string Version, string Url);
    public sealed record Status(string PlatformName, List<Doc> Pending);

    private static readonly TimeSpan QuietTtl = TimeSpan.FromHours(1);
    private DateTime _clearUntil;

    public async Task<Status?> PendingAsync()
    {
        if (!PlatformInfoService.Configured || DateTime.UtcNow < _clearUntil) return null;
        try
        {
            using var req = Request(HttpMethod.Get, "legal");
            using var resp = await Client().SendAsync(req);
            if (!resp.IsSuccessStatusCode || await resp.Content.ReadFromJsonAsync<Status>() is not { } status) return null;
            if (status.Pending.Count == 0) _clearUntil = DateTime.UtcNow + QuietTtl;
            return status;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "Dokumenty platformy z Portalu niedostępne.");
            return null;
        }
    }

    public async Task<bool> AcceptAsync(string email, IEnumerable<string> keys, string? ip, string? userAgent)
    {
        try
        {
            using var req = Request(HttpMethod.Post, "legal/accept");
            req.Content = JsonContent.Create(new { email, keys = keys.ToArray(), ip, userAgent });
            using var resp = await Client().SendAsync(req);
            if (resp.IsSuccessStatusCode) _clearUntil = DateTime.UtcNow + QuietTtl;
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Nie udało się zapisać akceptacji dokumentów w Portalu.");
            return false;
        }
    }

    private HttpClient Client()
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(8);
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
