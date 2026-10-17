using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public class TenantService(
    IDbContextFactory<PortalDbContext> dbFactory,
    DockerService docker,
    SiteSettingsService settings,
    NpmService npm,
    BunnyPlatformService bunny,
    IConfiguration config,
    ILogger<TenantService> logger)
{
    private string TenantImage => config.GetValue<string>("Portal:TenantImage") ?? "ptscheduler-web:latest";
    private string ForwardHost => config.GetValue<string>("Portal:ForwardHost") ?? "192.168.0.220";
    private string PortalUrl => config.GetValue<string>("Portal:PublicUrl") ?? $"http://{ForwardHost}:8081";

    private static readonly HttpClient PushClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public async Task<List<Tenant>> GetAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Tenants
            .AsNoTracking()
            .Include(t => t.Plan)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Tenant?> GetAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Tenants
            .AsNoTracking()
            .Include(t => t.Plan)
            .Include(t => t.Subscriptions.OrderByDescending(s => s.CreatedAt).Take(1))
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Tenant?> GetBySlugAsync(string slug)
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    }

    public async Task<Tenant> CreateAsync(string slug, string domain, string companyName,
        string ownerName, string ownerEmail, string? phone, string planId, string? setupMode, string? setupPayload = null)
    {
        await using var db = dbFactory.CreateDbContext();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
            throw new InvalidOperationException($"Tenant '{slug}' already exists.");

        const int basePort = 9001;
        var portalPort = config.GetValue<int?>("Portal:PortalPort") ?? 8081;
        var maxPort = await db.Tenants.MaxAsync(t => (int?)t.Port) ?? (basePort - 1);
        var port = Math.Max(maxPort + 1, basePort);
        while (port == portalPort) port++;

        var dbPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .Replace("/", "").Replace("+", "").Replace("=", "");
        if (dbPassword.Length > 32) dbPassword = dbPassword[..32];

        var tenant = new Tenant
        {
            Slug = slug,
            Domain = domain,
            Port = port,
            CompanyName = companyName,
            OwnerName = ownerName,
            OwnerEmail = ownerEmail,
            Phone = phone,
            DbPassword = dbPassword,
            PlanId = planId,
            SetupMode = setupMode,
            SetupPayload = setupPayload,
            Status = TenantStatus.Pending,
            WebContainerName = $"pt-{slug}-web",
            DbContainerName = $"pt-{slug}-db"
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.Created,
            Detail = $"Plan: {planId}, domena: {domain}"
        });
        await db.SaveChangesAsync();

        return tenant;
    }

    /// <summary>Etapy uruchamiania instancji — kreator rejestracji pokazuje je na żywo.</summary>
    public static class ProvisionSteps
    {
        public const string Database = "db";
        public const string App = "web";
        public const string Health = "health";
        public const string Setup = "setup";
        public const string Domain = "domain";
        /// <summary>Instancja działa, ale danych z kreatora nie udało się przekazać (trener dokończy w /setup).</summary>
        public const string SetupFailed = "setup-failed";
    }

    private static readonly HttpClient BootstrapClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<(bool Success, string Output)> ProvisionAsync(int tenantId, Action<string>? onStep = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        tenant.Status = TenantStatus.Provisioning;
        await db.SaveChangesAsync();

        try
        {
            if (!await docker.ImageExistsAsync(TenantImage))
                throw new InvalidOperationException(
                    $"Image '{TenantImage}' not found on the Docker host. " +
                    "Build it first: docker build -t ptscheduler-web:latest <path-to-main-app-repo>");

            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tenant.PlanId);
            var entitlementsJson = plan is null ? null : SerializePlan(plan);

            // Każda nowa instancja dostaje własny sekret wywołań Portal ↔ tenant.
            if (string.IsNullOrEmpty(tenant.InternalSecret))
            {
                tenant.InternalSecret = TenantSecrets.New();
                await db.SaveChangesAsync();
            }

            await docker.ProvisionTenantAsync(
                tenant.Slug,
                tenant.DbPassword,
                tenant.Port,
                TenantImage,
                tenant.Domain,
                entitlementsJson,
                PortalUrl,
                tenant.InternalSecret,
                onStep);

            tenant.Status = TenantStatus.Active;
            tenant.ProvisionedAt = DateTime.UtcNow;

            db.TenantEvents.Add(new TenantEvent
            {
                TenantId = tenant.Id,
                EventType = TenantEventTypes.Provisioned,
                Detail = $"Port: {tenant.Port}, image: {TenantImage}"
            });

            await db.SaveChangesAsync();

            var output = $"Tenant '{tenant.Slug}' provisioned on port {tenant.Port}";

            // Dane z rejestracji (kreator albo formularz admina) od razu do aplikacji — bez drugiego /setup.
            onStep?.Invoke(ProvisionSteps.Health);
            var (setupOk, setupMsg) = await BootstrapAsync(tenant, onStep);
            output += "\nKonfiguracja: " + setupMsg;
            if (!setupOk) onStep?.Invoke(ProvisionSteps.SetupFailed);
            if (setupOk && tenant.SetupPayload is not null)
            {
                tenant.SetupPayload = null; // skrót hasła nie jest już potrzebny
                await db.SaveChangesAsync();
            }

            var autoReg = await settings.GetAsync(SiteSettingsService.Keys.NpmAutoRegister);
            if (autoReg != "false" && !string.IsNullOrWhiteSpace(tenant.Domain)
                && !string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.NpmUrl)))
            {
                onStep?.Invoke(ProvisionSteps.Domain);
                var (npmOk, npmMsg) = await npm.RegisterProxyHostAsync(tenant.Domain, ForwardHost, tenant.Port);
                output += "\nNPM: " + (npmOk ? npmMsg : $"nie udało się zarejestrować ({npmMsg}) — dodaj ręcznie");
            }

            return (true, output);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioning failed for {Slug}", tenant.Slug);
            tenant.Status = TenantStatus.Pending;
            await db.SaveChangesAsync();
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Czeka, aż świeża instancja odpowie na /health, i przekazuje jej dane z rejestracji.
    /// Aplikacja przy pierwszym starcie robi migracje bazy, więc kilka prób to norma.
    /// </summary>
    private async Task<(bool Ok, string Message)> BootstrapAsync(Tenant tenant, Action<string>? onStep)
    {
        var payload = TenantSetupPayload.FromJson(tenant.SetupPayload) ?? TenantSetupPayload.PrefillFrom(tenant);
        var secret = TenantSecrets.For(tenant, config);
        if (string.IsNullOrEmpty(secret)) return (false, "brak sekretu instancji — dane wpisze trener w /setup");

        await docker.EnsureAttachedToTenantNetworkAsync(tenant.WebContainerName ?? $"pt-{tenant.Slug}-web");
        var deadline = DateTime.UtcNow.AddMinutes(3);
        string? baseUrl = null;
        while (DateTime.UtcNow < deadline)
        {
            var probe = await TenantEndpoint.ProbeAsync(config, tenant);
            if (probe.Ok) { baseUrl = probe.Base; break; }
            await Task.Delay(2000);
        }
        if (baseUrl is null) return (false, "instancja nie odpowiedziała w 3 minuty — dane wpisze trener w /setup");

        onStep?.Invoke(ProvisionSteps.Setup);
        string last = "";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/internal/setup/bootstrap")
                {
                    Content = new StringContent(payload.ToJson(), System.Text.Encoding.UTF8, "application/json")
                };
                req.Headers.Add("X-Internal-Secret", secret);
                using var resp = await BootstrapClient.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    var completed = doc.RootElement.TryGetProperty("completed", out var c) && c.GetBoolean();
                    return (true, completed ? "konto właściciela i ustawienia z kreatora wgrane" : "dane wpisane do /setup");
                }
                // 409 = błąd danych (np. adres konta technicznego) — powtarzanie nic nie da.
                if ((int)resp.StatusCode == 409) return (false, body);
                last = $"HTTP {(int)resp.StatusCode}";
            }
            catch (Exception ex) { last = ex.Message; }
            await Task.Delay(3000);
        }
        logger.LogWarning("Bootstrap instancji {Slug} nie powiódł się: {Error}", tenant.Slug, last);
        return (false, $"nie udało się przekazać danych ({last}) — trener wpisze je w /setup");
    }

    /// <summary>Adres, pod którym trener otworzy swoją aplikację (domena, a bez NPM — host i port).</summary>
    public async Task<string> PublicUrlAsync(Tenant tenant)
    {
        var host = tenant.Domain?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(await settings.GetAsync(SiteSettingsService.Keys.NpmUrl)))
            return (await settings.GetAsync(SiteSettingsService.Keys.NpmAutoSsl) == "false" ? "http://" : "https://") + host;
        return $"http://{ForwardHost}:{tenant.Port}";
    }

    public async Task SuspendAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var webName = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
        var dbName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
        try { await docker.StopContainerAsync(webName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zatrzymać {Container} przy zawieszaniu tenanta {Slug}.", webName, tenant.Slug); }
        try { await docker.StopContainerAsync(dbName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zatrzymać {Container} przy zawieszaniu tenanta {Slug}.", dbName, tenant.Slug); }

        tenant.Status = TenantStatus.Suspended;

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.Suspended
        });

        await db.SaveChangesAsync();
    }

    public async Task ResumeAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var webName = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
        var dbName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
        try { await docker.StartContainerAsync(dbName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się uruchomić {Container} przy wznawianiu tenanta {Slug}.", dbName, tenant.Slug); }
        try { await docker.StartContainerAsync(webName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się uruchomić {Container} przy wznawianiu tenanta {Slug}.", webName, tenant.Slug); }

        tenant.Status = TenantStatus.Active;

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.Resumed
        });

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int tenantId, bool removeContainers)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        if (removeContainers)
        {
            var webName = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
            var dbName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
            try { await docker.StopContainerAsync(webName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zatrzymać {Container} przy usuwaniu tenanta {Slug}.", webName, tenant.Slug); }
            try { await docker.StopContainerAsync(dbName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się zatrzymać {Container} przy usuwaniu tenanta {Slug}.", dbName, tenant.Slug); }
            try { await docker.RemoveContainerAsync(webName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się usunąć {Container} przy usuwaniu tenanta {Slug}.", webName, tenant.Slug); }
            try { await docker.RemoveContainerAsync(dbName); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się usunąć {Container} przy usuwaniu tenanta {Slug}.", dbName, tenant.Slug); }
            await docker.RemoveTenantResourcesAsync(tenant.Slug);

            await bunny.DeleteLibraryAsync(tenant);

            if (!string.IsNullOrWhiteSpace(tenant.Domain))
                try { await npm.DeleteProxyHostByDomainAsync(tenant.Domain); } catch (Exception ex) { logger.LogWarning(ex, "Nie udało się usunąć wpisu proxy dla domeny {Domain} tenanta {Slug}.", tenant.Domain, tenant.Slug); }
        }

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.Deleted,
            Detail = removeContainers ? "Z kontenerami Docker" : "Tylko wpis"
        });

        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();
    }

    public async Task UpdatePlanAsync(int tenantId, string planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");
        var oldPlan = tenant.PlanId;
        tenant.PlanId = planId;

        db.TenantEvents.Add(new TenantEvent
        {
            TenantId = tenant.Id,
            EventType = TenantEventTypes.PlanChanged,
            Detail = $"{oldPlan} -> {planId}"
        });

        await db.SaveChangesAsync();

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is not null) await PushEntitlementsAsync(tenant, plan);
    }

    /// <summary>
    /// Pushes the current plan definition to every tenant on it, so flag changes made in
    /// the Plans panel take effect without a container restart. Best-effort: a tenant that
    /// is down picks the plan up from the portal on its next start (see /api/internal/tenants/{slug}/entitlements).
    /// </summary>
    public async Task<(int Pushed, int Total)> PushPlanToTenantsAsync(string planId)
    {
        await using var db = dbFactory.CreateDbContext();
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is null) return (0, 0);
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.PlanId == planId).ToListAsync();
        var results = await Task.WhenAll(tenants.Select(t => PushEntitlementsAsync(t, plan)));
        return (results.Count(ok => ok), tenants.Count);
    }

    public async Task<bool> PushEntitlementsAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant is null) return false;
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tenant.PlanId);
        return plan is not null && await PushEntitlementsAsync(tenant, plan);
    }

    private async Task<bool> PushEntitlementsAsync(Tenant tenant, Plan plan)
    {
        var secret = TenantSecrets.For(tenant, config);
        if (string.IsNullOrEmpty(secret) || tenant.Port <= 0) return false;
        try
        {
            var baseUrl = await TenantEndpoint.BaseUrlAsync(config, tenant);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/internal/entitlements/reload")
            {
                Content = new StringContent(await EntitlementsJsonAsync(tenant.Id, plan), System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Internal-Secret", secret);
            using var resp = await PushClient.SendAsync(req);
            if (resp.IsSuccessStatusCode) return true;
            logger.LogWarning("Entitlements push to tenant {Slug} returned {Status}", tenant.Slug, (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Entitlements push to tenant {Slug} failed", tenant.Slug);
        }
        return false;
    }

    /// <summary>
    /// Forces the tenant's Postgres role password back in sync with tenant.DbPassword without
    /// touching the web container. Use this to recover a tenant that's stuck on a
    /// "password authentication failed" error after a container was manually recreated or a
    /// stale volume was reused.
    /// </summary>
    public async Task<(bool Success, string Message)> SyncDbPasswordAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var dbContainerName = tenant.DbContainerName ?? $"pt-{tenant.Slug}-db";
        var (ok, error) = await docker.EnsureDbPasswordAsync(dbContainerName, tenant.DbPassword);
        return ok
            ? (true, "Hasło bazy danych zsynchronizowane. Zrestartuj web jeśli nadal widzisz błąd połączenia.")
            : (false, $"Synchronizacja nie powiodła się: {error}");
    }

    /// <summary>
    /// Nadaje instancji własny sekret (albo go rotuje) i wgrywa go do kontenera web razem
    /// z adresem Portalu — kontener jest odtwarzany z tą samą konfiguracją, więc działa
    /// także dla instancji zaimportowanych. Naprawia „HTTP 404” metryk i konta admina
    /// u instancji utworzonych przed wprowadzeniem sekretów.
    /// </summary>
    public async Task<(bool Success, string Message)> SyncInternalSecretAsync(int tenantId, bool rotate = false)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var secret = rotate || string.IsNullOrEmpty(tenant.InternalSecret) ? TenantSecrets.New() : tenant.InternalSecret;
        var container = tenant.WebContainerName ?? $"pt-{tenant.Slug}-web";
        var (ok, error) = await docker.RecreateWithEnvAsync(container, new Dictionary<string, string>
        {
            ["TENANT_INTERNAL_SECRET"] = secret,
            ["TENANT_SLUG"] = tenant.Slug,
            ["PORTAL_URL"] = PortalUrl
        });
        if (!ok) return (false, $"Nie udało się odtworzyć kontenera {container}: {error}");

        tenant.InternalSecret = secret;
        db.TenantEvents.Add(new TenantEvent { TenantId = tenant.Id, EventType = "secret_synced", Detail = rotate ? "rotated" : "synced" });
        await db.SaveChangesAsync();
        return (true, "Sekret zsynchronizowany — kontener uruchamia się ponownie (ok. 30 s).");
    }

    /// <summary>
    /// Nadaje własny sekret każdej instancji, która go jeszcze nie ma (odtwarza jej kontener web).
    /// Dopóki instancja nie ma własnego sekretu, Portal odrzuca jej wywołania.
    /// </summary>
    public async Task<(int Done, List<string> Failed)> SyncMissingSecretsAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var ids = await db.Tenants.AsNoTracking()
            .Where(t => (t.InternalSecret == null || t.InternalSecret == "") && t.Status != TenantStatus.Destroyed && t.Status != TenantStatus.Pending)
            .Select(t => new { t.Id, t.Slug })
            .ToListAsync();
        var done = 0;
        var failed = new List<string>();
        foreach (var t in ids)
        {
            var (ok, message) = await SyncInternalSecretAsync(t.Id);
            if (ok) done++;
            else failed.Add($"{t.Slug}: {message}");
        }
        return (done, failed);
    }

    /// <summary>Nakłada limity pamięci i CPU na kontenery wszystkich instancji (bez restartu).</summary>
    public async Task<(int Done, List<string> Failed)> ApplyResourceLimitsAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var tenants = await db.Tenants.AsNoTracking()
            .Where(t => t.Status != TenantStatus.Destroyed && t.Status != TenantStatus.Pending)
            .Select(t => new { t.Slug, t.WebContainerName, t.DbContainerName })
            .ToListAsync();
        var done = 0;
        var failed = new List<string>();
        foreach (var t in tenants)
        {
            var (webOk, webErr) = await docker.ApplyResourceLimitsAsync(t.WebContainerName ?? $"pt-{t.Slug}-web", database: false);
            var (dbOk, dbErr) = await docker.ApplyResourceLimitsAsync(t.DbContainerName ?? $"pt-{t.Slug}-db", database: true);
            if (webOk && dbOk) done++;
            else failed.Add($"{t.Slug}: {webErr ?? dbErr}");
        }
        return (done, failed);
    }

    public async Task<(bool Success, string Message)> ReprovisionWebAsync(int tenantId)
    {
        await using var db = dbFactory.CreateDbContext();
        var tenant = await db.Tenants.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Tenant not found.");

        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tenant.PlanId);
        if (plan is null) return (false, $"Plan '{tenant.PlanId}' not found.");

        try
        {
            var entitlements = await EntitlementsJsonAsync(tenant.Id, plan);
            if (string.IsNullOrEmpty(tenant.InternalSecret))
            {
                tenant.InternalSecret = TenantSecrets.New();
                await db.SaveChangesAsync();
            }
            await docker.RecreateWebContainerAsync(
                tenant.Slug, tenant.DbPassword, tenant.Port,
                TenantImage, tenant.Domain, entitlements,
                PortalUrl, tenant.InternalSecret);
            return (true, $"Web container '{tenant.WebContainerName}' zrestartowany z nowym planem '{plan.Name}'.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reprovision failed for tenant {Id}", tenantId);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Uprawnienia instancji = plan + aktywne dodatki miesięczne (więcej transferu
    /// i przestrzeni wideo). Wartości „bez limitu” zostają bez zmian.
    /// </summary>
    public static string SerializeEntitlements(Plan plan, ExtraLimits extra)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(SerializePlan(plan))!.AsObject();
        static int Add(int baseValue, int extra) =>
            extra <= 0 || baseValue >= int.MaxValue - extra ? baseValue : baseValue + extra;
        // Limit 0 znaczy „brak” — dodatek GB do planu bez wideo nic nie daje (wideo i tak wyłączone).
        static int AddIfEnabled(int baseValue, int extra) => baseValue <= 0 ? baseValue : Add(baseValue, extra);
        node["maxVideoStorageGB"] = AddIfEnabled(plan.MaxVideoStorageGB, extra.StorageGb);
        node["maxVideoBandwidthGBPerMonth"] = AddIfEnabled(plan.MaxVideoBandwidthGBPerMonth, extra.BandwidthGb);
        node["addonVideoBandwidthGB"] = extra.BandwidthGb;
        node["addonVideoStorageGB"] = extra.StorageGb;
        // SMS z oferty dokładają się do limitu planu; gratisowe albo dokupione SMS-y włączają przypomnienia SMS.
        node["maxSmsPerMonth"] = Add(plan.SmsReminders ? plan.MaxSmsPerMonth : 0, extra.SmsPerMonth);
        node["smsReminders"] = plan.SmsReminders || extra.SmsPerMonth > 0 || extra.HasSmsCredits;
        return node.ToJsonString();
    }

    public async Task<string> EntitlementsJsonAsync(int tenantId, Plan plan)
    {
        await using var db = dbFactory.CreateDbContext();
        return SerializeEntitlements(plan, await AddonService.ExtraLimitsAsync(db, tenantId));
    }

    public static string SerializePlan(Plan plan) =>
        JsonSerializer.Serialize(plan, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        });

    public async Task<Tenant> ImportAsync(string slug, string domain, int port,
        string companyName, string ownerName, string ownerEmail, string? phone, string planId,
        string? webContainerName, string? dbContainerName)
    {
        await using var db = dbFactory.CreateDbContext();

        if (await db.Tenants.AnyAsync(t => t.Slug == slug))
            throw new InvalidOperationException($"Tenant '{slug}' już istnieje.");

        var tenant = new Tenant
        {
            Slug = slug,
            Domain = domain,
            Port = port,
            CompanyName = companyName,
            OwnerName = ownerName,
            OwnerEmail = ownerEmail,
            Phone = phone,
            DbPassword = "imported",
            PlanId = planId,
            SetupMode = "admin",
            Status = TenantStatus.Active,
            ProvisionedAt = DateTime.UtcNow,
            WebContainerName = webContainerName,
            DbContainerName = dbContainerName
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public async Task<DashboardStats> GetStatsAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return new DashboardStats
        {
            TotalTenants = await db.Tenants.CountAsync(),
            ActiveTenants = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Active),
            PendingTenants = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Pending),
            SuspendedTenants = await db.Tenants.CountAsync(t => t.Status == TenantStatus.Suspended),
            TotalRevenue = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active)
                .SumAsync(s => s.Amount),
            ActiveSubscriptions = await db.Subscriptions
                .CountAsync(s => s.Status == SubscriptionStatus.Active)
        };
    }

    public async Task<List<TenantEvent>> GetRecentEventsAsync(int limit = 20, int? tenantId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        IQueryable<TenantEvent> q = db.TenantEvents.AsNoTracking().Include(e => e.Tenant);
        if (tenantId.HasValue)
            q = q.Where(e => e.TenantId == tenantId.Value);
        return await q.OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync();
    }

    public async Task<List<PaymentRecord>> GetRecentPaymentsAsync(int limit = 20, int? tenantId = null)
    {
        await using var db = dbFactory.CreateDbContext();
        var q = db.PaymentRecords.AsNoTracking().Include(p => p.Tenant).AsQueryable();
        if (tenantId.HasValue)
            q = q.Where(p => p.TenantId == tenantId.Value);
        return await q.OrderByDescending(p => p.CreatedAt).Take(limit).ToListAsync();
    }

    public async Task<DashboardStats> GetExtendedStatsAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var prevMonthStart = monthStart.AddMonths(-1);

        var stats = await GetStatsAsync();

        stats.MonthlyPaymentsTotal = await db.PaymentRecords
            .Where(p => p.Status == PaymentRecordStatus.Paid && p.CreatedAt >= monthStart)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        stats.PrevMonthPaymentsTotal = await db.PaymentRecords
            .Where(p => p.Status == PaymentRecordStatus.Paid && p.CreatedAt >= prevMonthStart && p.CreatedAt < monthStart)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        stats.FailedPaymentsCount = await db.PaymentRecords
            .CountAsync(p => p.Status == PaymentRecordStatus.Failed && p.CreatedAt >= monthStart);

        stats.NewTenantsThisMonth = await db.Tenants
            .CountAsync(t => t.CreatedAt >= monthStart);

        stats.ChurnedThisMonth = await db.Tenants
            .CountAsync(t => t.Status == TenantStatus.Suspended && t.CreatedAt < monthStart);

        stats.TrialingTenants = await db.Tenants
            .CountAsync(t => t.BillingStatus == "trialing" && t.Status == TenantStatus.Active);

        return stats;
    }
}

public class DashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int PendingTenants { get; set; }
    public int SuspendedTenants { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ActiveSubscriptions { get; set; }
    public decimal MonthlyPaymentsTotal { get; set; }
    public decimal PrevMonthPaymentsTotal { get; set; }
    public int FailedPaymentsCount { get; set; }
    public int NewTenantsThisMonth { get; set; }
    public int ChurnedThisMonth { get; set; }
    public int TrialingTenants { get; set; }
}
