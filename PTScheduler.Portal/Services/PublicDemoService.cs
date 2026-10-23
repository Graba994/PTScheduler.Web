using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Publiczne demo na hantly.pl: jedno konto testowe służy jako pokaz dla odwiedzających. Włącznik i ustawienia są
/// w Panelu (Strona główna → Publiczne demo). Włączenie przełącza instancję w tryb demo (bez e-maili, SMS-ów, push,
/// płatności i KSeF, z banerem i zablokowanymi stronami konta) i wczytuje dane; co noc dane wracają do początku.
/// Gość wchodzi przez /demo (jednorazowy token jak przy wejściu administratora, ale tylko na konto trenera albo klienta).
/// </summary>
public class PublicDemoService(
    SiteSettingsService settings,
    TenantDemoService demo,
    TenantService tenants,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    ILogger<PublicDemoService> logger)
{
    public const string DefaultScenario = "pelny";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record Config(bool Enabled, int? TenantId, string Scenario, int? ResetHour, DateTime? LastReset);

    public async Task<Config> GetConfigAsync()
    {
        var s = await settings.GetAllAsync(SiteSettingsService.Keys.DemoEnabled, SiteSettingsService.Keys.DemoTenantId,
            SiteSettingsService.Keys.DemoScenario, SiteSettingsService.Keys.DemoResetHour, SiteSettingsService.Keys.DemoLastReset);
        string V(string k) => s.TryGetValue(k, out var v) ? v : "";
        var hour = V(SiteSettingsService.Keys.DemoResetHour);
        return new Config(
            V(SiteSettingsService.Keys.DemoEnabled) == "true",
            int.TryParse(V(SiteSettingsService.Keys.DemoTenantId), out var id) ? id : null,
            V(SiteSettingsService.Keys.DemoScenario) is { Length: > 0 } sc ? sc : DefaultScenario,
            hour == "off" ? null : int.TryParse(hour, out var h) && h is >= 0 and <= 23 ? h : 3,
            DateTime.TryParse(V(SiteSettingsService.Keys.DemoLastReset), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lr) ? lr : null);
    }

    /// <summary>Konta testowe, które mogą być demo (aktywne).</summary>
    public async Task<List<Tenant>> CandidatesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking().Where(t => t.IsTestAccount && t.Status == TenantStatus.Active).OrderBy(t => t.CompanyName).ToListAsync();
    }

    public async Task SaveAsync(int? tenantId, string scenario, int? resetHour)
    {
        await settings.SetAsync(SiteSettingsService.Keys.DemoTenantId, tenantId?.ToString(CultureInfo.InvariantCulture) ?? "");
        await settings.SetAsync(SiteSettingsService.Keys.DemoScenario, string.IsNullOrWhiteSpace(scenario) ? DefaultScenario : scenario);
        await settings.SetAsync(SiteSettingsService.Keys.DemoResetHour, resetHour?.ToString(CultureInfo.InvariantCulture) ?? "off");
    }

    /// <summary>Włącza albo wyłącza demo: najpierw tryb pokazowy w instancji, potem (przy włączaniu) świeże dane.</summary>
    public async Task<(bool Ok, string Message)> SetEnabledAsync(bool on, string registerUrl)
    {
        var cfg = await GetConfigAsync();
        var tenant = cfg.TenantId is { } id ? await TenantAsync(id) : null;
        if (on && tenant is null) return (false, "Wybierz konto testowe, które ma służyć jako demo.");
        if (on && !tenant!.IsTestAccount) return (false, "Demo może działać tylko na koncie oznaczonym jako testowe.");

        if (tenant is not null)
        {
            var (ok, msg) = await SetModeAsync(tenant, on, registerUrl);
            if (!ok && on) return (false, msg);
        }
        if (on)
        {
            var state = await demo.GetAsync(tenant!);
            if (!state.Loaded)
            {
                var load = await demo.LoadAsync(tenant!.Id, cfg.Scenario);
                if (!load.Ok) return (false, "Tryb demo włączony, ale dane się nie wczytały: " + load.Message);
                await settings.SetAsync(SiteSettingsService.Keys.DemoLastReset, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            }
        }
        await settings.SetAsync(SiteSettingsService.Keys.DemoEnabled, on ? "true" : "false");
        logger.LogInformation("Publiczne demo {Stan} ({Slug}).", on ? "włączone" : "wyłączone", tenant?.Slug ?? "—");
        return (true, on ? "Demo działa — przycisk „Wypróbuj demo” jest już na stronie głównej." : "Demo wyłączone — przycisk zniknął ze strony głównej.");
    }

    /// <summary>Dane demo od nowa (scenariusz wczytany jeszcze raz — daty względem dzisiaj).</summary>
    public async Task<(bool Ok, string Message)> ResetAsync()
    {
        var cfg = await GetConfigAsync();
        if (cfg.TenantId is not { } id) return (false, "Nie wybrano konta demo.");
        var r = await demo.LoadAsync(id, cfg.Scenario);
        if (r.Ok) await settings.SetAsync(SiteSettingsService.Keys.DemoLastReset, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        return (r.Ok, r.Ok ? "Dane demo przywrócone." : r.Message);
    }

    /// <summary>Adres wejścia gościa (null, gdy demo jest wyłączone albo instancja nie ma sekretu).</summary>
    public async Task<string?> EntryUrlAsync(bool asClient)
    {
        var cfg = await GetConfigAsync();
        if (!cfg.Enabled || cfg.TenantId is not { } id) return null;
        var tenant = await TenantAsync(id);
        if (tenant is null || tenant.Status != TenantStatus.Active) return null;
        var secret = TenantSecrets.For(tenant, config);
        if (string.IsNullOrWhiteSpace(secret)) return null;
        var token = TenantSso.CreateToken(tenant.Slug, "demo", asClient ? "demo-client" : "demo-trainer", secret, DateTimeOffset.UtcNow);
        return TenantSso.DemoUrl(await tenants.PublicUrlAsync(tenant), token);
    }

    /// <summary>Czy pora na nocny reset (czas polski, raz na dobę).</summary>
    public static bool ResetDue(Config cfg, DateTime utcNow, TimeZoneInfo tz)
    {
        if (!cfg.Enabled || cfg.ResetHour is not { } hour || cfg.TenantId is null) return false;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        if (local.Hour != hour) return false;
        return cfg.LastReset is not { } last || TimeZoneInfo.ConvertTimeFromUtc(last, tz).Date < local.Date;
    }

    private async Task<(bool Ok, string Message)> SetModeAsync(Tenant tenant, bool on, string registerUrl)
    {
        try
        {
            var secret = TenantSecrets.For(tenant, config);
            var baseUrl = await TenantEndpoint.BaseUrlAsync(config, tenant);
            if (string.IsNullOrEmpty(secret) || baseUrl is null) return (false, "Instancja nie odpowiada albo nie ma sekretu.");
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"{baseUrl.TrimEnd('/')}/internal/demo/mode?on={(on ? "true" : "false")}&registerUrl={Uri.EscapeDataString(registerUrl)}");
            req.Headers.Add("X-Internal-Secret", secret);
            using var resp = await Http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return (false, "Instancja nie obsługuje jeszcze trybu demo — zaktualizuj aplikację trenera.");
            if (!resp.IsSuccessStatusCode) return (false, $"Instancja odpowiedziała {(int)resp.StatusCode}.");
            await using var db = await dbFactory.CreateDbContextAsync();
            db.TenantEvents.Add(new TenantEvent { TenantId = tenant.Id, EventType = on ? "public_demo_on" : "public_demo_off", Detail = on ? "Włączono publiczne demo" : "Wyłączono publiczne demo" });
            await db.SaveChangesAsync();
            return (true, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Publiczne demo: nie udało się przełączyć trybu u {Slug}.", tenant.Slug);
            return (false, "Nie udało się połączyć z instancją: " + ex.Message);
        }
    }

    private async Task<Tenant?> TenantAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }
}

/// <summary>Co 10 minut: czy pora na nocny reset publicznego demo.</summary>
public class PublicDemoResetJob(IServiceScopeFactory scopes, ILogger<PublicDemoResetJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); } catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<PublicDemoService>();
                if (PublicDemoService.ResetDue(await svc.GetConfigAsync(), DateTime.UtcNow, tz))
                {
                    var (ok, msg) = await svc.ResetAsync();
                    logger.LogInformation("Nocny reset publicznego demo: {Wynik}", ok ? "gotowe" : msg);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Nocny reset publicznego demo nie powiódł się.");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
