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
                Description = "Dla trenera, który zaczyna",
                MonthlyPrice = 0,
                MaxClients = 3,
                MaxSessionsPerMonth = 30,
                MaxStorageGB = 1,
                BodyMeasurements = true,
                EmailReminders = true,
                RecurringSessions = true,
                TwoFactorAuth = true,
                CustomLogo = true,
                CustomFavicon = true,
                BasicAnalytics = true,
                VideoProvider = "youtube",
                SortOrder = 1,
                IsFeatured = false
            },
            new Plan
            {
                Id = "pro",
                Name = "Pro",
                Description = "Pełna funkcjonalność dla aktywnych trenerów",
                MonthlyPrice = 79,
                YearlyPrice = 790,
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
                VideoProvider = "bunny",
                SortOrder = 2,
                IsFeatured = true
            },
            new Plan
            {
                Id = "studio",
                Name = "Studio",
                Description = "Bez limitów, pełna kontrola, priorytetowe wsparcie",
                MonthlyPrice = 149,
                YearlyPrice = 1490,
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
                VideoProvider = "bunny",
                SortOrder = 3,
                IsFeatured = false
            }
        );
    }
}
