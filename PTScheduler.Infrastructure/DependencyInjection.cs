using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;

namespace PTScheduler.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath)
    {
        // Blazor Server keeps the DI scope alive for the whole circuit, so a single
        // scoped DbContext is shared across every component on the page. When the
        // layout (NavMenu), the page and middleware query it while rendering, EF
        // throws "A second operation was started on this context instance". The fix
        // is the DbContext-factory pattern: a factory produces short-lived contexts
        // per operation. We still expose a scoped ApplicationDbContext (resolved from
        // the factory) so Identity and the existing scoped services keep working
        // exactly as before; the read-heavy, layout-level services take the factory.
        static void Configure(IServiceProvider sp, Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options)
        {
            var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            options.UseNpgsql(connStr);
            // Migrations + model snapshot are hand-authored here (no dotnet ef in the
            // dev container), so the snapshot is never a byte-perfect match for the
            // runtime model. EF Core 10 validates this on MigrateAsync() and would
            // otherwise throw PendingModelChangesWarning at startup. The migrations
            // themselves are correct, so we suppress only that consistency check.
            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        services.AddDbContextFactory<ApplicationDbContext>(Configure);
        services.AddScoped<ApplicationDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

        var settingsFilePath = Path.Combine(contentRootPath, "connections.json");
        services.AddScoped<IDatabaseSettingsService>(sp =>
            new DatabaseSettingsService(sp.GetRequiredService<IConfiguration>(), settingsFilePath));

        services.AddMemoryCache();

        services.AddScoped<IBrandingService, BrandingService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<ISessionPackageService, SessionPackageService>();
        services.AddScoped<IIntroSessionService, IntroSessionService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IAcademyCatalogService, AcademyCatalogService>();
        services.AddScoped<IAcademyStudentService, AcademyStudentService>();
        services.AddScoped<ISiteSettingsService, SiteSettingsService>();
        services.AddScoped<IShopService, ShopService>();
        services.AddScoped<IPaymentGateway, PayUGateway>();
        services.AddHttpClient("PayU");

        return services;
    }
}
