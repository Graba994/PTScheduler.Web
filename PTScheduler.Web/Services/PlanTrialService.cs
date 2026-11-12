using System.Net.Http.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// Jednorazowe „Wypróbuj 14 dni” z zablokowanej funkcji: Portal włącza okres próbny najtańszego wyższego planu z tą
/// funkcją (rachunki za obecny plan idą bez zmian) i od razu odsyła nowe uprawnienia.
/// </summary>
public sealed class PlanTrialService(IHttpClientFactory httpFactory, EntitlementService entitlements, ILogger<PlanTrialService> logger)
{
    public sealed record Result(bool Ok, string Message);

    private sealed record Response(bool Ok, string? Error, string? Plan, DateTime? Until);

    public async Task<Result> StartAsync(string? feature)
    {
        if (!PlatformInfoService.Configured) return new(false, "Okres próbny włączysz, gdy aplikacja jest połączona z platformą.");
        try
        {
            var portal = Environment.GetEnvironmentVariable("PORTAL_URL")!.TrimEnd('/');
            var slug = Uri.EscapeDataString(Environment.GetEnvironmentVariable("TENANT_SLUG")!);
            var secret = Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{portal}/api/internal/tenants/{slug}/plan-trial") { Content = JsonContent.Create(new { feature }) };
            req.Headers.Add("X-Internal-Secret", secret);
            using var resp = await http.SendAsync(req);
            var r = resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<Response>() : null;
            if (r is not { Ok: true }) return new(false, r?.Error ?? "Nie udało się włączyć okresu próbnego. Spróbuj za chwilę.");

            // Portal wysyła uprawnienia sam — gdyby nie doszły, pobieramy je od razu.
            using var pull = new HttpRequestMessage(HttpMethod.Get, $"{portal}/api/internal/tenants/{slug}/entitlements");
            pull.Headers.Add("X-Internal-Secret", secret);
            using var pulled = await http.SendAsync(pull);
            if (pulled.IsSuccessStatusCode) entitlements.ReplaceFromJson(await pulled.Content.ReadAsStringAsync());

            return new(true, $"Masz plan {r.Plan} do {(r.Until ?? DateTime.UtcNow.AddDays(14)).ToLocalTime():d.MM} — wszystkie jego funkcje są już włączone.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Okres próbny planu nie włączony.");
            return new(false, "Platforma nie odpowiada. Spróbuj za chwilę.");
        }
    }
}
