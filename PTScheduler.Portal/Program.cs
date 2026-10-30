using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Components;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;

var builder = WebApplication.CreateBuilder(args);

// Lokalne dane połączenia trzymaj w connections.json (w .gitignore), nie w appsettings.
builder.Configuration.AddJsonFile("connections.json", optional: true, reloadOnChange: true);

builder.Services.AddDbContextFactory<PortalDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=ptportal;Username=ptportal;Password=ptportal";
    options.UseNpgsql(conn);
    options.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
});

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<PortalDbContext>>().CreateDbContext());

builder.Services.AddIdentity<IdentityUser, IdentityRole>(o =>
{
    o.Password.RequireDigit = true;
    o.Password.RequiredLength = 8;
    o.Password.RequireUppercase = true;
    o.Password.RequireNonAlphanumeric = true;
    o.SignIn.RequireConfirmedAccount = false;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<PortalDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/login";
    o.LogoutPath = "/logout";
    // Portal i tenanci mogą działać pod tym samym hostem (różne porty), a
    // przeglądarka nie rozróżnia ciasteczek po porcie. Własna nazwa zapobiega
    // nadpisywaniu ciasteczka logowania przez aplikację trenera i odwrotnie.
    o.Cookie.Name = ".PTPortal.Auth";
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
});
builder.Services.AddAntiforgery(o => o.Cookie.Name = ".PTPortal.AF");

// Panel trenera na stronie Hantly (/moje-konto) — osobne ciasteczko i schemat, bez ról: trener
// nie jest użytkownikiem Identity, więc żadna strona ani API administratora go nie wpuści.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<TrainerAccounts>();
builder.Services.AddAuthentication().AddCookie(TrainerAccounts.Scheme, o =>
{
    o.Cookie.Name = ".Hantly.Trainer";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.LoginPath = "/login";
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
});

// Klucze szyfrujące ciasteczka w bazie portalu, a nie w kontenerze — inaczej
// każda aktualizacja portalu wylogowywała administratorów.
builder.Services.AddDataProtection()
    .SetApplicationName("PTScheduler.Portal")
    .PersistKeysToDbContext<PortalDbContext>();

builder.Services.AddAuthorization();

builder.Services.AddSingleton<DockerService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<ContainerLogService>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddHostedService<OfferSyncService>();
builder.Services.AddScoped<SiteSettingsService>();
builder.Services.AddScoped<NpmService>();
builder.Services.AddScoped<UpdateService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<BunnyPlatformService>();
builder.Services.AddScoped<VideoHubService>();
builder.Services.AddScoped<ImageHubService>();
builder.Services.AddScoped<CommunityExerciseService>();
builder.Services.AddScoped<TenantDemoService>();
builder.Services.AddScoped<PublicDemoService>();
builder.Services.AddHostedService<PublicDemoResetJob>();
builder.Services.AddScoped<GoogleOAuthBroker>();
builder.Services.AddScoped<TenantMailRelay>();
builder.Services.AddScoped<StoreTicketService>();
builder.Services.AddScoped<BugReportService>();
builder.Services.AddScoped<AddonService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<StorePaymentService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddHostedService<BillingBackgroundService>();
builder.Services.AddHttpClient(FakturowniaClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<FakturowniaClient>();
builder.Services.AddScoped<GarminService>();
builder.Services.AddHostedService<GarminBackgroundService>();
builder.Services.AddScoped<AccountingService>();
builder.Services.AddHostedService<AccountingBackgroundService>();
builder.Services.AddScoped<ErrorAlertService>();
builder.Services.AddHostedService<ErrorAlertBackgroundService>();
builder.Services.AddScoped<UptimeService>();
builder.Services.AddHostedService<UptimeBackgroundService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<OffsiteBackupService>();
builder.Services.AddScoped<BackupMaintenanceService>();
builder.Services.AddScoped<RestoreService>();
builder.Services.AddSingleton<BackupJobs>();
builder.Services.AddScoped<ResourceReportService>();
builder.Services.AddSingleton<NpmHealthService>();
builder.Services.AddSingleton<AppLaunchService>();
builder.Services.AddScoped<RegistrationGateService>();
builder.Services.AddScoped<ReferralService>();
builder.Services.AddScoped<InviteService>();
builder.Services.AddScoped<LegalService>();
builder.Services.AddScoped<AiService>();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddScoped<LeoService>();
builder.Services.AddScoped<LeoInsights>();
builder.Services.AddScoped<LeoReportService>();
builder.Services.AddScoped<InboxService>();
builder.Services.AddScoped<LeoAnalytics>();
builder.Services.AddScoped<LeoWeeklyReport>();
builder.Services.AddHostedService<LeoReportBackgroundService>();
builder.Services.AddSingleton<FunnelService>();
builder.Services.AddHostedService<GrowthService>();
builder.Services.AddSingleton<CustomDomainService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CustomDomainService>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NpmHealthService>());
builder.Services.AddSingleton<ResourceMonitorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ResourceMonitorService>());
// Wysyłka kopii poza serwer może trwać długo — limit daje CancellationToken, nie HttpClient.
builder.Services.AddHttpClient("backup-offsite", c => c.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddSingleton<UpdateNotifier>();
builder.Services.AddSingleton<PanelStatusService>();
builder.Services.AddHostedService<UpdatePollerService>();
builder.Services.AddHostedService<TrialExpirationService>();
builder.Services.AddHostedService<HealthMonitorService>();
builder.Services.AddHostedService<TenantCleanupService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Panel w przeglądarce: po uśpieniu karty połączenie wznawia się po cichu (ReconnectModal.razor.js),
// a serwer pamięta rozłączoną sesję 30 minut zamiast domyślnych 3.
builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(30));

// Każda instancja ma własną pulę zapytań do API Portalu (SMS, poczta, wideo, Google, sklep):
// zapętlona albo przejęta instancja jednego trenera nie spowolni Portalu pozostałym.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        // Wejście do publicznego demo: każde tworzy jednorazowy token — limit na adres IP.
        // „Wyślij mi link” ze strony głównej — każde wysłanie to e-mail, więc limit na adres IP.
        if (ctx.Request.Path.StartsWithSegments("/link-na-pozniej"))
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("later:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "?"), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            });
        if (ctx.Request.Path.StartsWithSegments("/demo"))
            return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("demo:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "?"), _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            });
        var slug = TenantApiSlug(ctx.Request.Path);
        return slug is null
            ? System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("other")
            : System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("tenant:" + slug, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue<int?>("Portal:TenantApiRequestsPerMinute") ?? 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

// Nagłówki bezpieczeństwa: Portalu nie da się osadzić w ramce na cudzej stronie (clickjacking),
// przeglądarka nie zgaduje typów plików, a adres nie wycieka do obcych stron.
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
        if (!h.ContainsKey("Content-Security-Policy"))
            h["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";
        return Task.CompletedTask;
    });
    await next();
});

// Auto-migrate + seed admin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleManager.RoleExistsAsync("Admin"))
        await roleManager.CreateAsync(new IdentityRole("Admin"));

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    const string adminEmail = "admin@ptscheduler.pl";
    const string legacyAdminPassword = "Admin123!";
    var admins = await userManager.GetUsersInRoleAsync("Admin");
    if (admins.Count == 0)
    {
        // Konto startowe dostaje losowe hasło — wypisujemy je raz do logów kontenera.
        // Dawniej hasło „Admin123!” było w repozytorium, a konto wracało po każdej
        // zmianie adresu e-mail (szukane po adresie, nie po roli).
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)) + "aA1!";
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            await userManager.CreateAsync(admin, password);
        }
        else
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(admin);
            await userManager.ResetPasswordAsync(admin, token, password);
        }
        await userManager.AddToRoleAsync(admin, "Admin");
        app.Logger.LogWarning("Utworzono konto administratora Portalu {Email} z hasłem: {Password} — zmień je po zalogowaniu.",
            adminEmail, password);
    }
    else
    {
        // Nigdy nie usuwamy kont administratorów przy starcie (dawniej konto startowe ze starym hasłem
        // znikało, gdy pojawił się drugi administrator — przy zapraszaniu adminów to odcinało dostęp).
        var legacy = admins.FirstOrDefault(a => string.Equals(a.Email, adminEmail, StringComparison.OrdinalIgnoreCase));
        if (legacy is not null && await userManager.CheckPasswordAsync(legacy, legacyAdminPassword))
            app.Logger.LogWarning("Konto {Email} ma stare domyślne hasło — zmień je w Panelu → Bezpieczeństwo.", adminEmail);
    }

    // Awaryjny dostęp: Portal__RecoverAdmin=adres@e-mail w zmiennych kontenera i restart. Konto dostaje
    // rolę administratora, nowe losowe hasło (raz w logach), zdjętą blokadę i wyłączoną weryfikację
    // dwuetapową. Po zalogowaniu usuń zmienną i zmień hasło.
    // Jednorazowo: zostawiona zmienna (Guardian kopiuje ją przy aktualizacjach) nie resetuje hasła i 2FA przy każdym starcie.
    var recoverSettings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
    var recoverUsed = await recoverSettings.GetAsync(SiteSettingsService.Keys.RecoverAdminUsed);
    var recoverWanted = app.Configuration["Portal:RecoverAdmin"]?.Trim();
    if (string.IsNullOrEmpty(recoverWanted) && !string.IsNullOrEmpty(recoverUsed))
        await recoverSettings.SetAsync(SiteSettingsService.Keys.RecoverAdminUsed, ""); // zmienna zdjęta — kolejne odzyskanie znów możliwe
    else if (!string.IsNullOrEmpty(recoverWanted) && string.Equals(recoverUsed, recoverWanted, StringComparison.OrdinalIgnoreCase))
        app.Logger.LogWarning("Zmienna Portal__RecoverAdmin nadal jest ustawiona ({Email}) — dostęp już odzyskano, usuń ją (SKIP_BUILD=1 bash deploy/update-portal.sh).", recoverWanted);
    else if (recoverWanted is { Length: > 3 } recoverEmail && recoverEmail.Contains('@'))
    {
        await recoverSettings.SetAsync(SiteSettingsService.Keys.RecoverAdminUsed, recoverEmail);
        var password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18)) + "aA1!";
        var user = await userManager.FindByEmailAsync(recoverEmail);
        if (user is null)
        {
            user = new IdentityUser { UserName = recoverEmail, Email = recoverEmail, EmailConfirmed = true };
            await userManager.CreateAsync(user, password);
        }
        else
        {
            await userManager.RemovePasswordAsync(user);
            await userManager.AddPasswordAsync(user, password);
        }
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        if (!await userManager.IsInRoleAsync(user, "Admin")) await userManager.AddToRoleAsync(user, "Admin");
        app.Logger.LogWarning("ODZYSKIWANIE DOSTĘPU: konto administratora {Email}, hasło: {Password} — po zalogowaniu usuń zmienną Portal__RecoverAdmin i zmień hasło.",
            recoverEmail, password);
    }

    // Sekret komunikacji Portal ↔ tenanci. Bez niego endpointy /api/credits, /api/store
    // itd. wpuszczały każdego (warunek „pusty sekret = brak kontroli”). Gdy nie ma go
    // w konfiguracji, generujemy go raz i trzymamy w bazie; tenanci dostają go przy
    // (re)provisioningu.
    if (string.IsNullOrWhiteSpace(app.Configuration["Portal:TenantInternalSecret"]))
    {
        var settings = scope.ServiceProvider.GetRequiredService<SiteSettingsService>();
        var stored = await settings.GetAsync(SiteSettingsService.Keys.TenantInternalSecret);
        if (string.IsNullOrWhiteSpace(stored))
        {
            stored = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await settings.SetAsync(SiteSettingsService.Keys.TenantInternalSecret, stored);
            app.Logger.LogWarning("Wygenerowano nowy sekret tenantów — zreprowizjonuj tenantów, żeby go otrzymali.");
        }
        app.Configuration["Portal:TenantInternalSecret"] = stored;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// API zwraca czyste kody (401/404/409) — strona „nie znaleziono” jest tylko dla przeglądarki.
// Bez tego odmowa dostępu na POST/DELETE kończyła się mylącym 405 z ponownego wykonania.
app.Use((ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api")
        && ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } scp)
        scp.Enabled = false;
    return next(ctx);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// „Wymagaj 2FA od wszystkich” (Panel → Administratorzy): konto bez weryfikacji dwuetapowej
// trafia najpierw na stronę jej włączenia. Sprawdzamy tylko zwykłe wejścia na strony panelu.
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    if (ctx.User.Identity?.IsAuthenticated == true && HttpMethods.IsGet(ctx.Request.Method)
        && path.StartsWithSegments("/panel") && !path.StartsWithSegments("/panel/bezpieczenstwo")
        && !Path.HasExtension(path.Value))
    {
        var settings = ctx.RequestServices.GetRequiredService<SiteSettingsService>();
        if (await settings.GetAsync(SiteSettingsService.Keys.RequireAdmin2fa) == "true")
        {
            var users = ctx.RequestServices.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.GetUserAsync(ctx.User);
            if (user is not null && !user.TwoFactorEnabled)
            {
                ctx.Response.Redirect("/panel/bezpieczenstwo?wymagane=1");
                return;
            }
        }
    }
    await next();
});

app.UseAntiforgery();

// ?ref= z linku polecającego albo stopki „Zrobione w …” — zapamiętujemy na 30 dni, żeby polecenie
// zadziałało także wtedy, gdy trener najpierw obejrzy stronę główną, a zarejestruje się później.
app.Use(async (ctx, next) =>
{
    if (HttpMethods.IsGet(ctx.Request.Method) && ctx.Request.Query["ref"].ToString() is { Length: > 0 and <= 63 } refCode
        && System.Text.RegularExpressions.Regex.IsMatch(refCode, "^[a-z0-9-]+$"))
        ctx.Response.Cookies.Append("pt_ref", refCode, new CookieOptions
        {
            MaxAge = TimeSpan.FromDays(30), SameSite = SameSiteMode.Lax, HttpOnly = false, Secure = ctx.Request.IsHttps, IsEssential = true
        });
    await next();
});

// Wyszukiwarki: mapa publicznych stron i robots.txt (Panel, API i linki prywatne poza indeksem).
string PublicBase(HttpContext ctx) =>
    app.Configuration.GetValue<string>("Portal:PublicUrl")?.TrimEnd('/') is { Length: > 0 } pub ? pub : $"{ctx.Request.Scheme}://{ctx.Request.Host}";

app.MapGet("/sitemap.xml", (HttpContext ctx) =>
{
    var root = PublicBase(ctx);
    var pages = new[] { "/", "/register", "/pricing", "/aplikacja-dla-trenera-personalnego", "/porownanie/excel-i-whatsapp", "/porownanie/systemy-rezerwacji", "/regulamin", "/polityka-prywatnosci", "/umowa-powierzenia" };
    var urls = string.Concat(pages.Select(p => $"<url><loc>{System.Security.SecurityElement.Escape(root + p)}</loc></url>"));
    return Results.Content($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">{urls}</urlset>", "application/xml; charset=utf-8");
});

app.MapGet("/robots.txt", (HttpContext ctx) => Results.Text(
    $"User-agent: *\nDisallow: /panel\nDisallow: /api/\nDisallow: /subskrypcja/\nDisallow: /rachunek/\nDisallow: /login\nAllow: /\n\nSitemap: {PublicBase(ctx)}/sitemap.xml\n",
    "text/plain; charset=utf-8"));

app.MapGet("/health", async (IDbContextFactory<PortalDbContext> dbFactory) =>
{
    try
    {
        await using var db = dbFactory.CreateDbContext();
        await db.Database.CanConnectAsync();
        return Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }
    catch
    {
        return Results.Json(new { status = "unhealthy", timestamp = DateTime.UtcNow }, statusCode: 503);
    }
});

app.MapGet("/api/internal/git-config", async (HttpContext ctx, SiteSettingsService siteSettings) =>
{
    var guardianSecret = Environment.GetEnvironmentVariable("GUARDIAN_SECRET") ?? "";
    if (string.IsNullOrEmpty(guardianSecret))
    {
        var s = await siteSettings.GetAsync(SiteSettingsService.Keys.GuardianSecret);
        guardianSecret = s;
    }
    var provided = ctx.Request.Headers["X-Guardian-Secret"].FirstOrDefault();
    if (!TenantSecrets.Matches(provided, guardianSecret))
        return Results.Unauthorized();

    var settings = await siteSettings.GetAllAsync(
        SiteSettingsService.Keys.GithubToken,
        SiteSettingsService.Keys.GithubOwner,
        SiteSettingsService.Keys.GithubRepo,
        SiteSettingsService.Keys.GithubBranch);

    return Results.Ok(new
    {
        token = settings[SiteSettingsService.Keys.GithubToken],
        owner = settings[SiteSettingsService.Keys.GithubOwner],
        repo = settings[SiteSettingsService.Keys.GithubRepo],
        branch = settings[SiteSettingsService.Keys.GithubBranch]
    });
});

// „Nie teraz? Wyślemy Ci link” ze strony głównej: jeden e-mail z linkiem do kreatora (z nazwą studia
// i kolorem z kreatora na żywo). Adresu nie zapisujemy; ten sam adres dostaje link najwyżej raz na 10 minut.
app.MapPost("/link-na-pozniej", async (HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery,
    EmailService email, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, IConfiguration config, ILogger<Program> log) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var back = form["wroc"] == "ld-later-dark" ? "#ld-later-dark" : "#ld-later-hero";
    try { await antiforgery.ValidateRequestAsync(ctx); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.Redirect("/?link=blad" + back); }

    var to = form["email"].ToString().Trim();
    if (!HomeLinks.IsEmail(to) || RegistrationGateService.IsDisposableEmail(to)) return Results.Redirect("/?link=blad" + back);
    var key = "later:" + to.ToLowerInvariant();
    if (!cache.TryGetValue(key, out _))
    {
        Microsoft.Extensions.Caching.Memory.CacheExtensions.Set(cache, key, true, TimeSpan.FromMinutes(10));
        var company = HomeLinks.Clean(form["nazwa"], 40);
        var link = HomeLinks.RegisterLink(PortalUrls.Public(config), company, form["kolor"]);
        var (ok, error) = await email.SendAsync(to, "Twój link do kreatora Hantly", email.LaterLinkEmailBody(company, link));
        if (!ok)
        {
            cache.Remove(key);
            log.LogWarning("Link na później nie wyszedł: {Error}", error);
            return Results.Redirect("/?link=blad" + back);
        }
    }
    return Results.Redirect("/?link=wyslany" + back);
});

// Wejście do aplikacji trenera jednym kliknięciem: token powstaje dopiero po kliknięciu, żyje 2 minuty
// i działa raz; aplikacja trenera sprawdza go w /sso/portal. Wejście zapisujemy w historii trenera.
// Publiczne demo: „Wypróbuj demo” na stronie głównej → jednorazowe wejście do instancji pokazowej
// (?jako=klient — aplikacja klienta). Gdy demo jest wyłączone, wracamy na stronę główną.
app.MapGet("/demo", async (string? jako, HttpContext ctx, PublicDemoService publicDemo) =>
{
    var url = await publicDemo.EntryUrlAsync(asClient: jako == "klient");
    ctx.Response.Headers.CacheControl = "no-store";
    return Results.Redirect(url ?? "/");
});

app.MapGet("/panel/wejdz/{id:int}", async (int id, string? jako, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    TenantService tenants, IConfiguration config, ILogger<Program> log) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id);
    if (t is null || t.Status != TenantStatus.Active) return Results.Redirect($"/panel/tenants/{id}");
    var secret = TenantSecrets.For(t, config);
    if (string.IsNullOrWhiteSpace(secret)) return Results.Redirect($"/panel/tenants/{id}?sso=nosecret");
    var admin = ctx.User.Identity?.Name ?? "admin";
    var target = jako == "wlasciciel" ? "owner" : "root";
    db.TenantEvents.Add(new TenantEvent
    {
        TenantId = t.Id, EventType = TenantEventTypes.AdminSso,
        Detail = $"{admin} — {(target == "owner" ? "jako właściciel studia" : "konto techniczne")}",
    });
    await db.SaveChangesAsync();
    log.LogInformation("Administrator {Admin} wchodzi do aplikacji {Slug} ({Target}).", admin, t.Slug, target);
    var token = TenantSso.CreateToken(t.Slug, admin, target, secret, DateTimeOffset.UtcNow);
    ctx.Response.Headers.CacheControl = "no-store";
    return Results.Redirect(TenantSso.Url(await tenants.PublicUrlAsync(t), token));
}).RequireAuthorization(p => p.RequireRole("Admin"));

app.MapPost("/api/account/login", async (
    HttpContext ctx,
    SignInManager<IdentityUser> signIn,
    UserManager<IdentityUser> userMgr,
    IDbContextFactory<PortalDbContext> dbFactory) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var remember = form["rememberMe"] == "true";
    var returnUrl = form["returnUrl"].ToString();
    if (string.IsNullOrEmpty(returnUrl)) returnUrl = "/panel";

    var ip = ctx.Connection.RemoteIpAddress?.ToString();
    var ua = ctx.Request.Headers.UserAgent.ToString();
    if (ua.Length > 256) ua = ua[..256];

    var result = await signIn.PasswordSignInAsync(email, password, remember, lockoutOnFailure: true);

    // Nie administrator? To może trener z hasłem z kreatora — trafia do swojego panelu (/moje-konto).
    var trainers = ctx.RequestServices.GetRequiredService<TrainerAccounts>();
    var trainerOk = !result.Succeeded && !result.RequiresTwoFactor && !result.IsLockedOut
        && await trainers.CheckPasswordAsync(email, password);

    await using var db = dbFactory.CreateDbContext();
    db.LoginLogs.Add(new LoginLog
    {
        Email = email,
        Success = result.Succeeded || trainerOk,
        IpAddress = ip,
        UserAgent = ua,
        FailureReason = result.Succeeded ? null
            : trainerOk ? "trainer_panel"
            : result.IsLockedOut ? "locked_out"
            : result.IsNotAllowed ? "not_allowed"
            : "invalid_credentials"
    });
    await db.SaveChangesAsync();

    if (result.Succeeded)
    {
        // Administrator w tej samej przeglądarce co test kreatora — sesja trenera nie powinna się mieszać.
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(ctx, TrainerAccounts.Scheme);
        return Results.Redirect(SafeReturn(returnUrl));
    }

    if (trainerOk)
    {
        await TrainerAccounts.SignInAsync(ctx, email);
        return Results.Redirect("/moje-konto");
    }
    if (trainers.IsLocked(email))
        return Results.Redirect("/login?error=locked");

    // Hasło dobre, ale konto ma weryfikację dwuetapową — drugi krok (kod z aplikacji).
    if (result.RequiresTwoFactor)
        return Results.Redirect($"/login?step=2fa&remember={(remember ? "1" : "0")}&returnUrl={Uri.EscapeDataString(SafeReturn(returnUrl))}");

    if (result.IsLockedOut)
        return Results.Redirect("/login?error=locked");

    return Results.Redirect("/login?error=1");
});

// ── Panel trenera (/moje-konto): wejście jednorazowym kluczem, link z e-maila, wylogowanie ──
app.MapGet("/moje-konto/wejdz", async (string? t, HttpContext ctx, TrainerAccounts trainers) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (trainers.ConsumeLoginToken(t) is not { } email) return Results.Redirect("/login?error=link");
    await TrainerAccounts.SignInAsync(ctx, email);
    return Results.Redirect("/moje-konto");
});

// Kreator po zapisaniu zgłoszenia loguje trenera w tle (fetch) — panel jest gotowy, zanim aplikacja wstanie.
app.MapPost("/api/konto/auto", async (HttpContext ctx, TrainerAccounts trainers) =>
{
    var form = await ctx.Request.ReadFormAsync();
    if (trainers.ConsumeLoginToken(form["t"].ToString()) is not { } email) return Results.BadRequest();
    await TrainerAccounts.SignInAsync(ctx, email);
    return Results.NoContent();
});

app.MapPost("/api/konto/link", async (HttpContext ctx, TrainerAccounts trainers, EmailService mail) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var email = TrainerAccounts.Normalize(form["email"].ToString());
    // Ta sama odpowiedź bez względu na to, czy adres jest u nas — nie zdradzamy, kto ma konto.
    if (email.Length is > 3 and < 255 && email.Contains('@') && (await trainers.TenantsOfAsync(email)).Count > 0 && trainers.TryReserveLinkEmail(email))
    {
        var link = $"{PublicBase(ctx)}/moje-konto/wejdz?t={trainers.CreateLoginToken(email)}";
        await mail.SendAsync(email, "Link do panelu Hantly", mail.TrainerLoginLinkEmailBody(link));
    }
    return Results.Redirect("/login?link=sent");
});

// „Zarządzaj subskrypcją” z panelu trenera: karta → portal klienta Stripe; rachunki → strona subskrypcji Portalu.
app.MapGet("/moje-konto/subskrypcja/{id:int}", async (int id, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, StripeService stripe) =>
{
    if (await TrainerAccounts.CurrentEmailAsync(ctx) is not { } email) return Results.Redirect("/login");
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerEmail.ToLower() == email);
    if (tenant is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "no-store";
    if (!string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
    {
        var url = await stripe.CreateCustomerPortalLinkAsync(tenant.StripeCustomerId, $"{PublicBase(ctx)}/moje-konto");
        return url is null ? Results.Redirect("/moje-konto?blad=stripe") : Results.Redirect(url);
    }
    var token = ctx.RequestServices.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector(SubscriptionLinks.Purpose).ToTimeLimitedDataProtector().Protect(tenant.Id.ToString(), TimeSpan.FromMinutes(30));
    return Results.Redirect($"/subskrypcja/{Uri.EscapeDataString(token)}");
});

// Dokument sprzedaży w panelu trenera — tylko jego własne aplikacje.
app.MapGet("/moje-konto/dokument/{id:int}.pdf", async (int id, HttpContext ctx, TrainerAccounts trainers, AccountingService acc) =>
{
    if (await TrainerAccounts.CurrentEmailAsync(ctx) is not { } email) return Results.Redirect("/login");
    var ids = (await trainers.TenantsOfAsync(email)).Select(t => t.Id).ToHashSet();
    var doc = await acc.GetDocumentAsync(id);
    if (doc?.TenantId is not { } tenantId || !ids.Contains(tenantId) || doc.Status != SalesDocumentStatus.Issued) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "no-store";
    return await acc.PdfAsync(id) is { } f ? Results.File(f.Pdf, "application/pdf", f.FileName) : Results.NotFound();
});

// Dane do rachunku / faktury z panelu trenera (formularz bez JavaScriptu).
app.MapPost("/api/konto/dane-do-faktury", async (HttpContext ctx, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery, TrainerAccounts trainers, AccountingService acc) =>
{
    if (await TrainerAccounts.CurrentEmailAsync(ctx) is not { } email) return Results.Redirect("/login");
    var form = await ctx.Request.ReadFormAsync();
    try { await antiforgery.ValidateRequestAsync(ctx); }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { return Results.Redirect("/moje-konto?dane=blad#dane-do-faktury"); }
    if (!int.TryParse(form["tenant"], out var tenantId) || !(await trainers.TenantsOfAsync(email)).Any(t => t.Id == tenantId)) return Results.NotFound();
    var company = form["typ"] == "firma";
    var error = await acc.SaveBuyerAsync(tenantId, form["nazwa"], form["adres"], company ? form["nip"].ToString() : null, form["email"], $"trener {email}");
    return Results.Redirect(error is null ? "/moje-konto?dane=ok#dane-do-faktury" : $"/moje-konto?dane={Uri.EscapeDataString(error)}#dane-do-faktury");
});

app.MapGet("/api/konto/wyloguj", async (HttpContext ctx) =>
{
    await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(ctx, TrainerAccounts.Scheme);
    return Results.Redirect("/logout?trener=1");
});

// Drugi krok logowania: kod z Google Authenticator (TOTP) albo kod zapasowy.
app.MapPost("/api/account/login-2fa", async (HttpContext ctx, SignInManager<IdentityUser> signIn, IDbContextFactory<PortalDbContext> dbFactory) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var code = new string(form["code"].ToString().Where(char.IsLetterOrDigit).ToArray());
    var remember = form["rememberMe"] == "true";
    var rememberDevice = form["rememberDevice"] == "true";
    var returnUrl = SafeReturn(form["returnUrl"].ToString());

    var user = await signIn.GetTwoFactorAuthenticationUserAsync();
    if (user is null) return Results.Redirect("/login?error=2fa-expired");

    // 6 cyfr — kod z aplikacji; dłuższy albo z literami — kod zapasowy (jednorazowy).
    var recovery = code.Length != 6 || code.Any(char.IsLetter);
    var result = recovery
        ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
        : await signIn.TwoFactorAuthenticatorSignInAsync(code, remember, rememberDevice);

    await using var db = dbFactory.CreateDbContext();
    var ua = ctx.Request.Headers.UserAgent.ToString();
    db.LoginLogs.Add(new LoginLog
    {
        Email = user.Email ?? "",
        Success = result.Succeeded,
        IpAddress = ctx.Connection.RemoteIpAddress?.ToString(),
        UserAgent = ua.Length > 256 ? ua[..256] : ua,
        FailureReason = result.Succeeded ? (recovery ? "recovery_code" : null) : result.IsLockedOut ? "locked_out" : "invalid_2fa_code"
    });
    await db.SaveChangesAsync();

    if (result.Succeeded)
    {
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(ctx, TrainerAccounts.Scheme);
        return Results.Redirect(returnUrl);
    }
    if (result.IsLockedOut) return Results.Redirect("/login?step=2fa&error=locked");
    return Results.Redirect($"/login?step=2fa&error=2fa&remember={(remember ? "1" : "0")}&returnUrl={Uri.EscapeDataString(returnUrl)}");
});

// Ustawienie hasła z zaproszenia (nowy administrator) — token resetu hasła z Identity, ważny dobę.
app.MapPost("/api/account/admin-invite", async (HttpContext ctx, UserManager<IdentityUser> userMgr) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var id = form["u"].ToString();
    var token = form["t"].ToString();
    var password = form["password"].ToString();
    string Back(string error) => $"/zaproszenie-admina?u={Uri.EscapeDataString(id)}&t={Uri.EscapeDataString(token)}&error={Uri.EscapeDataString(error)}";
    if (password != form["password2"].ToString()) return Results.Redirect(Back("Hasła się różnią."));
    var user = await userMgr.FindByIdAsync(id);
    if (user is null || !await userMgr.IsInRoleAsync(user, "Admin")) return Results.Redirect(Back("Zaproszenie jest nieaktualne."));
    var result = await userMgr.ResetPasswordAsync(user, token, password);
    if (!result.Succeeded)
    {
        var msg = result.Errors.Any(e => e.Code == "InvalidToken")
            ? "Link wygasł albo został już użyty — poproś o nowe zaproszenie."
            : "Hasło musi mieć co najmniej 8 znaków, wielką literę, cyfrę i znak specjalny.";
        return Results.Redirect(Back(msg));
    }
    await userMgr.SetLockoutEndDateAsync(user, null);
    return Results.Redirect("/login?zaproszenie=ok");
});

// Tylko adresy w obrębie Portalu (bez przekierowań na obce strony po zalogowaniu).
static string SafeReturn(string? url) =>
    !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/\\") ? url : "/panel";

app.MapGet("/api/account/logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Redirect("/logout");
});

// Stripe webhook — Stripe posts events here as JSON with a signature
// header. Return 200 quickly; Stripe will retry on any non-2xx.
// ── Garmin Connect (plan Starter) ──
// Powrót z logowania do Garmina → z powrotem do aplikacji studia (adres studia + zapamiętana ścieżka).
app.MapGet("/garmin/callback", async (string? code, string? state, string? error, GarminService garmin) =>
    Results.Redirect(await garmin.CallbackAsync(code, state, error)));

// Dane z Garmina (push). Klucz w adresie zna tylko Garmin (ustawiony w portalu dewelopera); zły klucz = 404.
app.MapPost("/api/garmin/push/{key}", async (string key, HttpContext ctx, GarminService garmin) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    return await garmin.ReceiveAsync(key, body) ? Results.Ok() : Results.NotFound();
});

// Aplikacja studia → Portal (sekret instancji): stan, start logowania klienta, odłączenie.
app.MapGet("/api/garmin/{slug}/status", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config, GarminService garmin) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var cfg = await garmin.ConfigAsync();
    return Results.Json(new { enabled = cfg.Enabled, workouts = cfg.Workouts });
});
app.MapPost("/api/garmin/{slug}/start", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config, GarminService garmin) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    if (!doc.RootElement.TryGetProperty("clientId", out var cid) || !cid.TryGetInt32(out var clientId)) return Results.BadRequest(new { error = "Brak klienta." });
    var path = doc.RootElement.TryGetProperty("returnUrl", out var r) ? r.GetString() : null;
    var (url, error) = await garmin.StartAsync(tenant, clientId, path);
    return url is null ? Results.BadRequest(new { error }) : Results.Json(new { url });
});
app.MapPost("/api/garmin/{slug}/disconnect", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config, GarminService garmin) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    if (!doc.RootElement.TryGetProperty("clientId", out var cid) || !cid.TryGetInt32(out var clientId)) return Results.BadRequest(new { error = "Brak klienta." });
    var error = await garmin.DisconnectAsync(tenant, clientId);
    return error is null ? Results.Ok() : Results.Json(new { error }, statusCode: 502);
});

// Plan na zegarek (Training API): studio wysyła gotowy trening, Portal tworzy go u Garmina i planuje na dzień.
app.MapPost("/api/garmin/{slug}/workout", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config, GarminService garmin) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    var r = doc.RootElement;
    if (!r.TryGetProperty("clientId", out var cid) || cid.ValueKind != System.Text.Json.JsonValueKind.Number || !cid.TryGetInt32(out var clientId)
        || !r.TryGetProperty("workout", out var workout) || workout.ValueKind != System.Text.Json.JsonValueKind.Object
        || !r.TryGetProperty("date", out var d) || !DateOnly.TryParseExact(d.GetString(), "yyyy-MM-dd", out var date))
        return Results.BadRequest(new { error = "Niepełne dane treningu." });
    long? replace = r.TryGetProperty("replaceWorkoutId", out var rep) && rep.ValueKind == System.Text.Json.JsonValueKind.Number && rep.TryGetInt64(out var old) ? old : null;
    var res = await garmin.PushWorkoutAsync(tenant, clientId, workout, date, replace);
    return res.Error is null && res.WorkoutId is not null
        ? Results.Json(new { workoutId = res.WorkoutId, scheduleId = res.ScheduleId })
        : Results.Json(new { error = res.Error, workoutId = res.WorkoutId }, statusCode: 502);
});
app.MapPost("/api/garmin/{slug}/workout/delete", async (string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, IConfiguration config, GarminService garmin) =>
{
    await using var db = await dbFactory.CreateDbContextAsync();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
    var r = doc.RootElement;
    if (!r.TryGetProperty("clientId", out var cid) || !cid.TryGetInt32(out var clientId)
        || !r.TryGetProperty("workoutId", out var w) || w.ValueKind != System.Text.Json.JsonValueKind.Number || !w.TryGetInt64(out var workoutId))
        return Results.BadRequest(new { error = "Brak treningu." });
    var error = await garmin.DeleteWorkoutAsync(tenant, clientId, workoutId);
    return error is null ? Results.Ok() : Results.Json(new { error }, statusCode: 502);
});

app.MapPost("/api/webhooks/stripe", async (HttpContext ctx, StripeService stripe) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var payload = await reader.ReadToEndAsync();
    var sig = ctx.Request.Headers["Stripe-Signature"].ToString();
    var (handled, msg) = await stripe.HandleWebhookAsync(payload, sig);
    return handled ? Results.Ok(new { received = true, type = msg }) : Results.BadRequest(new { error = msg });
});

// Stripe checkout.session.completed for store one-time payments
app.MapPost("/api/webhooks/stripe/store", async (HttpContext ctx, StorePaymentService storePayment, SiteSettingsService siteSettings) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var payload = await reader.ReadToEndAsync();
    var sig = ctx.Request.Headers["Stripe-Signature"].ToString();

    var webhookSecret = await siteSettings.GetAsync(SiteSettingsService.Keys.StripeWebhookSecret);
    if (string.IsNullOrWhiteSpace(webhookSecret))
        return Results.BadRequest(new { error = "Webhook secret not configured" });

    Stripe.Event stripeEvent;
    try
    {
        stripeEvent = Stripe.EventUtility.ConstructEvent(payload, sig, webhookSecret);
    }
    catch (Stripe.StripeException)
    {
        return Results.BadRequest(new { error = "Invalid signature" });
    }

    if (stripeEvent.Type == Stripe.EventTypes.CheckoutSessionCompleted)
    {
        var session = (Stripe.Checkout.Session)stripeEvent.Data.Object;
        if (session.Metadata.TryGetValue("source", out var source) && source == "store"
            && session.Metadata.TryGetValue("orderGroupId", out _))
        {
            await storePayment.HandlePaymentConfirmationAsync("stripe", session.Id);
        }
    }

    return Results.Ok(new { received = true });
});

// PayU notification webhook
app.MapPost("/api/webhooks/payu", async (HttpContext ctx, StorePaymentService storePayment, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var signature = ctx.Request.Headers["OpenPayU-Signature"].ToString();

    if (!await storePayment.VerifyPayuNotification(body, signature))
    {
        logger.LogWarning("PayU webhook signature verification failed");
        return Results.BadRequest(new { error = "Invalid signature" });
    }

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("order", out var order))
        {
            var status = order.GetProperty("status").GetString();
            var orderId = order.GetProperty("orderId").GetString();

            if (status == "COMPLETED" && !string.IsNullOrWhiteSpace(orderId))
            {
                await storePayment.HandlePaymentConfirmationAsync("payu", orderId);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "PayU webhook processing failed");
    }

    return Results.Ok();
});

// Autopay ITN (application/x-www-form-urlencoded, pole „transactions”). Odpowiedź: XML confirmationList, zawsze HTTP 200.
app.MapPost("/api/webhooks/autopay", async (HttpContext ctx, StorePaymentService storePayment) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var xml = await storePayment.HandleAutopayItnAsync(body);
    return xml is null ? Results.BadRequest() : Results.Content(xml, "application/xml", System.Text.Encoding.UTF8);
});

// Autopay: powrót trenera z bramki — wraca do swojego sklepu (status przychodzi osobno w ITN).
app.MapGet("/api/webhooks/autopay/return", async (HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, BillingService billing, StorePaymentService store) =>
{
    var orderId = AutopayProtocol.SafeOrderId(ctx.Request.Query["OrderID"].ToString());
    if (orderId is null) return Results.Redirect("/");
    // Płatność weryfikacyjna z kreatora rejestracji — wracamy do kreatora (potwierdzenie przyjdzie ITN-em).
    if (await store.FindRegistrationAsync("autopay", orderId) is { RegistrationKey: { Length: > 0 } regKey })
        return Results.Redirect($"/register?wznow={Uri.EscapeDataString(regKey)}&platnosc=1");
    if (await billing.PayUrlBySessionAsync(orderId) is { } billUrl) return Results.Redirect(billUrl);
    await using var db = dbFactory.CreateDbContext();
    var domain = await db.ServiceOrders.AsNoTracking()
        .Where(o => o.PaymentExternalId == orderId && o.PaymentGateway == "autopay")
        .Select(o => o.Tenant.Domain).FirstOrDefaultAsync();
    return string.IsNullOrWhiteSpace(domain)
        ? Results.Redirect("/")
        : Results.Redirect($"https://{domain}/admin/sklep?payment=success");
});

// Przelewy24 notification webhook
app.MapPost("/api/webhooks/przelewy24", async (HttpContext ctx, StorePaymentService storePayment, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!await storePayment.VerifyP24Notification(root))
        {
            logger.LogWarning("P24 webhook signature verification failed");
            return Results.BadRequest(new { error = "Invalid signature" });
        }

        if (await storePayment.ConfirmP24Transaction(root))
        {
            var sessionId = root.GetProperty("sessionId").GetString();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await using var db = ctx.RequestServices.GetRequiredService<IDbContextFactory<PortalDbContext>>().CreateDbContext();
                var orders = await db.ServiceOrders
                    .Where(o => o.OrderGroupId == sessionId && o.PaymentGateway == "przelewy24")
                    .ToListAsync();

                if (orders.Count > 0)
                {
                    var externalId = orders[0].PaymentExternalId;
                    if (!string.IsNullOrWhiteSpace(externalId))
                        await storePayment.HandlePaymentConfirmationAsync("przelewy24", externalId);
                }
                else
                {
                    // Rachunek trenera: sessionId to nasz identyfikator płatności.
                    await ctx.RequestServices.GetRequiredService<BillingService>().TryConfirmAsync("przelewy24", null, sessionId);
                }
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "P24 webhook processing failed");
    }

    return Results.Ok();
});

// Rachunek trenera: rozpoczęcie płatności online z publicznej strony rachunku (link z e-maila).
app.MapPost("/rachunek/{token}/pay", async (string token, HttpContext ctx, BillingService billing) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var gateway = form["gateway"].ToString();
    var (url, error) = await billing.StartPaymentAsync(token, gateway);
    return url is not null
        ? Results.Redirect(url)
        : Results.Redirect($"/rachunek/{Uri.EscapeDataString(token)}?error={Uri.EscapeDataString(error ?? "nieznany błąd")}");
}).DisableAntiforgery();

// Backup file download — admin only. The BackupEntry.Id determines
// which file to stream; the path is stored in the entry so nothing
// user-controlled hits the filesystem.
app.MapGet("/api/backups/{id:int}/download", async (
    int id,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();

    await using var db = dbFactory.CreateDbContext();
    var entry = await db.BackupEntries.FindAsync(id);
    if (entry is null || string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath))
        return Results.NotFound();

    var type = BackupService.IsBundle(entry.FilePath) ? "application/x-tar" : "application/gzip";
    return Results.File(entry.FilePath, type, Path.GetFileName(entry.FilePath), enableRangeProcessing: true);
}).RequireAuthorization();

// Zdjęcia sylwetek trenerów ze strony głównej (publiczne; trzymane w ustawieniach Portalu).
app.MapGet("/media/trainers/{id}.jpg", async (string id, HttpContext ctx, SiteSettingsService siteSettings) =>
{
    var jpeg = await FeaturedTrainers.GetPhotoAsync(siteSettings, id);
    if (jpeg is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=604800";
    return Results.File(jpeg, "image/jpeg");
});

// Oferta trenera jako PDF — tylko administrator.
app.MapGet("/api/panel/tenants/{id:int}/offer.pdf", async (
    int id,
    HttpContext ctx,
    OfferService offers,
    SiteSettingsService siteSettings) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    var offer = await offers.GetAsync(id);
    if (offer is null) return Results.NotFound();
    var seller = await siteSettings.GetAsync(SiteSettingsService.Keys.OfferSellerDetails);
    var pdf = OfferPdf.Render(offer, seller, "Hantly");
    return Results.File(pdf, "application/pdf", $"oferta-{offer.Tenant.Slug}-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
}).RequireAuthorization();

// ── Księgowość (tylko administrator): PDF dokumentów, dowody kosztów, ewidencja, rejestry i paczka dla księgowej ──
static (DateOnly From, DateOnly To) FinancePeriod(string? od, string? @do)
{
    var today = AccountingService.Today;
    var from = DateOnly.TryParse(od, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : new DateOnly(today.Year, today.Month, 1);
    var to = DateOnly.TryParse(@do, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : today;
    return to < from ? (to, from) : (from, to);
}

app.MapGet("/api/panel/ksiegowosc/dokument/{id:int}.pdf", async (int id, HttpContext ctx, AccountingService acc) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    ctx.Response.Headers.CacheControl = "no-store";
    return await acc.PdfAsync(id) is { } f ? Results.File(f.Pdf, "application/pdf", f.FileName) : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/api/panel/ksiegowosc/koszt/{id:int}/dowod", async (int id, HttpContext ctx, AccountingService acc) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // Dowód zawsze jako pobranie (nie otwieramy w Portalu treści przesłanej z zewnątrz).
    return await acc.ExpenseAttachmentAsync(id) is { } a ? Results.File(a.Data, "application/octet-stream", a.Name) : Results.NotFound();
}).RequireAuthorization();

app.MapGet("/api/panel/ksiegowosc/{plik}", async (string plik, string? od, string? @do, HttpContext ctx, AccountingService acc) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    ctx.Response.Headers.CacheControl = "no-store";
    var (from, to) = FinancePeriod(od, @do);
    var suffix = $"{from:yyyy-MM-dd}_{to:yyyy-MM-dd}";
    switch (plik)
    {
        case "ewidencja.csv":
            return Results.File(await acc.LedgerCsvAsync(from, to), "text/csv; charset=utf-8", $"ewidencja-sprzedazy_{suffix}.csv");
        case "ewidencja.pdf":
            return Results.File(await acc.LedgerPdfAsync(from, to), "application/pdf", $"ewidencja-sprzedazy_{suffix}.pdf");
        case "dokumenty.csv":
        {
            var docs = (await acc.DocumentsAsync(new AccountingService.DocumentFilter(), 100_000)).Where(d => d.IssueDate >= from && d.IssueDate <= to).ToList();
            return Results.File(AccountingService.DocumentsCsv(docs), "text/csv; charset=utf-8", $"dokumenty_{suffix}.csv");
        }
        case "koszty.csv":
            return Results.File(AccountingService.ExpensesCsv(await acc.ExpensesAsync(from, to)), "text/csv; charset=utf-8", $"koszty_{suffix}.csv");
        case "paczka.zip":
            return Results.File(await acc.PackageAsync(from, to, ctx.User.Identity?.Name ?? "admin"), "application/zip", $"ksiegowosc_{suffix}.zip");
        default:
            return Results.NotFound();
    }
}).RequireAuthorization();

// Logi kontenera jako plik .txt — tylko administrator i tylko kontenery, które panel sam pokazuje.
app.MapGet("/api/panel/logs/download", async (
    string c, int? tail, int? minutes,
    HttpContext ctx,
    ContainerLogService logs) =>
{
    if (!ctx.User.IsInRole("Admin")) return Results.Forbid();
    var source = await logs.FindAsync(c);
    if (source is null) return Results.NotFound();

    DateTime? since = minutes is > 0 ? DateTime.UtcNow.AddMinutes(-minutes.Value) : null;
    var lines = await logs.ReadAsync(source.Container, tail ?? 2000, since, ctx.RequestAborted);
    var text = ContainerLogService.ToText(source.Container, lines);
    var file = $"{source.Container}-{DateTime.UtcNow:yyyyMMdd-HHmm}.txt";
    return Results.File(System.Text.Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", file);
}).RequireAuthorization();

// Tenant apps pull their current plan at startup, so a restart (or Guardian rolling
// update, which clones the old container env) never runs on stale entitlements.
app.MapGet("/api/internal/tenants/{slug}/entitlements", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    TenantService tenants) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tenant.PlanId);
    if (plan is null) return Results.NotFound();
    return Results.Content(await tenants.EntitlementsJsonAsync(tenant.Id, plan), "application/json");
});

// „Zarządzaj subskrypcją” w aplikacji trenera: link do portalu klienta Stripe (karta, faktury, anulowanie).
app.MapPost("/api/internal/tenants/{slug}/billing/portal", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    StripeService stripe,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var body = await ctx.Request.ReadFromJsonAsync<Dictionary<string, string?>>();
    if (string.IsNullOrWhiteSpace(tenant.StripeCustomerId))
    {
        // Rozliczenie rachunkami (Autopay / PayU / Przelewy24): strona subskrypcji w Portalu z linkiem ważnym 30 minut.
        var protector = ctx.RequestServices.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>()
            .CreateProtector(SubscriptionLinks.Purpose).ToTimeLimitedDataProtector();
        var token = protector.Protect(tenant.Id.ToString(), TimeSpan.FromMinutes(30));
        var portalBase = config.GetValue<string>("Portal:PublicUrl")?.TrimEnd('/') is { Length: > 0 } pub ? pub : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
        return Results.Json(new { url = $"{portalBase}/subskrypcja/{Uri.EscapeDataString(token)}" });
    }
    var returnUrl = body?.GetValueOrDefault("returnUrl");
    if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        returnUrl = $"https://{tenant.Domain}/admin/upgrade";
    var url = await stripe.CreateCustomerPortalLinkAsync(tenant.StripeCustomerId, returnUrl);
    return url is null ? Results.Json(new { error = "Stripe nie odpowiada." }, statusCode: 502) : Results.Json(new { url });
});

// Polecenia i stopka „Zrobione w …” w aplikacji trenera: link polecający, statystyki i czy stopka
// jest obowiązkowa w planie (plany z podstawowym brandingiem — wyłączyć można od wyższego planu).
// „Wypróbuj 14 dni” z zablokowanej funkcji w aplikacji trenera — jednorazowo, bez wpływu na rachunki.
app.MapPost("/api/internal/tenants/{slug}/plan-trial", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    TenantService tenants) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    string? feature = null;
    try
    {
        var body = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        if (body.RootElement.TryGetProperty("feature", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String) feature = f.GetString();
    }
    catch (System.Text.Json.JsonException) { }
    var r = await tenants.StartUpgradeTrialAsync(tenant.Id, feature);
    return Results.Json(new { ok = r.Ok, error = r.Error, plan = r.PlanName, until = r.Until });
});

// Cennik dla aplikacji studia („Ulepsz plan”, kłódki, zajawki): aktywne plany z funkcjami z katalogu.
// Funkcje niedostępne na całej platformie (SMS bez bramki, Garmin wyłączony) nie trafiają do zajawek.
app.MapGet("/api/internal/tenants/{slug}/plans", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    TenantService tenants) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var smsOn = await tenants.SmsOnAsync();
    var garminOn = await tenants.GarminOnAsync();
    var features = PlanFeatureCatalog.All
        .Where(f => (smsOn || f.Key is not ("SmsReminders" or "MaxSmsPerMonth")) && (garminOn || f.Key != "Wearables"))
        .ToList();
    var plans = await db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
    return Results.Json(new
    {
        plans = plans.Select(p => new
        {
            id = p.Id, name = p.Name, description = p.Description, monthlyPrice = p.MonthlyPrice, yearlyPrice = p.YearlyPrice,
            isFeatured = p.IsFeatured, trialDays = p.TrialDays,
            features = features.ToDictionary(f => f.Key, f => PlanFeatureCatalog.Get(p, f.Key))
        }),
        features = features.Select(f => new { key = f.Key, name = f.Name, benefit = f.Benefit, icon = f.Icon, kind = f.Kind.ToString(), pitch = f.Pitch })
    });
});

app.MapGet("/api/internal/tenants/{slug}/growth", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    ReferralService referrals,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var portal = config.GetValue<string>("Portal:PublicUrl")?.TrimEnd('/') is { Length: > 0 } pub ? pub : $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    var stats = await referrals.StatsAsync(tenant, portal);
    return Results.Json(new
    {
        platformName = ReferralService.PlatformName,
        badgeUrl = $"{portal}/?ref={Uri.EscapeDataString(tenant.Slug)}",
        badgeRequired = await PlanFeatureCatalog.EffectiveAsync(db, tenant) is not { WhiteLabel: true } and not { BrandingTier: "full" or "premium" },
        referralLink = stats.Link,
        referred = stats.Referred,
        paying = stats.Paying,
        freeMonths = stats.FreeMonthsLeft,
        bonusDays = ReferralService.ReferredBonusDays,
        rewardsLeft = Math.Max(0, ReferralService.MaxRewardsPerYear - stats.RewardsThisYear),
        viaStripe = !string.IsNullOrWhiteSpace(tenant.StripeSubscriptionId)
    });
});

// Ocena aplikacji wysyłana przez trenera z instancji tenanta (Zarządzanie → „Oceń aplikację”).
app.MapPost("/api/internal/tenants/{slug}/feedback", async (
    string slug,
    AppFeedbackRequest body,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    if (body.Rating is < 1 or > 5) return Results.BadRequest();

    static string? Clip(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(v.Trim().Length, max)];
    db.AppFeedbacks.Add(new AppFeedback
    {
        TenantId = tenant.Id,
        Rating = body.Rating,
        Text = Clip(body.Text, 2000),
        AuthorEmail = Clip(body.AuthorEmail, 256),
        ContactEmail = Clip(body.ContactEmail, 256),
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
    return Results.Ok();
});

// Zgłoszenie błędu z aplikacji trenera (przycisk „Zgłoś błąd”): komentarz, zrzut ekranu, kontekst strony.
app.MapPost("/api/internal/tenants/{slug}/bug-reports", async (
    string slug,
    BugReportService.Incoming body,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    BugReportService bugs,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var (ok, error, id) = await bugs.SaveAsync(tenant, body);
    if (!ok) return Results.BadRequest(new { error });
    await bugs.NotifyAsync(id);
    return Results.Ok(new { id });
});

// Zrzut ekranu ze zgłoszenia błędu — tylko dla administratora Portalu.
app.MapGet("/panel/bledy/{id:int}/zrzut", async (int id, BugReportService bugs, HttpContext ctx) =>
{
    if (await bugs.ScreenshotAsync(id) is not { } shot) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(shot.Data, shot.Type);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Dokumenty prawne w aplikacji trenera: które aktualne wersje czekają na akceptację właściciela
// (np. po zmianie regulaminu albo gdy aplikację założył administrator) i zapis akceptacji.
app.MapGet("/api/internal/tenants/{slug}/legal", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    LegalService legal,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var portal = PublicBase(ctx);
    var pending = await legal.PendingForTenantAsync(tenant.Id);
    // Treść dokumentów od razu w odpowiedzi — aplikacja trenera pokazuje je w oknie akceptacji, bez otwierania
    // nowej karty (w zainstalowanej aplikacji na telefonie nowa karta to pusty ekran).
    var docs = new List<object>();
    foreach (var d in pending)
        docs.Add(new { key = d.Key, title = d.Title, version = d.Version, url = portal + d.Path, html = await legal.RenderAsync(d.Key) });
    return Results.Json(new
    {
        platformName = (await legal.CompanyAsync()).Brand,
        pending = docs
    });
});

app.MapPost("/api/internal/tenants/{slug}/legal/accept", async (
    string slug,
    LegalAcceptRequest body,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    LegalService legal,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Email)) return Results.BadRequest();
    var keys = (body.Keys ?? []).Where(k => LegalService.Docs.Any(d => d.Key == k)).ToList();
    if (keys.Count == 0) keys = (await legal.PendingForTenantAsync(tenant.Id)).Select(d => d.Key).ToList();
    if (keys.Count > 0) await legal.RecordAsync(tenant.Id, body.Email, keys, body.Ip, body.UserAgent, "aplikacja");
    return Results.Ok();
});

// Poczta w Panelu: Brevo (Inbound Parsing) przekazuje tu e-maile wysłane na adres kontaktowy.
// Adres zawiera tajny ciąg z ustawień — bez niego 404. Brevo ponawia przy błędzie, duplikaty odsiewamy po Message-ID.
app.MapPost("/api/inbound/brevo/{secret}", async (string secret, HttpRequest req, InboxService inbox) =>
{
    if (!await inbox.SecretMatchesAsync(secret)) return Results.NotFound();
    if (req.ContentLength is > 30_000_000) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    System.Text.Json.JsonDocument doc;
    try { doc = await System.Text.Json.JsonDocument.ParseAsync(req.Body); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(); }
    using (doc)
    {
        var added = await inbox.ReceiveAsync(doc.RootElement);
        return Results.Ok(new { received = added });
    }
}).AllowAnonymous().DisableAntiforgery();

// Załącznik z Poczty — pobierany z Brevo w chwili kliknięcia, tylko dla administratora, zawsze jako plik do zapisania.
app.MapGet("/panel/poczta/zalacznik/{messageId:int}/{index:int}", async (int messageId, int index, InboxService inbox) =>
{
    if (await inbox.AttachmentAsync(messageId, index) is not { } file) return Results.NotFound();
    var type = file.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase) || file.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase)
        || file.ContentType.Contains("xml", StringComparison.OrdinalIgnoreCase) || file.ContentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
        ? "application/octet-stream" : file.ContentType;
    return Results.File(file.Data, type, file.Name);
}).RequireAuthorization(p => p.RequireRole("Admin"));

// Ikona nastroju maskotki do e-maili (PNG — Gmail i Outlook nie wyświetlają SVG). Publiczna, bez danych.
app.MapGet("/assistant/mascot/{mood}.png", async (string mood, AssistantService assistant, IWebHostEnvironment env, HttpContext ctx) =>
{
    if (await assistant.EmailImageAsync(mood, env.WebRootPath) is not { } img) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(img.Data, img.ContentType);
});

// Agent Portalu (Leo) w porannym raporcie administratora — te same zasady co maskotka trenerów.
app.MapGet("/assistant/leo/{mood}.png", async (string mood, AssistantService assistant, IWebHostEnvironment env, HttpContext ctx) =>
{
    if (await assistant.EmailImageAsync(mood, env.WebRootPath, AssistantService.Leo) is not { } img) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(img.Data, img.ContentType);
});

// Asystent-maskotka w aplikacji trenera: wygląd (imię, ikony nastrojów, animacja) i teksty podpowiedzi
// ubrane w słowa przez AI. Co pokazać, liczy aplikacja trenera; bez AI wraca jej tekst zapasowy.
app.MapGet("/api/internal/tenants/{slug}/assistant", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    AssistantService assistant,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var c = await assistant.GetConfigAsync(tenant);
    var version = await assistant.IconsVersionAsync();
    var emailIcons = MascotArt.Moods.ToDictionary(m => m.Key, m => $"{PublicBase(ctx)}/assistant/mascot/{m.Key}.png?v={version}");
    return Results.Json(new { enabled = c.Enabled, name = c.Name, animation = c.Animation, ai = c.Ai, icons = c.Icons, emailIcons,
        layout = c.Layout, animatedSvg = c.AnimatedSvg, motion = c.Motion, effects = c.Effects, planAi = await assistant.PlanAiAvailableAsync(tenant) });
});

// „Zaproponuj plan” w aplikacji trenera — AI dopracowuje szkic z reguł (bez danych osobowych), aplikacja sprawdza wynik.
app.MapPost("/api/internal/tenants/{slug}/assistant/plan", async (
    string slug,
    AssistantPlanRequest body,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    AssistantService assistant,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Payload) || body.Payload.Length > 60_000) return Results.BadRequest();
    var r = await assistant.PlanAsync(tenant, body.Payload);
    return Results.Json(new { text = r.Text, ai = r.FromAi, error = r.Error });
});

app.MapPost("/api/internal/tenants/{slug}/assistant/phrase", async (
    string slug,
    AssistantPhraseRequest body,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    AssistantService assistant,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Kind) || string.IsNullOrWhiteSpace(body.Fallback) || body.Fallback.Length > 600) return Results.BadRequest();
    var facts = (body.Facts ?? []).Take(20)
        .Where(f => f.Key.Length <= 40 && (f.Value?.Length ?? 0) <= 160)
        .ToDictionary(f => f.Key, f => f.Value ?? "");
    var phrase = await assistant.PhraseAsync(tenant, body.Kind[..Math.Min(40, body.Kind.Length)], facts, body.Fallback,
        audience: body.Audience == "klient" ? "klient" : "trener");
    return Results.Json(new { text = phrase.Text, ai = phrase.FromAi });
});

// Szkic wiadomości trenera do klienta w stylu trenera (profil + przykłady). Imiona klientów zostają w aplikacji ([klient]).
app.MapPost("/api/internal/tenants/{slug}/assistant/draft", async (
    string slug,
    AssistantDraftRequest body,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    AssistantService assistant,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.Purpose) || string.IsNullOrWhiteSpace(body.Fallback) || body.Fallback.Length > 1000) return Results.BadRequest();
    var facts = (body.Facts ?? []).Take(20)
        .Where(f => f.Key.Length <= 40 && (f.Value?.Length ?? 0) <= 120)
        .ToDictionary(f => f.Key, f => f.Value ?? "");
    static string? Cut(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().Length <= max ? v.Trim() : v.Trim()[..max];
    var style = new AssistantService.TrainerStyleInfo(
        body.Address == "pan" ? "pan" : "ty",
        body.Emoji is "brak" or "często" ? body.Emoji : "czasem",
        body.Length == "srednio" ? "srednio" : "krotko",
        Cut(body.Greeting, 40), Cut(body.Signature, 80), Cut(body.Notes, 300));
    var examples = (body.Examples ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Take(5).Select(e => e.Length > 600 ? e[..600] : e).ToList();
    var draft = await assistant.DraftInStyleAsync(tenant, body.Purpose[..Math.Min(40, body.Purpose.Length)], facts, body.Fallback, style, examples);
    return Results.Json(new { text = draft.Text, ai = draft.FromAi });
});

// ---- Store API ----
// Tenant apps call these endpoints to fetch their service catalog and place orders.
// Secured by the same shared secret used for internal endpoints.
app.MapGet("/api/store/{slug}", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    StorePaymentService storePayment,
    AddonService addons,
    BillingService billing,
    SiteSettingsService siteSettings) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    // Bez bramki SMS na platformie pakiety SMS nie trafiają do sklepu trenera.
    var smsOn = await siteSettings.PlatformSmsOnAsync();
    var items = await db.ServiceItems.AsNoTracking()
        .Where(s => s.IsActive && (smsOn || s.FulfillmentType != "credit_sms"))
        .OrderBy(s => s.SortOrder)
        .ToListAsync();

    var overrides = await db.TenantServicePrices.AsNoTracking()
        .Where(p => p.TenantId == tenant.Id)
        .ToDictionaryAsync(p => p.ServiceItemId);

    var visible = items.Where(s => !overrides.TryGetValue(s.Id, out var ov) || !ov.IsHidden).ToList();
    var autoBill = new Dictionary<int, bool>();
    foreach (var s in visible) autoBill[s.Id] = await addons.CanAutoBillAsync(tenant, s);

    var catalog = visible
        .Select(s =>
        {
            var price = overrides.TryGetValue(s.Id, out var ov) ? ov.CustomPrice : s.DefaultPrice;
            return new
            {
                s.Id, s.Name, s.Description, s.Category, s.Icon,
                Price = price, s.PriceType, s.Unit,
                // Dodatek miesięczny: podnosi limit, dopóki jest aktywny. autoBilling = dopisanie do abonamentu kartą.
                MonthlyAddon = AddonService.IsMonthlyAddon(s),
                AutoBilling = autoBill[s.Id],
                AddonKind = s.FulfillmentType switch
                {
                    "credit_cdn_bandwidth" => "bandwidth",
                    "credit_cdn_storage" => "storage",
                    "credit_sms" => "sms",
                    _ => null
                }
            };
        })
        .ToList();

    var active = (await addons.GetActiveAsync(tenant.Id)).Select(a => new
    {
        a.Id, a.ServiceItemId, Name = a.ServiceItem?.Name, a.Quantity, a.StartedAt,
        AutoBilling = a.StripeSubscriptionItemId != null,
        Price = overrides.TryGetValue(a.ServiceItemId, out var ov) ? ov.CustomPrice : a.ServiceItem?.DefaultPrice ?? 0
    });

    var gateways = await storePayment.GetAvailableGatewaysAsync();

    // Rachunki trenera (Portal wystawia je co miesiąc) — trener widzi je w sklepie i płaci jednym kliknięciem.
    var today = BillingService.Today;
    var bills = (await billing.ListAsync(tenant.Id, 12))
        .Where(b => b.Status != TenantBillStatus.Cancelled)
        .Select(b => new
        {
            b.Number,
            Period = BillingService.PeriodLabel(b.PeriodStart),
            b.Amount,
            Paid = b.Status == TenantBillStatus.Paid,
            DueDate = b.DueDate.ToString("yyyy-MM-dd"),
            Overdue = b.Status == TenantBillStatus.Issued && today > b.DueDate,
            PayUrl = billing.PayUrl(b)
        })
        .Take(6)
        .ToList();

    return Results.Json(new { tenantId = tenant.Id, companyName = tenant.CompanyName, catalog, gateways, addons = active, bills });
});

// Dodatki miesięczne: dopisanie do abonamentu (Stripe) i rezygnacja.
app.MapPost("/api/store/{slug}/addons", async (
    string slug, AddonRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, AddonService addons) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    // Kartą (Stripe) albo do miesięcznego rachunku — w obu przypadkach limit rośnie od razu.
    var (ok, error, mode) = await addons.AddAsync(tenant.Id, body.ServiceItemId);
    return ok ? Results.Ok(new { ok = true, mode }) : Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);
});

app.MapDelete("/api/store/{slug}/addons/{id:int}", async (
    string slug, int id, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, AddonService addons) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var (ok, error) = await addons.CancelAsync(tenant.Id, id);
    return ok ? Results.Ok(new { ok = true }) : Results.Json(new { error }, statusCode: StatusCodes.Status409Conflict);
});

app.MapPost("/api/store/{slug}/order", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    StorePaymentService storePayment,
    AddonService addons,
    CreditService creditService,
    TenantService tenantService,
    StoreTicketService tickets,
    ILoggerFactory loggerFactory) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(body);
    var root = doc.RootElement;

    if (!root.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != System.Text.Json.JsonValueKind.Array)
        return Results.BadRequest(new { error = "Brak elementów zamówienia." });

    var notes = root.TryGetProperty("notes", out var n) ? n.GetString() : null;
    var gateway = root.TryGetProperty("gateway", out var gw) ? gw.GetString() : null;
    var returnUrl = root.TryGetProperty("returnUrl", out var ru) ? ru.GetString() : null;

    var overrides = await db.TenantServicePrices.AsNoTracking()
        .Where(p => p.TenantId == tenant.Id)
        .ToDictionaryAsync(p => p.ServiceItemId);

    var orderGroupId = Guid.NewGuid().ToString("N");
    var usePayment = !string.IsNullOrWhiteSpace(gateway);

    var orders = new List<ServiceOrder>();
    var items = new Dictionary<int, ServiceItem>();
    var addonsAdded = 0;
    foreach (var itemEl in itemsEl.EnumerateArray())
    {
        var serviceItemId = itemEl.GetInt32();
        var serviceItem = await db.ServiceItems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceItemId && s.IsActive);
        if (serviceItem is null) continue;

        if (overrides.TryGetValue(serviceItemId, out var ov) && ov.IsHidden) continue;

        // Dodatek miesięczny bez płatności online: od razu do abonamentu (jak przycisk „Dodaj do abonamentu”).
        if (!usePayment && AddonService.IsMonthlyAddon(serviceItem))
        {
            var (added, _, _) = await addons.AddAsync(tenant.Id, serviceItemId);
            if (added) addonsAdded++;
            continue;
        }
        items[serviceItemId] = serviceItem;

        var price = overrides.TryGetValue(serviceItemId, out var ovp) ? ovp.CustomPrice : serviceItem.DefaultPrice;

        orders.Add(new ServiceOrder
        {
            TenantId = tenant.Id,
            ServiceItemId = serviceItemId,
            Price = price,
            Notes = notes,
            OrderGroupId = orderGroupId,
            PaymentGateway = usePayment ? gateway : null,
            Status = usePayment ? ServiceOrderStatus.AwaitingPayment : ServiceOrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
    }

    if (orders.Count == 0)
        return addonsAdded > 0
            ? Results.Json(new { success = true, count = 0, addons = addonsAdded, fulfilled = 0, tickets = 0 })
            : Results.BadRequest(new { error = "Żaden z wybranych elementów nie jest dostępny." });

    db.ServiceOrders.AddRange(orders);
    await db.SaveChangesAsync();

    if (!usePayment)
    {
        // Bez płatności online: doładowania (np. SMS) działają od razu, a kwota trafia na najbliższy rachunek;
        // usługi z realizacją stają się zgłoszeniami „do zrobienia” z e-mailem / SMS-em do administratora.
        var ticketIds = new List<int>();
        foreach (var o in orders)
        {
            // Usługę doliczamy dopiero po wykonaniu (Zgłoszenia → Zakończ), żeby odrzucona nie trafiła na rachunek.
            if (StoreTicketService.IsTicket(items[o.ServiceItemId])) ticketIds.Add(o.Id);
            else StoreTicketService.ChargeToBill(db, o, items[o.ServiceItemId]);
        }
        await db.SaveChangesAsync();

        var fulfilled = 0;
        foreach (var o in orders.Where(o => !StoreTicketService.IsTicket(items[o.ServiceItemId])))
        {
            try { await creditService.FulfillOrderAsync(o, items[o.ServiceItemId]); fulfilled++; }
            catch (Exception ex) { loggerFactory.CreateLogger("StoreOrders").LogError(ex, "Automatyczna realizacja zamówienia #{Id} nie powiodła się.", o.Id); }
        }
        if (fulfilled > 0) await tenantService.PushEntitlementsAsync(tenant.Id);
        await tickets.NotifyNewTicketsAsync(ticketIds);

        return Results.Json(new
        {
            success = true, count = orders.Count, orderIds = orders.Select(o => o.Id).ToArray(),
            addons = addonsAdded, fulfilled, tickets = ticketIds.Count
        });
    }

    if (usePayment && !string.IsNullOrWhiteSpace(returnUrl))
    {
        var totalAmount = orders.Sum(o => o.Price);
        var description = orders.Count == 1
            ? orders[0].Notes ?? "Zamówienie usługi Hantly"
            : $"Zamówienie {orders.Count} usług Hantly";

        var portalUrl = config.GetValue<string>("Portal:PublicUrl") ?? "";
        var (paymentUrl, externalId, error) = await storePayment.CreatePaymentAsync(
            gateway!, totalAmount, description, orderGroupId, returnUrl, portalUrl, tenant.OwnerEmail);

        if (error is not null)
        {
            foreach (var o in orders) db.ServiceOrders.Remove(o);
            await db.SaveChangesAsync();
            return Results.Json(new { success = false, error = $"Błąd bramki płatności: {error}" },
                statusCode: 502);
        }

        foreach (var o in orders)
            o.PaymentExternalId = externalId;
        await db.SaveChangesAsync();

        return Results.Json(new
        {
            success = true,
            count = orders.Count,
            orderIds = orders.Select(o => o.Id).ToArray(),
            paymentUrl,
            paymentGateway = gateway
        });
    }

    return Results.Json(new { success = true, count = orders.Count, orderIds = orders.Select(o => o.Id).ToArray() });
});

// ---- Credits API ----
// Tenant apps call these to check credit balances and deduct SMS credits.
app.MapGet("/api/credits/{slug}", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    CreditService creditService) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    var balances = await creditService.GetBalancesAsync(tenant.Id);
    var sms = await creditService.GetSmsStatusAsync(tenant.Id);
    var platformBunnyConfigured = await db.Set<SiteSetting>()
        .AnyAsync(s => s.Key == SiteSettingsService.Keys.PlatformBunnyLibraryId && s.Value != null && s.Value != "");

    return Results.Json(new
    {
        sms = balances.GetValueOrDefault("sms", 0),
        cdnStorageGb = balances.GetValueOrDefault("cdn_storage_gb", 0),
        cdnBandwidthGb = balances.GetValueOrDefault("cdn_bandwidth_gb", 0),
        platformSmsEnabled = sms.PlatformEnabled,
        smsMonthlyLimit = sms.MonthlyLimit,
        smsMonthlyUsed = sms.MonthlyUsed,
        platformCdnEnabled = platformBunnyConfigured
    });
});

app.MapPost("/api/credits/{slug}/sms/send", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    CreditService creditService) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(body);
    var root = doc.RootElement;

    var phone = root.GetProperty("phone").GetString() ?? "";
    var message = root.GetProperty("message").GetString() ?? "";

    var result = await creditService.SendSmsCentralizedAsync(tenant.Id, phone, message);

    var balances = await creditService.GetBalancesAsync(tenant.Id);

    return Results.Json(new
    {
        success = result.Success,
        error = result.Error,
        quotaExceeded = result.QuotaExceeded,
        remaining = balances.GetValueOrDefault("sms", 0)
    });
});

app.MapPost("/api/credits/{slug}/sms/test", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    SiteSettingsService settingsService) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    var token = await settingsService.GetAsync(SiteSettingsService.Keys.PlatformSmsApiToken);
    if (string.IsNullOrWhiteSpace(token))
        return Results.Json(new { success = false, error = "Platforma SMS nie skonfigurowana." });

    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    using var doc = System.Text.Json.JsonDocument.Parse(body);
    var phone = doc.RootElement.GetProperty("phone").GetString() ?? "";

    var senderName = await settingsService.GetAsync(SiteSettingsService.Keys.PlatformSmsSenderName);

    var digits = new string(phone.Where(char.IsDigit).ToArray());
    var normalized = digits.Length switch
    {
        9 => "48" + digits,
        11 when digits.StartsWith("48") => digits,
        _ when digits.Length > 9 => digits,
        _ => (string?)null
    };

    if (normalized is null)
        return Results.Json(new { success = false, error = "Nieprawidłowy numer telefonu." });

    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.smsapi.pl/sms.do");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var fields = new Dictionary<string, string>
        {
            ["to"] = normalized,
            ["message"] = "Test SMS Hantly — konfiguracja dziala poprawnie.",
            ["format"] = "json"
        };
        if (!string.IsNullOrWhiteSpace(senderName))
            fields["from"] = senderName;
        req.Content = new FormUrlEncodedContent(fields);
        var resp = await http.SendAsync(req);
        var respBody = await resp.Content.ReadAsStringAsync();
        using var respDoc = System.Text.Json.JsonDocument.Parse(respBody);
        if (respDoc.RootElement.TryGetProperty("error", out _))
        {
            var msg = respDoc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "Błąd SMSAPI";
            return Results.Json(new { success = false, error = msg });
        }
        return Results.Json(new { success = true });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, error = ex.Message });
    }
});

// Google Calendar / Meet trenerów przez klienta OAuth platformy. Instancja prosi
// o adres zgody, Google wraca do Portalu, a instancja pobiera potem krótkie tokeny.
app.MapGet("/api/internal/tenants/{slug}/google/status", async (
    string slug, string userKey, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, GoogleOAuthBroker google) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var grant = await google.GetGrantAsync(tenant.Id, userKey);
    return Results.Json(new { available = await google.GetConfigAsync() is not null, connected = grant is not null, email = grant?.GoogleEmail });
});

app.MapPost("/api/internal/tenants/{slug}/google/authorize", async (
    string slug, GoogleAuthorizeRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, GoogleOAuthBroker google) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var (url, error) = await google.BuildAuthorizeUrlAsync(tenant, body.UserKey, body.ReturnUrl);
    return url is null ? Results.BadRequest(new { error }) : Results.Json(new { url });
});

app.MapPost("/api/internal/tenants/{slug}/google/token", async (
    string slug, GoogleUserRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, GoogleOAuthBroker google) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var (token, expiresIn, email, revoked) = await google.GetAccessTokenAsync(tenant.Id, body.UserKey);
    if (token is null)
        return revoked ? Results.NotFound(new { connected = false }) : Results.StatusCode(StatusCodes.Status502BadGateway);
    return Results.Json(new { accessToken = token, expiresIn, email });
});

app.MapPost("/api/internal/tenants/{slug}/google/disconnect", async (
    string slug, GoogleUserRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, GoogleOAuthBroker google) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    await google.DisconnectAsync(tenant.Id, body.UserKey);
    return Results.Ok();
});

app.MapGet(GoogleOAuthBroker.CallbackPath, async (string? code, string? state, string? error, GoogleOAuthBroker google, OffsiteBackupService offsite) =>
{
    // Ten sam adres powrotu obsługuje połączenie Dysku Google dla kopii zapasowych Portalu.
    if (state?.StartsWith(OffsiteBackupService.GdriveStatePrefix, StringComparison.Ordinal) == true)
        return Results.Redirect(await offsite.HandleDriveCallbackAsync(code, state, error));
    var (redirectTo, _) = await google.HandleCallbackAsync(code, state, error);
    return Results.Redirect(redirectTo);
});

// Poczta instancji trenerów: Portal wysyła w ich imieniu przez SMTP platformy.
// Instancje nie znają hasła serwera, a każda ma własny dzienny limit.
app.MapGet("/api/internal/tenants/{slug}/mail/status", async (
    string slug, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, TenantMailRelay relay) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    return Results.Json(new
    {
        enabled = await relay.IsEnabledAsync(),
        dailyLimit = await relay.GetDailyLimitAsync(),
        sentToday = await relay.SentTodayAsync(tenant.Id)
    });
});

app.MapPost("/api/internal/tenants/{slug}/mail", async (
    string slug, MailRelayRequest body, HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config, TenantMailRelay relay) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var (status, error) = await relay.SendAsync(tenant, body);
    return status == 200 ? Results.Ok(new { sent = true }) : Results.Json(new { error }, statusCode: status);
});

// ---- Bunny CDN credentials API ----
// Każda instancja dostaje klucz WYŁĄCZNIE swojej biblioteki (zakładanej przy pierwszym
// użyciu kluczem konta platformy).
app.MapGet("/api/credits/{slug}/bunny", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    // Zgodność ze starszymi aplikacjami: tylko istniejąca osobna biblioteka (nowych już nie zakładamy).
    // Nowe filmy trafiają do centralnej biblioteki przez /api/internal/tenants/{slug}/video — klucz zostaje w Portalu.
    if (string.IsNullOrEmpty(tenant.BunnyLibraryId) || string.IsNullOrEmpty(tenant.BunnyLibraryApiKey))
        return Results.Json(new { enabled = false });
    return Results.Json(new { enabled = true, apiKey = tenant.BunnyLibraryApiKey, libraryId = tenant.BunnyLibraryId, cdnHostname = tenant.BunnyCdnHostname });
});

app.MapVideoHub();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Wspólna kontrola nagłówka X-Internal-Secret dla wywołań tenant → Portal.
// Pusty sekret = odmowa (wcześniej oznaczał brak jakiejkolwiek kontroli).
record AssistantPlanRequest(string? Payload);
record AppFeedbackRequest(int Rating, string? Text, string? ContactEmail, string? AuthorEmail);
record AssistantPhraseRequest(string Kind, Dictionary<string, string?>? Facts, string Fallback, string? Audience);
record AssistantDraftRequest(string Purpose, Dictionary<string, string?>? Facts, string Fallback, string? Address, string? Emoji, string? Length,
    string? Greeting, string? Signature, string? Notes, List<string>? Examples);
record LegalAcceptRequest(string Email, string[]? Keys, string? Ip, string? UserAgent);

record GoogleAuthorizeRequest(string UserKey, string ReturnUrl);

// Slug instancji z adresów API wołanych przez aplikacje trenerów (null = inne ścieżki, bez limitu).
partial class Program
{
    internal static string? TenantApiSlug(PathString path)
    {
        var segs = (path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length >= 4 && segs[0] == "api" && segs[1] == "internal" && segs[2] == "tenants") return segs[3].ToLowerInvariant();
        if (segs.Length >= 3 && segs[0] == "api" && (segs[1] == "credits" || segs[1] == "store")) return segs[2].ToLowerInvariant();
        return null;
    }
}
record GoogleUserRequest(string UserKey);
record AddonRequest(int ServiceItemId);

static class InternalAuth
{
    /// <summary>
    /// Wywołanie tenant → Portal wyłącznie własnym sekretem instancji. Wspólny sekret
    /// platformy nie jest tu akceptowany: znały go wszystkie starsze instancje, więc jedna
    /// mogłaby wydawać SMS-y, pobierać klucze wideo czy tokeny Google innej. Instancja bez
    /// własnego sekretu dostaje odmowę, dopóki w Portalu nie klikniesz „Nadaj sekrety”.
    /// </summary>
    public static bool IsAuthorizedFor(HttpContext ctx, Tenant tenant, IConfiguration config) =>
        !string.IsNullOrEmpty(tenant.InternalSecret)
        && TenantSecrets.Matches(ctx.Request.Headers["X-Internal-Secret"].ToString(), tenant.InternalSecret);
}
