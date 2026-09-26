using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using PTScheduler.Guardian;
using PTScheduler.Guardian.Services;

var builder = WebApplication.CreateSlimBuilder(args);

var guardianSecret = Environment.GetEnvironmentVariable("GUARDIAN_SECRET")
    ?? builder.Configuration["Guardian:Secret"]
    ?? "";

builder.Services.AddSingleton<DockerClient>(_ =>
    new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient());
builder.Services.AddSingleton<LogStore>();
builder.Services.AddSingleton<HealthWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HealthWatcher>());
builder.Services.AddSingleton<UpgradeOrchestrator>();

var app = builder.Build();

if (guardianSecret.Length is > 0 and < 24)
    app.Logger.LogWarning("GUARDIAN_SECRET ma tylko {Length} znaków — Guardian steruje Dockerem, użyj co najmniej 32 losowych znaków.", guardianSecret.Length);

app.Services.GetRequiredService<LogStore>().RecoverInterrupted();
var orchestrator = app.Services.GetRequiredService<UpgradeOrchestrator>();
await orchestrator.CleanupOrphanedContainersAsync();

var secretBytes = Encoding.UTF8.GetBytes(guardianSecret);
var buildCommit = Environment.GetEnvironmentVariable("GUARDIAN_BUILD_COMMIT") ?? "unknown";
var buildTime = Environment.GetEnvironmentVariable("GUARDIAN_BUILD_TIME") ?? "unknown";
var version = buildCommit.Length > 7 ? buildCommit[..7] : buildCommit;
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
static string Caller(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
static IResult StartResult(bool started, string jobId, string? error) =>
    started ? Results.Ok(new { started, jobId })
    : UpgradeOrchestrator.IsBusy(error) ? Results.Conflict(new { started, error })
    : Results.BadRequest(new { started, error });

// Nagłówki bezpieczeństwa dla panelu (także /index.html serwowanego jako plik statyczny).
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        if (!h.ContainsKey("Content-Security-Policy"))
            h["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        return Task.CompletedTask;
    });
    await next();
});

app.UseStaticFiles();

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        if (string.IsNullOrEmpty(guardianSecret))
        {
            ctx.Response.StatusCode = 503;
            await ctx.Response.WriteAsync("GUARDIAN_SECRET nie ustawiony.");
            return;
        }
        // Porównanie w stałym czasie — długość odpowiedzi nie zdradza, ile znaków sekretu się zgadza.
        var provided = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Guardian-Secret"].FirstOrDefault() ?? "");
        if (!CryptographicOperations.FixedTimeEquals(provided, secretBytes))
        {
            app.Logger.LogWarning("Odrzucono wywołanie {Method} {Path} z {Ip} — zły sekret.", ctx.Request.Method, ctx.Request.Path, Caller(ctx));
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }
        if (!HttpMethods.IsGet(ctx.Request.Method))
            app.Logger.LogInformation("Polecenie {Method} {Path} z {Ip}.", ctx.Request.Method, ctx.Request.Path, Caller(ctx));
    }
    await next();
});

// ── Health (no auth) ────────────────────────────────────────────────

app.MapGet("/health", (HealthWatcher hw) => Results.Ok(new
{
    status = "healthy",
    version,
    portalHealthy = hw.PortalHealthy,
    uptime = orchestrator.Uptime.ToString(@"d\.hh\:mm\:ss")
}));

app.MapGet("/", (HttpContext ctx) =>
{
    // Panel nie ładuje nic z zewnątrz — ścisła polityka CSP utrudnia wstrzyknięcie skryptu.
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    return Results.File(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), "text/html; charset=utf-8");
});

// ── API (auth required) ────────────────────────────────────────────

app.MapGet("/api/status", (HealthWatcher hw) =>
{
    var history = orchestrator.GetHistory(5);
    var active = orchestrator.ActiveJobId is { } id ? orchestrator.GetJob(id) : null;
    return Results.Ok(new GuardianStatus
    {
        Healthy = true,
        Version = version,
        BuildTime = buildTime,
        TenantImage = orchestrator.TenantImage,
        Uptime = orchestrator.Uptime.ToString(@"d\.hh\:mm\:ss"),
        PortalHealthy = hw.PortalHealthy,
        PortalLastChecked = hw.LastCheckedAt,
        ActiveJob = active,
        TotalJobs = history.Count
    });
});

app.MapPost("/api/upgrade/portal", async (HttpContext ctx) =>
{
    var (started, jobId, error) = await orchestrator.StartPortalUpgradeAsync(Caller(ctx));
    return StartResult(started, jobId, error);
});

// Obraz aplikacji trenerów; z listą instancji w treści — od razu wdrożenie w tym samym zadaniu.
app.MapPost("/api/upgrade/tenant", async (HttpContext ctx) =>
{
    var rebuild = ctx.Request.Query["rebuild"].FirstOrDefault() != "false";
    TenantRollingRequest? rollout = null;
    if (ctx.Request.ContentLength is > 0 || ctx.Request.Headers.TransferEncoding.Count > 0)
    {
        try { rollout = await ctx.Request.ReadFromJsonAsync<TenantRollingRequest>(jsonOptions); }
        catch { return Results.BadRequest(new { started = false, error = "Nieprawidłowy JSON." }); }
    }
    var (started, jobId, error) = await orchestrator.StartTenantUpgradeAsync(rebuild, rollout, Caller(ctx));
    return StartResult(started, jobId, error);
});

app.MapPost("/api/upgrade/tenants/rolling", async (HttpContext ctx) =>
{
    TenantRollingRequest? request;
    try { request = await ctx.Request.ReadFromJsonAsync<TenantRollingRequest>(jsonOptions); }
    catch { return Results.BadRequest(new { started = false, error = "Nieprawidłowy JSON." }); }

    if (request is null || request.Tenants.Count == 0)
        return Results.BadRequest(new { started = false, error = "Brak tenantów." });

    var (started, jobId, error) = await orchestrator.StartTenantRollingUpdateAsync(request, Caller(ctx));
    return StartResult(started, jobId, error);
});

app.MapGet("/api/upgrade/jobs/{id}", (string id) =>
{
    if (!LogStore.IsSafeId(id)) return Results.BadRequest();
    var job = orchestrator.GetJob(id);
    return job is not null ? Results.Ok(job) : Results.NotFound();
});

app.MapGet("/api/upgrade/active", () =>
{
    var id = orchestrator.ActiveJobId;
    if (id is null) return Results.Ok(new { active = false });
    var job = orchestrator.GetJob(id);
    return Results.Ok(new { active = true, job });
});

app.MapGet("/api/upgrade/history", (HttpContext ctx) =>
{
    var limit = int.TryParse(ctx.Request.Query["limit"], out var l) ? l : 20;
    return Results.Ok(orchestrator.GetHistory(Math.Clamp(limit, 1, 50)));
});

app.MapPost("/api/rollback/portal", async (HttpContext ctx) =>
{
    var (started, jobId, error) = await orchestrator.RollbackPortalAsync(Caller(ctx));
    return StartResult(started, jobId, error);
});

app.Run();
