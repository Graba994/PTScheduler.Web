using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Bramki publikacji z kreatora: czy aplikacja trenera uruchamia się od razu, czy czeka w kolejce
/// na admina. Uruchamiamy od razu, gdy karta jest podpięta (pilnuje tego kreator) i: jest ważny kod
/// zaproszenia albo — przy trybie „uruchamiaj od razu” — nie przekroczono dziennego limitu. Zawsze
/// sprawdzamy też serwer (Docker, obraz, miejsce), żeby zamiast błędu trener zobaczył kolejkę.
/// </summary>
public class RegistrationGateService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    DockerService docker,
    ResourceReportService resources,
    IConfiguration config,
    ILogger<RegistrationGateService> logger)
{
    public const int DefaultDailyLimit = 10;

    public sealed record Decision(bool Launch, string? QueueReason);

    private string TenantImage => config.GetValue<string>("Portal:TenantImage") ?? "ptscheduler-web:latest";

    /// <summary>Popularne skrzynki jednorazowe — rejestracja z nich nie ma sensu.</summary>
    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "10minutemail.com", "10minutemail.net", "temp-mail.org", "tempmail.com", "tempmail.net", "guerrillamail.com",
        "guerrillamail.net", "sharklasers.com", "mailinator.com", "yopmail.com", "yopmail.fr", "trashmail.com",
        "getnada.com", "nada.email", "dispostable.com", "maildrop.cc", "throwawaymail.com", "fakeinbox.com",
        "mintemail.com", "mohmal.com", "emailondeck.com", "tempinbox.com", "spamgourmet.com", "mytemp.email",
        "tempr.email", "discard.email", "burnermail.io", "inboxkitten.com", "minuteinbox.com", "tmail.ws"
    };

    public static bool IsDisposableEmail(string email)
    {
        var at = email.LastIndexOf('@');
        return at > 0 && DisposableDomains.Contains(email[(at + 1)..].Trim());
    }

    public async Task<int> DailyLimitAsync() =>
        int.TryParse(await settings.GetAsync(SiteSettingsService.Keys.RegistrationDailyLimit), out var n) && n >= 0 ? n : DefaultDailyLimit;

    /// <summary>Ile aplikacji uruchomiło się dziś automatycznie (bez kodu zaproszenia).</summary>
    public async Task<int> AutoLaunchesTodayAsync()
    {
        var since = DateTime.UtcNow.Date;
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.TenantEvents.CountAsync(e => e.EventType == TenantEventTypes.AutoLaunched && e.OccurredAt >= since && e.Detail == "limit");
    }

    public async Task<InviteCode?> FindInviteAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim().ToUpperInvariant();
        await using var db = await dbFactory.CreateDbContextAsync();
        var invite = await db.InviteCodes.AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized);
        return invite is not null && invite.IsUsable(DateTime.UtcNow) ? invite : null;
    }

    public async Task ConsumeInviteAsync(string code)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invite = await db.InviteCodes.FirstOrDefaultAsync(c => c.Code == code.Trim().ToUpperInvariant());
        if (invite is null) return;
        invite.Uses++;
        await db.SaveChangesAsync();
    }

    /// <summary>Czy uruchamiać teraz. Powód kolejki trafia do Panelu (Trenerzy → „Czeka”).</summary>
    public async Task<Decision> DecideAsync(Tenant tenant)
    {
        var invite = await FindInviteAsync(tenant.InviteCode);
        if (invite is null)
        {
            if (await settings.GetAsync(SiteSettingsService.Keys.RegistrationMode) == "review")
                return new(false, "Tryb „czekaj na moją akceptację” (Panel → Strona publiczna).");
            var limit = await DailyLimitAsync();
            if (await AutoLaunchesTodayAsync() >= limit)
                return new(false, $"Dzienny limit automatycznych uruchomień ({limit}) wyczerpany.");
        }

        var (ok, reason) = await PreflightAsync();
        return ok ? new(true, null) : new(false, reason);
    }

    /// <summary>Test serwera przed uruchomieniem: Docker odpowiada, obraz aplikacji jest, jest miejsce.</summary>
    public async Task<(bool Ok, string? Reason)> PreflightAsync()
    {
        try
        {
            await docker.GetSystemInfoAsync().WaitAsync(TimeSpan.FromSeconds(8));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Test przed uruchomieniem: Docker nie odpowiada.");
            return (false, "Docker nie odpowiada — sprawdź Guardiana i gniazdo Dockera.");
        }
        if (!await docker.ImageExistsAsync(TenantImage))
            return (false, $"Brak obrazu aplikacji „{TenantImage}” na serwerze — zbuduj go albo zaktualizuj w Panelu → Aktualizacje.");
        try
        {
            var report = await resources.BuildAsync(ResourceReportService.Period.Day).WaitAsync(TimeSpan.FromSeconds(10));
            if (report.MoreTenants is 0)
                return (false, "Na serwerze nie ma już miejsca na kolejnego trenera (Panel → Zasoby).");
        }
        catch (Exception ex) { logger.LogDebug(ex, "Raport zasobów niedostępny — pomijamy sprawdzenie miejsca."); }
        return (true, null);
    }

    /// <summary>Zapisuje powód kolejki przy zgłoszeniu (widać go w Panelu).</summary>
    public async Task QueueAsync(int tenantId, string reason)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.FindAsync(tenantId);
        if (t is null) return;
        t.QueuedReason = reason;
        await db.SaveChangesAsync();
    }

    /// <summary>Nowy jednorazowy token „Wejdź do aplikacji” — jego skrót trafia do danych z kreatora tuż przed startem.</summary>
    public async Task<string?> PrepareWelcomeTokenAsync(int tenantId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var t = await db.Tenants.FindAsync(tenantId);
        var payload = TenantSetupPayload.FromJson(t?.SetupPayload);
        if (t is null || payload is null || string.IsNullOrEmpty(payload.PasswordHash)) return null;
        var (token, hash) = TenantSetupPayload.NewWelcomeToken();
        payload.WelcomeTokenHash = hash;
        payload.WelcomeTokenExpiresUtc = DateTime.UtcNow.AddMinutes(45);
        t.SetupPayload = payload.ToJson();
        t.QueuedReason = null;
        await db.SaveChangesAsync();
        return token;
    }
}
