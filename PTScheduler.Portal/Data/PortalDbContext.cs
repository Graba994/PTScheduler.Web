using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<BackupEntry> BackupEntries => Set<BackupEntry>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<TenantEvent> TenantEvents => Set<TenantEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.HasIndex(t => t.Domain).IsUnique();
            e.HasIndex(t => t.Port).IsUnique();
            e.HasOne(t => t.Plan).WithMany(p => p.Tenants).HasForeignKey(t => t.PlanId);
        });

        b.Entity<Plan>(e =>
        {
            e.HasKey(p => p.Id);
        });

        b.Entity<Subscription>(e =>
        {
            e.HasOne(s => s.Tenant).WithMany(t => t.Subscriptions).HasForeignKey(s => s.TenantId);
            e.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId);
        });

        b.Entity<LoginLog>(e =>
        {
            e.HasIndex(l => l.Email);
            e.HasIndex(l => l.CreatedAt);
        });

        b.Entity<SiteSetting>(e =>
        {
            e.HasKey(s => s.Key);
        });

        b.Entity<BackupEntry>(e =>
        {
            e.HasIndex(x => x.Slug);
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<PaymentRecord>(e =>
        {
            e.HasIndex(p => p.TenantId);
            e.HasIndex(p => p.CreatedAt);
            e.HasIndex(p => p.StripeInvoiceId);
            e.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId);
        });

        b.Entity<TenantEvent>(e =>
        {
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.EventType);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
        });

        SeedPlans(b);
    }

    private static void SeedPlans(ModelBuilder b)
    {
        b.Entity<Plan>().HasData(
            new Plan
            {
                Id = "start",
                Name = "Start",
                Description = "Testuj za darmo przez 7 dni",
                MonthlyPrice = 0,
                TrialDays = 7,
                MaxClients = 3,
                MaxSessionsPerMonth = 30,
                MaxStorageGB = 1,
                BodyMeasurements = true,
                EmailReminders = true,
                RecurringSessions = true,
                TwoFactorAuth = true,
                CustomLogo = false,
                CustomFavicon = false,
                BasicAnalytics = true,
                BrandingTier = "preview",
                VideoProvider = "youtube",
                SortOrder = 1,
                IsFeatured = false
            },
            new Plan
            {
                Id = "starter",
                Name = "Starter",
                Description = "Podstawowe narzędzia dla trenera",
                MonthlyPrice = 49,
                YearlyPrice = 490,
                MaxClients = 15,
                MaxTrainers = 0,
                MaxSubordinates = 0,
                MaxCourses = 3,
                MaxSessionsPerMonth = 200,
                MaxStorageGB = 5,
                MaxVideoStorageGB = 5,
                MaxSmsPerMonth = 0,
                PaymentsEnabled = true,
                BodyMeasurements = true,
                EmailReminders = true,
                RecurringSessions = true,
                TwoFactorAuth = true,
                CustomLogo = true,
                CustomFavicon = true,
                BasicAnalytics = true,
                FinancialReports = true,
                IntegrationPayU = true,
                BrandingTier = "basic",
                VideoProvider = "youtube",
                SortOrder = 2,
                IsFeatured = false
            },
            new Plan
            {
                Id = "pro",
                Name = "Pro",
                Description = "Pełna funkcjonalność dla aktywnych trenerów",
                MonthlyPrice = 99,
                YearlyPrice = 990,
                MaxClients = 50,
                MaxTrainers = 2,
                MaxSubordinates = 1,
                MaxCourses = 10,
                MaxSessionsPerMonth = 500,
                MaxStorageGB = 10,
                MaxVideoStorageGB = 20,
                MaxVideoBandwidthGBPerMonth = 100,
                MaxSmsPerMonth = 50,
                PaymentsEnabled = true,
                Coupons = true,
                CoursesEnabled = true,
                BodyMeasurements = true,
                EmailReminders = true,
                SmsReminders = true,
                PushNotifications = true,
                RecurringSessions = true,
                TwoFactorAuth = true,
                CustomLogo = true,
                CustomFavicon = true,
                CustomEmailTemplates = true,
                BasicAnalytics = true,
                AdvancedAnalytics = true,
                FinancialReports = true,
                ClientReports = true,
                DataExport = true,
                IntegrationPayU = true,
                IntegrationPrzelewy24 = true,
                IntegrationGoogleMeet = true,
                BrandingTier = "full",
                VideoProvider = "bunny",
                SortOrder = 3,
                IsFeatured = true
            },
            new Plan
            {
                Id = "studio",
                Name = "Business",
                Description = "Bez limitów, pełna kontrola, priorytetowe wsparcie",
                MonthlyPrice = 199,
                YearlyPrice = 1990,
                MaxClients = int.MaxValue,
                MaxTrainers = 10,
                MaxSubordinates = 5,
                MaxCourses = int.MaxValue,
                MaxSessionsPerMonth = int.MaxValue,
                MaxStorageGB = 100,
                MaxVideoStorageGB = 200,
                MaxVideoBandwidthGBPerMonth = 1000,
                MaxSmsPerMonth = 500,
                PaymentsEnabled = true,
                Coupons = true,
                CoursesEnabled = true,
                BodyMeasurements = true,
                EmailReminders = true,
                SmsReminders = true,
                PushNotifications = true,
                RecurringSessions = true,
                RoleBasedAccess = true,
                AuditLog = true,
                TwoFactorAuth = true,
                CustomLogo = true,
                CustomFavicon = true,
                CustomEmailTemplates = true,
                BasicAnalytics = true,
                AdvancedAnalytics = true,
                FinancialReports = true,
                ClientReports = true,
                DataExport = true,
                IntegrationPayU = true,
                IntegrationPrzelewy24 = true,
                IntegrationGoogleMeet = true,
                BrandingTier = "premium",
                VideoProvider = "bunny",
                SortOrder = 4,
                IsFeatured = false
            }
        );
    }
}
