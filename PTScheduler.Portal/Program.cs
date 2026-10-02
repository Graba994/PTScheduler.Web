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
builder.Services.AddScoped<GoogleOAuthBroker>();
builder.Services.AddScoped<TenantMailRelay>();
builder.Services.AddScoped<StoreTicketService>();
builder.Services.AddScoped<AddonService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<StorePaymentService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddHostedService<BillingBackgroundService>();
builder.Services.AddScoped<ErrorAlertService>();
builder.Services.AddHostedService<ErrorAlertBackgroundService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<OffsiteBackupService>();
builder.Services.AddScoped<BackupMaintenanceService>();
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

// Każda instancja ma własną pulę zapytań do API Portalu (SMS, poczta, wideo, Google, sklep):
// zapętlona albo przejęta instancja jednego trenera nie spowolni Portalu pozostałym.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
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
    else if (admins.Count > 1)
    {
        var legacy = admins.FirstOrDefault(a => string.Equals(a.Email, adminEmail, StringComparison.OrdinalIgnoreCase));
        if (legacy is not null && await userManager.CheckPasswordAsync(legacy, legacyAdminPassword))
            await userManager.DeleteAsync(legacy);
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
app.UseAntiforgery();

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

    await using var db = dbFactory.CreateDbContext();
    db.LoginLogs.Add(new LoginLog
    {
        Email = email,
        Success = result.Succeeded,
        IpAddress = ip,
        UserAgent = ua,
        FailureReason = result.Succeeded ? null
            : result.IsLockedOut ? "locked_out"
            : result.IsNotAllowed ? "not_allowed"
            : "invalid_credentials"
    });
    await db.SaveChangesAsync();

    if (result.Succeeded)
        return Results.Redirect(returnUrl);

    if (result.IsLockedOut)
        return Results.Redirect("/login?error=locked");

    return Results.Redirect("/login?error=1");
});

app.MapGet("/api/account/logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Redirect("/logout");
});

// Stripe webhook — Stripe posts events here as JSON with a signature
// header. Return 200 quickly; Stripe will retry on any non-2xx.
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
app.MapGet("/api/webhooks/autopay/return", async (HttpContext ctx, IDbContextFactory<PortalDbContext> dbFactory, BillingService billing) =>
{
    var orderId = AutopayProtocol.SafeOrderId(ctx.Request.Query["OrderID"].ToString());
    if (orderId is null) return Results.Redirect("/");
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

    return Results.File(entry.FilePath, "application/gzip", Path.GetFileName(entry.FilePath));
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
    var pdf = OfferPdf.Render(offer, seller, "PTScheduler");
    return Results.File(pdf, "application/pdf", $"oferta-{offer.Tenant.Slug}-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
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
    IConfiguration config) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();
    var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tenant.PlanId);
    if (plan is null) return Results.NotFound();
    return Results.Content(TenantService.SerializeEntitlements(plan, await AddonService.ExtraLimitsAsync(db, tenant.Id)), "application/json");
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
    BillingService billing) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    var items = await db.ServiceItems.AsNoTracking()
        .Where(s => s.IsActive)
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
            ? orders[0].Notes ?? "Zamówienie usługi PTScheduler"
            : $"Zamówienie {orders.Count} usług PTScheduler";

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
        .AnyAsync(s => s.Key == "platform_bunny_account_key" && s.Value != null && s.Value != "");

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
            ["message"] = "Test SMS PTScheduler — konfiguracja dziala poprawnie.",
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
    IConfiguration config,
    BunnyPlatformService bunny) =>
{
    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null || !InternalAuth.IsAuthorizedFor(ctx, tenant, config)) return Results.Unauthorized();

    // Tylko własna biblioteka instancji. Wspólnej biblioteki nie udostępniamy — jej klucz
    // pozwalałby każdemu trenerowi oglądać i usuwać filmy pozostałych.
    var library = await bunny.EnsureLibraryAsync(tenant.Id, ctx.RequestAborted);
    return library is null
        ? Results.Json(new { enabled = false })
        : Results.Json(new { enabled = true, apiKey = library.ApiKey, libraryId = library.LibraryId, cdnHostname = library.CdnHostname });
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Wspólna kontrola nagłówka X-Internal-Secret dla wywołań tenant → Portal.
// Pusty sekret = odmowa (wcześniej oznaczał brak jakiejkolwiek kontroli).
record AppFeedbackRequest(int Rating, string? Text, string? ContactEmail, string? AuthorEmail);

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
