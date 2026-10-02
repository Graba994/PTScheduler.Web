using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Uruchamia aplikację trenera po „Opublikuj” w kreatorze — w tle, niezależnie od połączenia
/// przeglądarki (zamknięcie karty nie przerywa uruchamiania). Stan etapów trzyma w pamięci,
/// a strona kreatora pokazuje go na żywo. Pilnuje też limitu rejestracji z jednego adresu IP.
/// </summary>
public class AppLaunchService(IServiceScopeFactory scopes, ILogger<AppLaunchService> logger)
{
    public enum StepState { Waiting, Running, Done, Skipped, Failed }

    public sealed class Step(string key, string label)
    {
        public string Key { get; } = key;
        public string Label { get; } = label;
        public StepState State { get; set; }
    }

    public sealed class Launch
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int TenantId { get; init; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        public List<Step> Steps { get; } =
        [
            new(TenantService.ProvisionSteps.Database, "Tworzę bazę danych"),
            new(TenantService.ProvisionSteps.App, "Uruchamiam aplikację"),
            new(TenantService.ProvisionSteps.Health, "Pierwszy start i przygotowanie bazy"),
            new(TenantService.ProvisionSteps.Setup, "Wgrywam kolor, stronę i ofertę"),
            new(TenantService.ProvisionSteps.Domain, "Ustawiam adres i certyfikat HTTPS"),
        ];
        public bool Done => FinishedAt is not null;
        public bool Failed { get; set; }
        /// <summary>Aplikacja działa, ale ustawień z kreatora nie wgrano — trener dokończy konfigurację w aplikacji.</summary>
        public bool SetupFailed { get; set; }
        public string? Error { get; set; }
        /// <summary>Adres aplikacji (bez tokenu) i link „wejdź” z jednorazowym tokenem.</summary>
        public string? AppUrl { get; set; }
        public string? EnterUrl { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, Launch> _launches = new();
    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts = new();

    /// <summary>Po każdej zmianie etapu — strony kreatora odświeżają widok.</summary>
    public event Action<Guid>? Changed;

    public Launch? Get(Guid id) => _launches.GetValueOrDefault(id);

    /// <summary>Najwyżej 3 publikacje na godzinę z jednego adresu (ochrona przed zakładaniem instancji hurtem).</summary>
    public bool TryRegisterAttempt(string? ip)
    {
        var key = string.IsNullOrWhiteSpace(ip) ? "?" : ip;
        var now = DateTime.UtcNow;
        var list = _attempts.GetOrAdd(key, _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t > TimeSpan.FromHours(1));
            if (list.Count >= 3) return false;
            list.Add(now);
            return true;
        }
    }

    public Launch Start(int tenantId, string? welcomeToken)
    {
        var launch = new Launch { TenantId = tenantId };
        _launches[launch.Id] = launch;
        foreach (var old in _launches.Values.Where(l => l.Done && DateTime.UtcNow - l.StartedAt > TimeSpan.FromHours(2)).ToList())
            _launches.TryRemove(old.Id, out _);

        _ = Task.Run(() => RunAsync(launch, welcomeToken));
        return launch;
    }

    private void Mark(Launch launch, string key)
    {
        if (key == TenantService.ProvisionSteps.SetupFailed)
        {
            launch.SetupFailed = true;
            var setup = launch.Steps.First(s => s.Key == TenantService.ProvisionSteps.Setup);
            setup.State = StepState.Failed;
            Notify(launch);
            return;
        }
        var idx = launch.Steps.FindIndex(s => s.Key == key);
        if (idx < 0) return;
        for (var i = 0; i < idx; i++)
            if (launch.Steps[i].State is StepState.Waiting or StepState.Running && launch.Steps[i].State != StepState.Failed)
                launch.Steps[i].State = launch.Steps[i].State == StepState.Running ? StepState.Done : StepState.Skipped;
        launch.Steps[idx].State = StepState.Running;
        Notify(launch);
    }

    private void Notify(Launch launch)
    {
        try { Changed?.Invoke(launch.Id); } catch { /* strona mogła się zamknąć */ }
    }

    private async Task RunAsync(Launch launch, string? welcomeToken)
    {
        using var scope = scopes.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantService>();
        var email = scope.ServiceProvider.GetRequiredService<EmailService>();
        var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();

        var (ok, output) = (false, "");
        try
        {
            (ok, output) = await tenants.ProvisionAsync(launch.TenantId, key => Mark(launch, key));
        }
        catch (Exception ex)
        {
            output = ex.Message;
            logger.LogError(ex, "Uruchamianie instancji {TenantId} z kreatora nie powiodło się.", launch.TenantId);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == launch.TenantId);
        if (tenant is not null) launch.AppUrl = await tenants.PublicUrlAsync(tenant);

        foreach (var s in launch.Steps)
            if (s.State == StepState.Running) s.State = ok ? StepState.Done : StepState.Failed;
            else if (s.State == StepState.Waiting) s.State = ok ? StepState.Skipped : StepState.Waiting;
        launch.Failed = !ok;
        launch.Error = ok ? null : "Nie udało się uruchomić aplikacji automatycznie. Zgłoszenie jest zapisane — dokończymy je i damy znać e-mailem.";
        launch.EnterUrl = ok && launch.AppUrl is not null && welcomeToken is not null && !launch.SetupFailed
            ? $"{launch.AppUrl}/account/welcome?token={Uri.EscapeDataString(welcomeToken)}"
            : launch.AppUrl;
        launch.FinishedAt = DateTime.UtcNow;
        Notify(launch);

        if (tenant is null) return;
        var planName = tenant.Plan?.Name ?? tenant.PlanId;
        var (first, _) = TenantSetupPayload.SplitName(tenant.OwnerName);
        try
        {
            if (ok)
                await email.SendAsync(tenant.OwnerEmail, $"{tenant.CompanyName} — Twoja aplikacja działa",
                    email.AppReadyEmailBody(first ?? tenant.OwnerName, tenant.CompanyName, launch.AppUrl!, tenant.OwnerEmail, planName));
            var adminEmail = await settings.GetAsync(SiteSettingsService.Keys.AdminNotificationEmail);
            if (!string.IsNullOrWhiteSpace(adminEmail))
                await email.SendAsync(adminEmail, ok ? $"Nowy trener: {tenant.CompanyName}" : $"Rejestracja do dokończenia: {tenant.CompanyName}",
                    email.AdminAppLaunchedEmailBody(tenant.OwnerName, tenant.OwnerEmail, tenant.CompanyName, planName, tenant.Phone, launch.AppUrl ?? "", ok, output));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Nie wysłano e-maili po uruchomieniu instancji {Slug}.", tenant.Slug); }
    }
}
