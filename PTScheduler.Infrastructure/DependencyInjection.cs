using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var connStr = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            options.UseNpgsql(connStr);
        });

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
