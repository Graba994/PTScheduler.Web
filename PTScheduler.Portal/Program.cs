using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Components;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContextFactory<PortalDbContext>(options =>
{
    var conn = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Port=5432;Database=ptportal;Username=ptportal;Password=ptportal";
    options.UseNpgsql(conn);
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
});

builder.Services.AddAuthorization();

builder.Services.AddSingleton<DockerService>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<SiteSettingsService>();
builder.Services.AddScoped<NpmService>();
builder.Services.AddScoped<UpdateService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddScoped<StorePaymentService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddSingleton<UpdateNotifier>();
builder.Services.AddHostedService<UpdatePollerService>();
builder.Services.AddHostedService<TrialExpirationService>();
builder.Services.AddHostedService<HealthMonitorService>();
builder.Services.AddHostedService<TenantCleanupService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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
    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
        await userManager.CreateAsync(admin, "Admin123!");
        await userManager.AddToRoleAsync(admin, "Admin");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "P24 webhook processing failed");
    }

    return Results.Ok();
});

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

// ---- Store API ----
// Tenant apps call these endpoints to fetch their service catalog and place orders.
// Secured by the same shared secret used for internal endpoints.
app.MapGet("/api/store/{slug}", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    StorePaymentService storePayment) =>
{
    var secret = config.GetValue<string>("Portal:TenantInternalSecret") ?? "";
    if (!string.IsNullOrEmpty(secret) && ctx.Request.Headers["X-Internal-Secret"].ToString() != secret)
        return Results.Unauthorized();

    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null) return Results.NotFound();

    var items = await db.ServiceItems.AsNoTracking()
        .Where(s => s.IsActive)
        .OrderBy(s => s.SortOrder)
        .ToListAsync();

    var overrides = await db.TenantServicePrices.AsNoTracking()
        .Where(p => p.TenantId == tenant.Id)
        .ToDictionaryAsync(p => p.ServiceItemId);

    var catalog = items
        .Where(s => !overrides.TryGetValue(s.Id, out var ov) || !ov.IsHidden)
        .Select(s =>
        {
            var price = overrides.TryGetValue(s.Id, out var ov) ? ov.CustomPrice : s.DefaultPrice;
            return new
            {
                s.Id, s.Name, s.Description, s.Category, s.Icon,
                Price = price, s.PriceType, s.Unit
            };
        })
        .ToList();

    var gateways = await storePayment.GetAvailableGatewaysAsync();

    return Results.Json(new { tenantId = tenant.Id, companyName = tenant.CompanyName, catalog, gateways });
});

app.MapPost("/api/store/{slug}/order", async (
    string slug,
    HttpContext ctx,
    IDbContextFactory<PortalDbContext> dbFactory,
    IConfiguration config,
    StorePaymentService storePayment) =>
{
    var secret = config.GetValue<string>("Portal:TenantInternalSecret") ?? "";
    if (!string.IsNullOrEmpty(secret) && ctx.Request.Headers["X-Internal-Secret"].ToString() != secret)
        return Results.Unauthorized();

    await using var db = dbFactory.CreateDbContext();
    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug);
    if (tenant is null) return Results.NotFound();

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
    foreach (var itemEl in itemsEl.EnumerateArray())
    {
        var serviceItemId = itemEl.GetInt32();
        var serviceItem = await db.ServiceItems.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceItemId && s.IsActive);
        if (serviceItem is null) continue;

        if (overrides.TryGetValue(serviceItemId, out var ov) && ov.IsHidden) continue;

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
        return Results.BadRequest(new { error = "Żaden z wybranych elementów nie jest dostępny." });

    db.ServiceOrders.AddRange(orders);
    await db.SaveChangesAsync();

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
