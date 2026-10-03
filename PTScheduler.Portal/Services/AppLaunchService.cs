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
        /// <summary>Uruchomienie się nie udało (po 3 próbach) — zgłoszenie czeka w kolejce, admin dostał powód.</summary>
        public bool Failed { get; set; }
        /// <summary>Która próba (1–3) — przy ponawianiu kreator pokazuje „Próbuję jeszcze raz”.</summary>
        public int Attempt { get; set; } = 1;
        /// <summary>Zbudowane przez admina dla trenera — trener dostaje e-mail z ustawieniem hasła.</summary>
        public bool BuiltByAdmin { get; init; }
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

    /// <summary>Ostatnie uruchamianie danego zgłoszenia (np. po odświeżeniu strony kreatora).</summary>
    public Launch? FindByTenant(int tenantId) =>
        _launches.Values.Where(l => l.TenantId == tenantId).OrderByDescending(l => l.StartedAt).FirstOrDefault();

    /// <summary>Najwyżej 5 publikacji na godzinę z jednego adresu (ochrona przed zakładaniem instancji hurtem).</summary>
    public bool TryRegisterAttempt(string? ip)
    {
        var key = string.IsNullOrWhiteSpace(ip) ? "?" : ip;
        var now = DateTime.UtcNow;
        var list = _attempts.GetOrAdd(key, _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t > TimeSpan.FromHours(1));
            if (list.Count >= 5) return false;
            list.Add(now);
            return true;
        }
    }

    /// <param name="autoLaunchTag">„limit” albo „invite” — zapis do dziennego limitu automatycznych uruchomień.</param>
    public Launch Start(int tenantId, string? welcomeToken, string? autoLaunchTag = null, bool builtByAdmin = false)
    {
        var launch = new Launch { TenantId = tenantId, BuiltByAdmin = builtByAdmin };
        if (autoLaunchTag is not null) _ = RecordAutoLaunchAsync(tenantId, autoLaunchTag);
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

    private async Task RecordAutoLaunchAsync(int tenantId, string tag)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>().CreateDbContextAsync();
            db.TenantEvents.Add(new Entities.TenantEvent { TenantId = tenantId, EventType = Entities.TenantEventTypes.AutoLaunched, Detail = tag });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "Nie zapisano automatycznego uruchomienia {TenantId}.", tenantId); }
    }

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(45)];

    private async Task RunAsync(Launch launch, string? welcomeToken)
    {
        using var scope = scopes.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<TenantService>();
        var email = scope.ServiceProvider.GetRequiredService<EmailService>();
        var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();

        // Do 3 prób: chwilowy problem z Dockerem czy bazą nie powinien kończyć się kolejką.
        var (ok, output) = (false, "");
        for (var attempt = 1; attempt <= 3 && !ok; attempt++)
        {
            launch.Attempt = attempt;
            if (attempt > 1)
            {
                foreach (var st in launch.Steps) st.State = StepState.Waiting;
                launch.SetupFailed = false;
                Notify(launch);
                await Task.Delay(RetryDelays[attempt - 2]);
            }
            try
            {
                (ok, output) = await tenants.ProvisionAsync(launch.TenantId, key => Mark(launch, key));
            }
            catch (Exception ex)
            {
                output = ex.Message;
                logger.LogError(ex, "Uruchamianie instancji {TenantId} z kreatora (próba {Attempt}) nie powiodło się.", launch.TenantId, attempt);
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var tenant = await db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == launch.TenantId);
        if (tenant is not null) launch.AppUrl = await tenants.PublicUrlAsync(tenant);

        // Sprawdzenie po starcie: strona logowania musi odpowiadać, zanim pokażemy „Gotowe”.
        if (ok && tenant is not null && !await SmokeTestAsync(config, tenant))
        {
            launch.SetupFailed = true;
            output += "\nSprawdzenie po starcie: strona logowania nie odpowiada.";
        }

        if (ok && tenant is not null)
        {
            try { await scope.ServiceProvider.GetRequiredService<StripeService>().RestartTrialAsync(tenant.Id); }
            catch (Exception ex) { logger.LogWarning(ex, "Okres próbny {Slug} nie przesunięty.", tenant.Slug); }
            // Rozliczenie rachunkami (płatność weryfikacyjna przez polską bramkę): okres próbny od dziś, potem rachunki.
            if (tenant.StripeSubscriptionId is null && tenant.RegistrationPaidAt is not null && tenant.Plan is { MonthlyPrice: > 0 } plan)
            {
                var extra = string.IsNullOrWhiteSpace(tenant.InviteCode) ? 0
                    : await db.InviteCodes.Where(c => c.Code == tenant.InviteCode).Select(c => c.ExtraTrialDays).FirstOrDefaultAsync();
                var days = Math.Max(0, plan.TrialDays) + Math.Max(0, extra);
                tenant.TrialEndsAt = days > 0 ? DateTime.UtcNow.AddDays(days) : null;
                await db.SaveChangesAsync();
            }
        }

        if (!ok && tenant is not null)
        {
            tenant.QueuedReason = $"Uruchomienie nie powiodło się po 3 próbach: {Short(output)}";
            await db.SaveChangesAsync();
        }

        foreach (var s in launch.Steps)
            if (s.State == StepState.Running) s.State = ok ? StepState.Done : StepState.Failed;
            else if (s.State == StepState.Waiting) s.State = ok ? StepState.Skipped : StepState.Waiting;
        launch.Failed = !ok;
        launch.Error = ok ? null : "Twoja aplikacja jest gotowa i czeka w kolejce do uruchomienia.";
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
            if (ok && launch.BuiltByAdmin)
                await email.SendAsync(tenant.OwnerEmail, $"{tenant.CompanyName} — Twoja aplikacja czeka",
                    email.AppBuiltForYouEmailBody(first ?? tenant.OwnerName, tenant.CompanyName, launch.AppUrl!, planName));
            else if (ok)
                await email.SendAsync(tenant.OwnerEmail, $"{tenant.CompanyName} — Twoja aplikacja działa",
                    email.AppReadyEmailBody(first ?? tenant.OwnerName, tenant.CompanyName, launch.AppUrl!, tenant.OwnerEmail, planName));
            else
                await email.SendAsync(tenant.OwnerEmail, $"{tenant.CompanyName} — aplikacja w kolejce do uruchomienia",
                    email.AppQueuedEmailBody(first ?? tenant.OwnerName, tenant.CompanyName));
            var adminEmail = await settings.GetAsync(SiteSettingsService.Keys.AdminNotificationEmail);
            if (!string.IsNullOrWhiteSpace(adminEmail))
                await email.SendAsync(adminEmail, ok ? $"Nowy trener: {tenant.CompanyName}" : $"Rejestracja do dokończenia: {tenant.CompanyName}",
                    email.AdminAppLaunchedEmailBody(tenant.OwnerName, tenant.OwnerEmail, tenant.CompanyName, planName, tenant.Phone, launch.AppUrl ?? "", ok, output));
        }
        catch (Exception ex) { logger.LogWarning(ex, "Nie wysłano e-maili po uruchomieniu instancji {Slug}.", tenant.Slug); }
    }

    private static readonly HttpClient SmokeClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<bool> SmokeTestAsync(IConfiguration config, Entities.Tenant tenant)
    {
        for (var i = 0; i < 6; i++)
        {
            try
            {
                var probe = await TenantEndpoint.ProbeAsync(config, tenant);
                if (probe.Ok && probe.Base is not null)
                {
                    using var resp = await SmokeClient.GetAsync($"{probe.Base}/Account/Login");
                    if (resp.IsSuccessStatusCode) return true;
                }
            }
            catch (Exception) { /* jeszcze wstaje */ }
            await Task.Delay(5000);
        }
        return false;
    }

    private static string Short(string s)
    {
        var line = s.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? s;
        return line.Length > 300 ? line[..300] + "…" : line;
    }
}
