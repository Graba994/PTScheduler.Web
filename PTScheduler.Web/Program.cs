using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using PTScheduler.Application;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Web;
using PTScheduler.Web.Components;
using PTScheduler.Web.Components.Account;
using PTScheduler.Web.Services;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("connections.json", optional: true, reloadOnChange: true);

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
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddErrorDescriber<PolishIdentityErrorDescriber>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IEmailSender<ApplicationUser>, PTScheduler.Web.Components.Account.IdentityEmailSender>();
builder.Services.AddSingleton<IWebRootPathProvider, WebRootPathProvider>();
builder.Services.AddScoped<PTScheduler.Web.Services.HintStateService>();
builder.Services.AddHostedService<SessionReminderService>();

var app = builder.Build();

// Seed roles on startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await PTScheduler.Infrastructure.Data.DbInitializer.SeedRolesAsync(roleManager);
    var db = scope.ServiceProvider.GetRequiredService<PTScheduler.Infrastructure.Data.ApplicationDbContext>();
    await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(db.Database);
    await PTScheduler.Infrastructure.Data.DbInitializer.SeedSessionTypesAsync(db);
}

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

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.Run();
