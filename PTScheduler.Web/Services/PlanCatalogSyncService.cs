using System.Net.Http.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// Pobiera cennik z Portalu (aktywne plany i ich funkcje) do <see cref="PlanCatalog"/> — przy starcie i co 30 minut.
/// Bez Portalu zostaje lista zapasowa z kodu.
/// </summary>
public sealed class PlanCatalogSyncService(IHttpClientFactory httpFactory, ILogger<PlanCatalogSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!PlatformInfoService.Configured) return;
        await Task.Delay(TimeSpan.FromSeconds(15), ct);
        while (!ct.IsCancellationRequested)
        {
            await RefreshAsync(ct);
            await Task.Delay(TimeSpan.FromMinutes(30), ct);
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var portal = Environment.GetEnvironmentVariable("PORTAL_URL")!.TrimEnd('/');
            var slug = Environment.GetEnvironmentVariable("TENANT_SLUG")!;
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{portal}/api/internal/tenants/{Uri.EscapeDataString(slug)}/plans");
            req.Headers.Add("X-Internal-Secret", Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(10);
            using var resp = await http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                PlanCatalog.Apply(await resp.Content.ReadFromJsonAsync<PlanCatalog.Feed>(cancellationToken: ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "Cennik z Portalu niedostępny — zostaje lista zapasowa.");
        }
    }
}
