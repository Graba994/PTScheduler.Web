using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using PTScheduler.Application;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Web;
using PTScheduler.Web.Components;
using PTScheduler.Web.Components.Account;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("connections.json", optional: true, reloadOnChange: true);

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "data", "keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("PTScheduler");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton<IWebRootPathProvider, WebRootPathProvider>();

var app = builder.Build();

// Apply pending EF Core migrations, then seed roles + session types
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PTScheduler.Infrastructure.Data.ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await PTScheduler.Infrastructure.Data.DbInitializer.SeedRolesAsync(roleManager);
    await PTScheduler.Infrastructure.Data.DbInitializer.SeedSessionTypesAsync(db);
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

// Backup download endpoint (admin only)
app.MapGet("/admin/backup/download", async (
    PTScheduler.Application.Interfaces.IBackupService backupService,
    HttpContext ctx) =>
{
    if (!ctx.User.IsInRole(PTScheduler.Domain.Constants.Roles.Admin))
        return Results.Forbid();

    var data = await backupService.ExportAsync();
    return Results.File(data, "application/octet-stream",
        $"ptscheduler_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql");
}).RequireAuthorization();

// Checkout: create order + redirect to payment gateway
app.MapPost("/api/checkout", async (
    HttpContext ctx,
    PTScheduler.Application.Interfaces.IShopService shopService,
    PTScheduler.Application.Interfaces.IPaymentGateway paymentGateway) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var productIdStr = form["productId"].ToString();
    var email = form["email"].ToString().Trim();
    var name = form["name"].ToString().Trim();

    if (!int.TryParse(productIdStr, out var productId) || string.IsNullOrEmpty(email))
        return Results.BadRequest("Brakuje wymaganych danych.");

    if (!paymentGateway.IsConfigured)
        return Results.Problem("Bramka płatności nie jest skonfigurowana.");

    var product = await shopService.GetProductAsync(productId);
    if (product is null || !product.IsActive)
        return Results.NotFound("Produkt nie istnieje lub jest nieaktywny.");

    var orderId = await shopService.CreateOrderAsync(
        new PTScheduler.Application.DTOs.CheckoutRequest
        {
            ProductId = productId,
            Email = email,
            Name = string.IsNullOrEmpty(name) ? null : name
        },
        paymentGateway.ProviderName);

    var request = new PTScheduler.Application.Interfaces.PaymentRequest
    {
        OrderId = orderId.ToString(),
        Description = product.Name,
        Amount = product.Price,
        Currency = product.Currency,
        CustomerEmail = email,
        CustomerName = string.IsNullOrEmpty(name) ? null : name,
        CustomerIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1",
        NotifyUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/api/payu/notify",
        ContinueUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/shop/thank-you"
    };

    try
    {
        var redirect = await paymentGateway.CreatePaymentAsync(request);
        return Results.Redirect(redirect.RedirectUrl);
    }
    catch (Exception ex)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Checkout");
        logger.LogError(ex, "Checkout failed for order {OrderId}", orderId);
        await shopService.UpdateOrderStatusAsync(orderId, PTScheduler.Domain.Enums.OrderStatus.Failed);
        return Results.Problem("Nie udało się utworzyć płatności. Spróbuj ponownie.");
    }
});

// PayU webhook notification
app.MapPost("/api/payu/notify", async (
    HttpContext ctx,
    PTScheduler.Application.Interfaces.IPaymentGateway paymentGateway,
    PTScheduler.Application.Interfaces.IShopService shopService) =>
{
    var signature = ctx.Request.Headers["OpenPayu-Signature"].FirstOrDefault();
    ctx.Request.EnableBuffering();
    var notification = await paymentGateway.ParseNotificationAsync(ctx.Request.Body, signature);

    if (notification is null)
        return Results.BadRequest("Nieprawidłowy podpis.");

    if (!int.TryParse(notification.InternalOrderId, out var orderId))
        return Results.BadRequest("Nieprawidłowe extOrderId.");

    var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PayUWebhook");
    logger.LogInformation("PayU notification: order={OrderId} status={Status}", orderId, notification.Status);

    if (notification.Status == "COMPLETED")
    {
        await shopService.MarkOrderPaidAsync(orderId, notification.ExternalOrderId);
        await shopService.FulfillOrderAsync(orderId);
    }
    else if (notification.Status == "CANCELED")
    {
        await shopService.UpdateOrderStatusAsync(orderId, PTScheduler.Domain.Enums.OrderStatus.Cancelled);
    }

    return Results.Ok();
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.Run();
