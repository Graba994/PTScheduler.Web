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

var orchestrator = app.Services.GetRequiredService<UpgradeOrchestrator>();
await orchestrator.CleanupOrphanedContainersAsync();

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
        var provided = ctx.Request.Headers["X-Guardian-Secret"].FirstOrDefault();
        if (provided != guardianSecret)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }
    }
    await next();
});

// ── Health (no auth) ────────────────────────────────────────────────

app.MapGet("/health", (HealthWatcher hw) => Results.Ok(new
{
    status = "healthy",
    portalHealthy = hw.PortalHealthy,
    uptime = orchestrator.Uptime.ToString(@"d\.hh\:mm\:ss")
}));

app.MapGet("/", () => Results.File(
    Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"),
    "text/html"));

// ── API (auth required) ────────────────────────────────────────────

app.MapGet("/api/status", (HealthWatcher hw) =>
{
    var history = orchestrator.GetHistory(5);
    var active = orchestrator.ActiveJobId is { } id ? orchestrator.GetJob(id) : null;
    return Results.Ok(new GuardianStatus
    {
        Healthy = true,
        Uptime = orchestrator.Uptime.ToString(@"d\.hh\:mm\:ss"),
        PortalHealthy = hw.PortalHealthy,
        PortalLastChecked = hw.LastCheckedAt,
        ActiveJob = active,
        TotalJobs = history.Count
    });
});

app.MapPost("/api/upgrade/portal", async () =>
{
    var (started, jobId, error) = await orchestrator.StartPortalUpgradeAsync();
    return started
        ? Results.Ok(new { started, jobId })
        : Results.Conflict(new { started, error });
});

app.MapPost("/api/upgrade/tenant", async (HttpContext ctx) =>
{
    var rebuild = ctx.Request.Query["rebuild"].FirstOrDefault() != "false";
    var (started, jobId, error) = await orchestrator.StartTenantUpgradeAsync(rebuild);
    return started
        ? Results.Ok(new { started, jobId })
        : Results.Conflict(new { started, error });
});

app.MapGet("/api/upgrade/jobs/{id}", (string id) =>
{
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

app.MapPost("/api/rollback/portal", async () =>
{
    var (started, jobId, error) = await orchestrator.RollbackPortalAsync();
    return started
        ? Results.Ok(new { started, jobId })
        : Results.Conflict(new { started, error });
});

app.Run();
