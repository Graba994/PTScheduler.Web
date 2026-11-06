using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Dane demo w instancji trenera — tylko dla kont oznaczonych jako testowe. Portal woła aplikację trenera
/// (POST /internal/demo/…) z sekretem instancji; sama aplikacja nie ma przycisku, więc trener nie załaduje demo sam.
/// </summary>
public class TenantDemoService(IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config, ILogger<TenantDemoService> logger)
{
    // Pełny scenariusz to kilkadziesiąt tysięcy wierszy — dajemy czas.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public sealed record Scenario(string Key, string Name, string Description, int Months, int Clients);
    public sealed record State(bool Reachable, bool Loaded, string? Scenario, string? ScenarioName, DateTime? LoadedAt, string? Plan,
        Dictionary<string, int> Counts, List<Scenario> Scenarios, string? Error);
    public sealed record Outcome(bool Ok, string Message, Dictionary<string, int> Counts, List<string> Skipped, List<string> Logins);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SetTestAccountAsync(int tenantId, bool on)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.FirstAsync(x => x.Id == tenantId);
        t.IsTestAccount = on;
        t.TestAccountSince = on ? DateTime.UtcNow : null;
        db.TenantEvents.Add(new TenantEvent { TenantId = t.Id, EventType = on ? "test_account_on" : "test_account_off", Detail = on ? "Oznaczono jako konto testowe" : "Zdjęto oznaczenie konta testowego" });
        await db.SaveChangesAsync();
    }

    public async Task<State> GetAsync(Tenant tenant)
    {
        try
        {
            using var resp = await Http.SendAsync(await RequestAsync(tenant, HttpMethod.Get, "/internal/demo"));
            if (!resp.IsSuccessStatusCode) return Unreachable($"Instancja odpowiedziała {(int)resp.StatusCode} — może działać starsza wersja aplikacji.");
            var s = await resp.Content.ReadFromJsonAsync<RemoteState>(Json);
            return s is null ? Unreachable("Pusta odpowiedź instancji.")
                : new State(true, s.Loaded, s.Scenario, s.ScenarioName, s.LoadedAt, s.Plan, s.Counts ?? [], s.Scenarios ?? [], null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Unreachable("Nie udało się połączyć z instancją: " + ex.Message);
        }
    }

    public async Task<Outcome> LoadAsync(int tenantId, string scenario)
    {
        var tenant = await TenantAsync(tenantId);
        if (tenant is null) return Fail("Nie znaleziono trenera.");
        if (!tenant.IsTestAccount) return Fail("Dane demo można ładować tylko na koncie testowym.");
        return await PostAsync(tenant, $"/internal/demo/load?scenario={Uri.EscapeDataString(scenario)}", $"demo_loaded:{scenario}");
    }

    public async Task<Outcome> ClearAsync(int tenantId)
    {
        var tenant = await TenantAsync(tenantId);
        if (tenant is null) return Fail("Nie znaleziono trenera.");
        return await PostAsync(tenant, "/internal/demo/clear", "demo_cleared");
    }

    private async Task<Outcome> PostAsync(Tenant tenant, string path, string eventType)
    {
        try
        {
            using var resp = await Http.SendAsync(await RequestAsync(tenant, HttpMethod.Post, path));
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Fail("Instancja nie obsługuje jeszcze danych demo — zaktualizuj aplikację trenera.");
            var r = await resp.Content.ReadFromJsonAsync<RemoteOutcome>(Json);
            var outcome = new Outcome(r?.Ok == true, r?.Message ?? "Brak odpowiedzi.", r?.Counts ?? [], r?.Skipped ?? [], r?.Logins ?? []);
            await using var db = await dbFactory.CreateDbContextAsync();
            db.TenantEvents.Add(new TenantEvent { TenantId = tenant.Id, EventType = eventType, Detail = outcome.Message });
            await db.SaveChangesAsync();
            return outcome;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Dane demo: błąd wywołania {Path} u {Slug}.", path, tenant.Slug);
            return Fail("Nie udało się połączyć z instancją: " + ex.Message);
        }
    }

    private async Task<HttpRequestMessage> RequestAsync(Tenant tenant, HttpMethod method, string path)
    {
        var secret = TenantSecrets.For(tenant, config);
        if (string.IsNullOrEmpty(secret)) throw new InvalidOperationException("instancja nie ma sekretu — zreprowizjonuj ją.");
        var baseUrl = await TenantEndpoint.BaseUrlAsync(config, tenant) ?? throw new InvalidOperationException("instancja nie odpowiada.");
        var req = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
        req.Headers.Add("X-Internal-Secret", secret);
        return req;
    }

    private async Task<Tenant?> TenantAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    private static State Unreachable(string error) => new(false, false, null, null, null, null, [], [], error);
    private static Outcome Fail(string message) => new(false, message, [], [], []);

    private sealed record RemoteState(bool Loaded, string? Scenario, string? ScenarioName, DateTime? LoadedAt, Dictionary<string, int>? Counts, string? Plan, List<Scenario>? Scenarios);
    private sealed record RemoteOutcome(bool Ok, string? Message, Dictionary<string, int>? Counts, List<string>? Skipped, List<string>? Logins);
}
