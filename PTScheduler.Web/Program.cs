using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using PTScheduler.Application;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Web;
using PTScheduler.Web.Components;
using PTScheduler.Web.Components.Account;
using PTScheduler.Web.Services;

// Npgsql 6+ requires DateTime parameters for timestamptz columns to be Kind=Utc by default.
// This app stores LOCAL time (DateTime.Now is the convention; CreatedAt/StartTime/etc).
// The legacy switch lets Npgsql accept any DateTime kind, treating Unspecified/Local as local time.
// Must be set BEFORE the data source is built, hence the very top of Program.cs.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("connections.json", optional: true, reloadOnChange: true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Raise the Blazor Server circuit's SignalR receive limit so file uploads
// (logo, favicon, course covers) aren't truncated — the 32 KB default cut
// larger images in half.
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
});

// Surface real error details on the circuit so failures show a message
// instead of a silent "circuit terminated" during diagnosis.
builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DetailedErrors = true;
});

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
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddErrorDescriber<PolishIdentityErrorDescriber>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IEmailSender<ApplicationUser>, PTScheduler.Web.Components.Account.IdentityEmailSender>();
builder.Services.AddSingleton<IWebRootPathProvider, WebRootPathProvider>();
builder.Services.AddScoped<PTScheduler.Web.Services.HintStateService>();
builder.Services.AddScoped<PTScheduler.Web.Services.ToastService>();
builder.Services.AddSingleton<PTScheduler.Web.Services.EntitlementService>();
builder.Services.AddHostedService<SessionReminderService>();

// Tracks whether DB is reachable. Mutated at startup and via /db-error/retry.
builder.Services.AddSingleton<StartupHealth>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var path = ctx.Request.Path.Value ?? "";
        if (ctx.Request.Method == "POST" &&
            (path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("/Account/Register", StringComparison.OrdinalIgnoreCase)))
        {
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            });
        }
        return RateLimitPartition.GetNoLimiter("unlimited");
    });
});

var app = builder.Build();

// Run migrations + seed. On failure: log and flag the app as DB-degraded — DO NOT crash.
var startupHealth = app.Services.GetRequiredService<StartupHealth>();
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
await TryInitializeDatabaseAsync(app.Services, startupHealth, startupLogger, isStartup: true);

// First-line middleware: when DB is unreachable, redirect every request to /db-error
// (except the error page itself and static assets). This must come BEFORE auth, because
// Identity reads users from the DB and would otherwise blow up before our page is reached.
app.Use(async (ctx, next) =>
{
    if (!startupHealth.DatabaseAvailable)
    {
        var path = ctx.Request.Path.Value ?? "/";
        if (!IsAllowedWhenDbDown(path))
        {
            ctx.Response.Redirect("/db-error");
            return;
        }
    }
    await next();
});

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

// Skip HTTPS redirect inside Docker container — handle TLS at reverse proxy level
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
    app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAntiforgery();

// DB-error landing page — pure HTML, no Blazor / Identity / DB dependencies, so it
// works even when half the stack is broken.
app.MapGet("/db-error", (StartupHealth h, IHostEnvironment env) =>
    Results.Content(RenderDbErrorPage(h, env.IsDevelopment()), "text/html; charset=utf-8"));

// Manual retry — re-runs migrations + seed. On success flips the flag and redirects to /.
app.MapGet("/db-error/retry", async (StartupHealth h, IServiceProvider sp, ILogger<Program> log) =>
{
    await TryInitializeDatabaseAsync(sp, h, log, isStartup: false);
    return h.DatabaseAvailable ? Results.Redirect("/") : Results.Redirect("/db-error");
});

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

// Monthly client report (PDF) — Admin / Trainer / Subordinate
app.MapGet("/reports/client/{clientId:int}/monthly", async (
    int clientId,
    int year,
    int month,
    PTScheduler.Application.Interfaces.IClientReportService reportService,
    HttpContext ctx) =>
{
    var u = ctx.User;
    if (!(u.IsInRole(PTScheduler.Domain.Constants.Roles.Admin)
       || u.IsInRole(PTScheduler.Domain.Constants.Roles.Trainer)
       || u.IsInRole(PTScheduler.Domain.Constants.Roles.Subordinate)))
        return Results.Forbid();

    if (year < 2000 || year > 2100 || month < 1 || month > 12)
        return Results.BadRequest("Nieprawidłowy rok lub miesiąc.");

    try
    {
        var (bytes, fileName) = await reportService.GenerateMonthlyReportAsync(clientId, year, month);
        return Results.File(bytes, "application/pdf", fileName);
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(ex.Message);
    }
}).RequireAuthorization();

// Dynamic PWA manifest — reads branding from DB so name/theme follow admin settings.
// Served as application/manifest+json; browsers prefer .webmanifest over .json.
app.MapGet("/manifest.webmanifest", async (PTScheduler.Application.Interfaces.IBrandingService branding) =>
{
    AppBrandingDto? b = null;
    try { b = await branding.GetAsync(); } catch { /* DB down — use safe defaults */ }

    var name      = b?.CompanyName ?? "PTScheduler";
    var short_    = string.IsNullOrWhiteSpace(b?.PwaShortName) ? name : b.PwaShortName;
    var color     = ThemeColor(b?.ThemeName);
    var customIcon = b?.PwaIconPath;

    // Custom icon goes first (browser picks the best-fit); fall back to built-in set.
    var defaultIcons = new object[]
    {
        new { src = "/icons/icon-72.png",  sizes = "72x72",   type = "image/png" },
        new { src = "/icons/icon-96.png",  sizes = "96x96",   type = "image/png" },
        new { src = "/icons/icon-128.png", sizes = "128x128", type = "image/png" },
        new { src = "/icons/icon-144.png", sizes = "144x144", type = "image/png" },
        new { src = "/icons/icon-152.png", sizes = "152x152", type = "image/png" },
        new { src = "/icons/icon-192.png", sizes = "192x192", type = "image/png" },
        new { src = "/icons/icon-384.png", sizes = "384x384", type = "image/png" },
        new { src = "/icons/icon-512.png", sizes = "512x512", type = "image/png" },
        new { src = "/icons/icon-512-maskable.png", sizes = "512x512", type = "image/png", purpose = "maskable" },
    };
    var icons = string.IsNullOrEmpty(customIcon)
        ? defaultIcons
        : (object[])[ new { src = customIcon, sizes = "512x512", type = "image/png", purpose = "any maskable" }, ..defaultIcons ];

    var shortcutIcon = string.IsNullOrEmpty(customIcon) ? "/icons/icon-96.png" : customIcon;

    var manifest = new
    {
        id     = "/",
        name,
        short_name = short_,
        description = "System rezerwacji dla trenera personalnego",
        lang    = "pl",
        start_url = "/app",
        scope   = "/",
        display = "standalone",
        background_color = color,
        theme_color = color,
        orientation = "any",
        prefer_related_applications = false,
        icons,
        shortcuts = new object[]
        {
            new { name = "Kalendarz", short_name = "Kalendarz", url = "/calendar",
                  icons = new[]{ new { src = shortcutIcon, sizes = "96x96" } } },
            new { name = "Wizyty",    short_name = "Wizyty",    url = "/sessions",
                  icons = new[]{ new { src = shortcutIcon, sizes = "96x96" } } },
            new { name = "Klienci",   short_name = "Klienci",   url = "/clients",
                  icons = new[]{ new { src = shortcutIcon, sizes = "96x96" } } },
        }
    };
    return Results.Json(manifest, contentType: "application/manifest+json");
});

// MapStaticAssets handles build-time assets with fingerprinting/caching;
// UseStaticFiles is a fallback for runtime-uploaded files (branding logos, icons).
app.UseStaticFiles();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapGet("/health", (StartupHealth h) => Results.Json(new
{
    status = h.DatabaseAvailable ? "healthy" : "degraded",
    database = h.DatabaseAvailable,
    timestamp = DateTime.Now.ToString("o")
}));

// Internal endpoint for the portal to push a new entitlements JSON without
// restarting the container. Protected by a shared secret env var
// TENANT_INTERNAL_SECRET — the portal sends it in the X-Internal-Secret header.
app.MapPost("/internal/entitlements/reload",
    async (HttpContext ctx, PTScheduler.Web.Services.EntitlementService svc) =>
{
    var expected = Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET");
    if (string.IsNullOrEmpty(expected)) return Results.NotFound();
    if (ctx.Request.Headers["X-Internal-Secret"].ToString() != expected)
        return Results.Unauthorized();

    using var reader = new StreamReader(ctx.Request.Body);
    var json = await reader.ReadToEndAsync();
    svc.ReplaceFromJson(json);
    return Results.Ok(new { plan = svc.Current.Name });
});

// Google Meet OAuth callback — exchanges the authorization code for a refresh token.
app.MapGet("/api/google-meet/callback", async (HttpContext ctx, PTScheduler.Application.Interfaces.IGoogleMeetService meet) =>
{
    var code = ctx.Request.Query["code"].ToString();
    if (string.IsNullOrEmpty(code))
        return Results.BadRequest("Brak kodu autoryzacji.");

    var scheme = ctx.Request.Scheme;
    var host = ctx.Request.Host.ToString();
    var redirectUri = $"{scheme}://{host}/api/google-meet/callback";

    var (ok, error) = await meet.ExchangeCodeAsync(code, redirectUri);
    var html = ok
        ? "<html><body><h2>Połączono z Google Meet!</h2><p>Możesz zamknąć tę kartę i wrócić do panelu administracyjnego.</p></body></html>"
        : $"<html><body><h2>Błąd</h2><p>{error}</p></body></html>";
    return Results.Content(html, "text/html");
});

// Gateway notify (webhook): verifies the payment and fulfils the order.
// Route carries the provider key, e.g. /payments/payu/notify, /payments/p24/notify.
app.MapPost("/payments/{provider}/notify",
    async (string provider, HttpContext ctx, PTScheduler.Application.Interfaces.IPaymentService payments) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    var ok = await payments.HandleNotifyAsync(provider, body, headers);
    return ok ? Results.Ok() : Results.BadRequest();
});

app.Run();

// ---- helpers ----

static bool IsAllowedWhenDbDown(string path) =>
       path.StartsWith("/db-error", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/images", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/icons", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/branding", StringComparison.OrdinalIgnoreCase)
    || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
    || path.Equals("/manifest.webmanifest", StringComparison.OrdinalIgnoreCase)
    || path.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase)
    || path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase);

static async Task TryInitializeDatabaseAsync(IServiceProvider services, StartupHealth health, ILogger logger, bool isStartup)
{
    try
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database);

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await DbInitializer.SeedRolesAsync(roleManager);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await DbInitializer.SeedAdminAsync(userManager);

        await DbInitializer.SeedSessionTypesAsync(db);
        await DbInitializer.SeedPermissionsAsync(db);

        var wasDown = !health.DatabaseAvailable;
        health.DatabaseAvailable = true;
        health.DatabaseError = null;
        health.DatabaseErrorDetail = null;
        if (wasDown) logger.LogInformation("Database is reachable again — degraded mode lifted.");
    }
    catch (Exception ex)
    {
        health.DatabaseAvailable = false;
        health.DatabaseError = ex.GetBaseException().Message;
        health.DatabaseErrorDetail = ex.ToString();
        health.FailedAt = DateTime.Now;
        if (isStartup)
            logger.LogCritical(ex, "Database initialization failed at startup. App will start in degraded mode (only /db-error reachable).");
        else
            logger.LogError(ex, "Database retry failed.");
    }
}

static string RenderDbErrorPage(StartupHealth h, bool isDev)
{
    var msg = System.Net.WebUtility.HtmlEncode(h.DatabaseError ?? "Brak szczegółów błędu.");
    var stack = !string.IsNullOrEmpty(h.DatabaseErrorDetail)
        ? System.Net.WebUtility.HtmlEncode(h.DatabaseErrorDetail)
        : "";
    var stackBlock = isDev && !string.IsNullOrEmpty(stack)
        ? $@"<div class=""tech-row""><div class=""tech-label"">Stack trace</div><pre>{stack}</pre></div>"
        : "";
    var failedAt = h.FailedAt == default ? "—" : h.FailedAt.ToString("dd.MM.yyyy HH:mm:ss");

    return $@"<!doctype html>
<html lang=""pl"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <title>Chwilowy problem techniczny — PTScheduler</title>
    <style>
        :root {{ color-scheme: light; }}
        * {{ box-sizing: border-box; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            background: linear-gradient(180deg, #fef9f6 0%, #fef3eb 100%);
            margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
            padding: 1rem; color: #1f2937; line-height: 1.5; }}
        .card {{ background: #fff; max-width: 560px; width: 100%; border-radius: 16px;
            box-shadow: 0 10px 32px rgba(0,0,0,.06), 0 2px 6px rgba(0,0,0,.04);
            padding: 2.5rem 2rem; animation: fadeUp .35s ease-out; }}
        @keyframes fadeUp {{ from {{ opacity: 0; transform: translateY(8px); }} to {{ opacity: 1; transform: none; }} }}
        .badge {{ width: 72px; height: 72px; border-radius: 50%; margin: 0 auto 1.25rem;
            background: linear-gradient(135deg, #fde68a 0%, #fbbf24 100%);
            display: flex; align-items: center; justify-content: center;
            box-shadow: 0 4px 14px rgba(251, 191, 36, .35); font-size: 2rem; }}
        h1 {{ font-size: 1.5rem; margin: 0 0 .75rem; text-align: center; font-weight: 600; }}
        .lead {{ color: #4b5563; text-align: center; margin: 0 0 1.75rem; font-size: 1rem; }}
        .tips {{ background: #f9fafb; border-radius: 12px; padding: 1.1rem 1.25rem; margin-bottom: 1.5rem; }}
        .tips-title {{ font-weight: 600; color: #374151; font-size: .9rem; margin-bottom: .5rem; }}
        .tips ul {{ margin: 0; padding-left: 1.2rem; color: #4b5563; font-size: .9rem; }}
        .tips li {{ margin: .3rem 0; }}
        .actions {{ display: flex; gap: .6rem; justify-content: center; flex-wrap: wrap; margin-bottom: 1rem; }}
        .btn {{ background: #f59e0b; color: #fff; border: none; padding: .7rem 1.5rem; border-radius: 10px;
            font: inherit; font-weight: 500; cursor: pointer; text-decoration: none;
            transition: background-color .15s, transform .15s; }}
        .btn:hover {{ background: #d97706; }}
        .btn:active {{ transform: scale(0.97); }}
        .btn-ghost {{ background: transparent; color: #6b7280; border: 1px solid #e5e7eb; }}
        .btn-ghost:hover {{ background: #f9fafb; color: #111827; }}
        .admin {{ margin-top: 1.75rem; padding-top: 1rem; border-top: 1px solid #f3f4f6; }}
        .admin summary {{ cursor: pointer; color: #9ca3af; font-size: .8rem; user-select: none;
            list-style: none; display: inline-flex; align-items: center; gap: .35rem; transition: color .15s; }}
        .admin summary:hover {{ color: #6b7280; }}
        .admin summary::-webkit-details-marker {{ display: none; }}
        .admin summary::before {{ content: '▸'; transition: transform .15s; display: inline-block; font-size: .7rem; }}
        .admin[open] summary::before {{ transform: rotate(90deg); }}
        .admin[open] summary {{ color: #4b5563; margin-bottom: 1rem; }}
        .tech {{ background: #f9fafb; border-radius: 10px; padding: 1rem; font-size: .82rem; }}
        .tech-row {{ margin-bottom: .85rem; }}
        .tech-row:last-child {{ margin-bottom: 0; }}
        .tech-label {{ color: #6b7280; font-weight: 500; margin-bottom: .2rem; font-size: .75rem;
            text-transform: uppercase; letter-spacing: .04em; }}
        .tech-value {{ color: #111827; font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            word-break: break-word; }}
        .tech pre {{ background: #fff; border: 1px solid #e5e7eb; padding: .75rem; border-radius: 6px;
            overflow-x: auto; font-size: .72rem; max-height: 260px; margin: 0;
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; line-height: 1.45; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""badge"">☕</div>
        <h1>Chwilowy problem techniczny</h1>
        <p class=""lead"">Aplikacja jest w tej chwili niedostępna. Już to naprawiamy — spróbuj za moment.</p>

        <div class=""tips"">
            <div class=""tips-title"">Co możesz zrobić:</div>
            <ul>
                <li>Odczekaj chwilę i kliknij <strong>Spróbuj ponownie</strong>.</li>
                <li>Jeśli problem się utrzymuje — skontaktuj się ze swoim trenerem.</li>
                <li>Twoje dane są bezpieczne, nic nie zostało utracone.</li>
            </ul>
        </div>

        <div class=""actions"">
            <a href=""/db-error/retry"" class=""btn"">Spróbuj ponownie</a>
            <a href=""/"" class=""btn btn-ghost"">Strona główna</a>
        </div>

        <details class=""admin"">
            <summary>Informacje dla administratora</summary>
            <div class=""tech"">
                <div class=""tech-row"">
                    <div class=""tech-label"">Komunikat błędu</div>
                    <div class=""tech-value"">{msg}</div>
                </div>
                <div class=""tech-row"">
                    <div class=""tech-label"">Czas wystąpienia</div>
                    <div class=""tech-value"">{failedAt}</div>
                </div>
                {stackBlock}
            </div>
        </details>
    </div>
</body>
</html>";
}

static string ThemeColor(string? theme)
{
    var t = theme ?? "ocean";
    if (t.EndsWith("-dark", StringComparison.Ordinal)) t = t[..^5];
    return t switch
    {
        "forest"   => "#16A34A",
        "sunset"   => "#EA580C",
        "crimson"  => "#DC2626",
        "lavender" => "#9333EA",
        "slate"    => "#475569",
        "rose"     => "#E11D48",
        "teal"     => "#0D9488",
        "amber"    => "#D97706",
        "indigo"   => "#4F46E5",
        _          => "#0284C7",
    };
}

public sealed class StartupHealth
{
    public bool DatabaseAvailable { get; set; } = true;
    public string? DatabaseError { get; set; }
    public string? DatabaseErrorDetail { get; set; }
    public DateTime FailedAt { get; set; }
}
