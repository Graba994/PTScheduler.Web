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
    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();
    public DbSet<TenantServicePrice> TenantServicePrices => Set<TenantServicePrice>();
    public DbSet<ServiceOrder> ServiceOrders => Set<ServiceOrder>();

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
            e.HasIndex(p => p.ExternalPaymentId);
            e.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId);
            e.HasOne(p => p.ServiceOrder).WithMany().HasForeignKey(p => p.ServiceOrderId);
        });

        b.Entity<TenantEvent>(e =>
        {
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.EventType);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
        });

        b.Entity<ServiceItem>(e =>
        {
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.IsActive);
        });

        b.Entity<TenantServicePrice>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ServiceItemId }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId);
        });

        b.Entity<ServiceOrder>(e =>
        {
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.OrderGroupId);
            e.HasIndex(x => x.PaymentExternalId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
            e.HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId);
        });

        SeedPlans(b);
        SeedServiceItems(b);
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

    private static void SeedServiceItems(ModelBuilder b)
    {
        b.Entity<ServiceItem>().HasData(
            new ServiceItem
            {
                Id = 1,
                Name = "Zmiana logo / kolorów strony",
                Description = "Wymiana logo, dopasowanie kolorystyki i motywu strony trenera.",
                Category = "branding",
                DefaultPrice = 30,
                PriceType = "one_time",
                Icon = "bi-palette",
                SortOrder = 1
            },
            new ServiceItem
            {
                Id = 2,
                Name = "Konfiguracja grafiku zajęć",
                Description = "Ustawienie typów wizyt, godzin pracy, cyklicznych zajęć.",
                Category = "setup",
                DefaultPrice = 30,
                PriceType = "one_time",
                Icon = "bi-calendar-week",
                SortOrder = 2
            },
            new ServiceItem
            {
                Id = 3,
                Name = "Ustawienie płatności online",
                Description = "Konfiguracja PayU lub Przelewy24, testowanie procesu płatności.",
                Category = "setup",
                DefaultPrice = 50,
                PriceType = "one_time",
                Icon = "bi-credit-card",
                SortOrder = 3
            },
            new ServiceItem
            {
                Id = 4,
                Name = "Import bazy klientów",
                Description = "Import listy klientów z pliku Excel/CSV do systemu.",
                Category = "setup",
                DefaultPrice = 50,
                PriceType = "one_time",
                Icon = "bi-people",
                SortOrder = 4
            },
            new ServiceItem
            {
                Id = 5,
                Name = "Szkolenie 1:1 (30 min)",
                Description = "Indywidualne szkolenie wideo z obsługi systemu.",
                Category = "training",
                DefaultPrice = 80,
                PriceType = "one_time",
                Icon = "bi-camera-video",
                SortOrder = 5
            },
            new ServiceItem
            {
                Id = 6,
                Name = "Pełna konfiguracja strony",
                Description = "Kompleksowe ustawienie strony: branding, grafik, usługi, płatności.",
                Category = "setup",
                DefaultPrice = 150,
                PriceType = "one_time",
                Icon = "bi-wrench-adjustable",
                SortOrder = 6
            },
            new ServiceItem
            {
                Id = 7,
                Name = "Pakiet Wsparcie Podstawowy",
                Description = "2 drobne zmiany/mies., email 24h, 1 szkolenie/kwartał.",
                Category = "support",
                DefaultPrice = 29,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-headset",
                SortOrder = 10
            },
            new ServiceItem
            {
                Id = 8,
                Name = "Pakiet Wsparcie Premium",
                Description = "Bez limitu drobnych zmian, priorytet + telefon, 1 szkolenie/mies.",
                Category = "support",
                DefaultPrice = 79,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-star",
                SortOrder = 11
            }
        );
    }
}
