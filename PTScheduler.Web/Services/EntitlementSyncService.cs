namespace PTScheduler.Web.Services;

// Pulls the tenant's current plan from the portal at startup and then periodically.
// TENANT_ENTITLEMENTS is baked into the container env at creation time, so without this
// a restart or Guardian rolling update (which clones the old env) would revert to a stale
// plan. The portal also pushes on plan changes; this is the safety net when a push is missed.
public class EntitlementSyncService(
    EntitlementService entitlements,
    ILogger<EntitlementSyncService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private string? _lastJson;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var portalUrl = Environment.GetEnvironmentVariable("PORTAL_URL");
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG");
        var secret = Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
        if (string.IsNullOrEmpty(portalUrl) || string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(secret))
            return; // Standalone / dev — env entitlements only.

        var url = $"{portalUrl.TrimEnd('/')}/api/internal/tenants/{Uri.EscapeDataString(slug)}/entitlements";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        while (!stoppingToken.IsCancellationRequested)
        {
            var ok = await TryPullAsync(http, url, secret, stoppingToken);
            try { await Task.Delay(ok ? RefreshInterval : RetryDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<bool> TryPullAsync(HttpClient http, string url, string secret, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Internal-Secret", secret);
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Entitlements pull from portal returned {Status}.", (int)resp.StatusCode);
                return false;
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json)) return false;
            if (json != _lastJson)
            {
                entitlements.ReplaceFromJson(json);
                _lastJson = json;
            }
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Entitlements pull from portal failed — keeping current plan.");
            return false;
        }
    }
}
