using System.Net.Http.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// Dane z Portalu do polecania aplikacji: link polecający trenera, statystyki i stopka „Zrobione w …”
/// na publicznej stronie. Odpowiedź trzymamy w pamięci przez 6 godzin — strona główna nie czeka na Portal.
/// </summary>
public class PlatformInfoService(IHttpClientFactory httpFactory, ILogger<PlatformInfoService> logger)
{
    public sealed record Info(string PlatformName, string BadgeUrl, bool BadgeRequired, string ReferralLink,
        int Referred, int Paying, int FreeMonths, int BonusDays, int RewardsLeft, bool ViaStripe);

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private Info? _cached;
    private DateTime _fetchedAt;
    private Task<Info?>? _inflight;
    private readonly object _gate = new();

    public static bool Configured =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PORTAL_URL"))
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TENANT_SLUG"))
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));

    /// <summary>Ostatnia znana odpowiedź (bez czekania) — odświeża się w tle, gdy jest stara.</summary>
    public Info? Cached
    {
        get
        {
            if (Configured && (_cached is null || DateTime.UtcNow - _fetchedAt > Ttl)) _ = GetAsync();
            return _cached;
        }
    }

    public Task<Info?> GetAsync(bool fresh = false)
    {
        if (!Configured) return Task.FromResult<Info?>(null);
        if (!fresh && _cached is not null && DateTime.UtcNow - _fetchedAt <= Ttl) return Task.FromResult<Info?>(_cached);
        lock (_gate) return _inflight ??= FetchAsync();
    }

    private async Task<Info?> FetchAsync()
    {
        try
        {
            var portal = Environment.GetEnvironmentVariable("PORTAL_URL")!.TrimEnd('/');
            var slug = Environment.GetEnvironmentVariable("TENANT_SLUG")!;
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{portal}/api/internal/tenants/{Uri.EscapeDataString(slug)}/growth");
            req.Headers.Add("X-Internal-Secret", Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            using var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode && await resp.Content.ReadFromJsonAsync<Info>() is { } info)
            {
                _cached = info;
                _fetchedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "Dane polecania z Portalu niedostępne.");
        }
        finally { lock (_gate) _inflight = null; }
        return _cached;
    }
}
