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
        services.AddDbContextFactory<ApplicationDbContext>(options =>
        {
            var connStr = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            options.UseNpgsql(connStr)
                   .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        });

        // Scoped ApplicationDbContext for Identity (derived from the Singleton factory)
        services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

        var settingsFilePath = Path.Combine(contentRootPath, "connections.json");
        services.AddScoped<IDatabaseSettingsService>(sp =>
            new DatabaseSettingsService(sp.GetRequiredService<IConfiguration>(), settingsFilePath));

        services.AddScoped<IBrandingService, BrandingService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<ISessionPackageService, SessionPackageService>();
        services.AddScoped<ISessionTypeService, SessionTypeService>();
        services.AddScoped<IIntroSessionService, IntroSessionService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ITrainerAvailabilityService, TrainerAvailabilityService>();
        services.AddScoped<ISessionSeriesService, SessionSeriesService>();
        services.AddScoped<IClientContactService, ClientContactService>();
        services.AddScoped<IDemoDataService, DemoDataService>();
        services.AddScoped<ISessionInvitationService, SessionInvitationService>();
        services.AddScoped<IEmailSettingsService, EmailSettingsService>();
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.AddScoped<IBodyMeasurementService, BodyMeasurementService>();
        services.AddScoped<ITrainerConfigService, TrainerConfigService>();
        services.AddScoped<INotificationPreferencesService, NotificationPreferencesService>();
        services.AddScoped<IPublicBookingService, PublicBookingService>();
        services.AddScoped<IClientReportService, ClientReportService>();
        services.AddScoped<IPermissionService, PermissionService>();

        // QuestPDF community license — free for orgs <$1M annual revenue.
        // Set globally; safe to call multiple times in tests.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        return services;
    }
}
