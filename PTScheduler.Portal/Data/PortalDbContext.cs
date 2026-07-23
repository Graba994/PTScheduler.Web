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
                MaxEmailsPerMonth = 100,
                MaxPushNotificationsPerDay = 10,
                BackupRetentionDays = 7,
                CustomLogo = true,
                EmailReminders = true,
                BasicAnalytics = true,
                EmailSupport = true,
                ClientDocuments = true,
                BodyMeasurements = true,
                RecurringSessions = true,
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
                MaxEmailsPerMonth = 2000,
                MaxSmsPerMonth = 50,
                BackupRetentionDays = 30,
                PaymentsEnabled = true,
                Invoicing = true,
                Coupons = true,
                Subscriptions = true,
                CoursesEnabled = true,
                WorkoutPlansEnabled = true,
                MealPlansEnabled = true,
                ClientDocuments = true,
                ProgressPhotos = true,
                BodyMeasurements = true,
                NutritionTracking = true,
                ClientChat = true,
                VideoCalls = true,
                EmailReminders = true,
                SmsReminders = true,
                PushNotifications = true,
                AutomatedFollowups = true,
                GroupSessions = true,
                RecurringSessions = true,
                Waitlist = true,
                CalendarSync = true,
                CustomLogo = true,
                CustomColors = true,
                CustomFavicon = true,
                CustomEmailTemplates = true,
                LandingPageBuilder = true,
                BasicAnalytics = true,
                AdvancedAnalytics = true,
                FinancialReports = true,
                ClientReports = true,
                DataExport = true,
                EmailSupport = true,
                IntegrationPayU = true,
                IntegrationPrzelewy24 = true,
                IntegrationStripe = true,
                IntegrationGoogleCalendar = true,
                IntegrationZoom = true,
                IntegrationGoogleMeet = true,
                VideoProvider = "bunny",
                SortOrder = 2,
                IsFeatured = true
            },
            new Plan
            {
                Id = "studio",
                Name = "Studio",
                Description = "Bez limitów, własna domena, priorytetowe wsparcie, AI",
                MonthlyPrice = 149,
                YearlyPrice = 1490,
                MaxClients = int.MaxValue,
                MaxTrainers = 10,
                MaxSubordinates = 5,
                MaxLocations = 5,
                MaxCourses = int.MaxValue,
                MaxSessionsPerMonth = int.MaxValue,
                MaxStorageGB = 100,
                MaxVideoStorageGB = 200,
                MaxVideoBandwidthGBPerMonth = 1000,
                MaxEmailsPerMonth = int.MaxValue,
                MaxSmsPerMonth = 500,
                BackupRetentionDays = 90,
                PaymentsEnabled = true,
                MultipleGateways = true,
                Invoicing = true,
                TaxReports = true,
                Coupons = true,
                Subscriptions = true,
                CoursesEnabled = true,
                WorkoutPlansEnabled = true,
                MealPlansEnabled = true,
                ClientDocuments = true,
                ProgressPhotos = true,
                BodyMeasurements = true,
                NutritionTracking = true,
                ClientChat = true,
                VideoCalls = true,
                EmailReminders = true,
                SmsReminders = true,
                PushNotifications = true,
                AutomatedFollowups = true,
                GroupMessaging = true,
                GroupSessions = true,
                RecurringSessions = true,
                Waitlist = true,
                MultipleCalendars = true,
                CalendarSync = true,
                RoleBasedAccess = true,
                AuditLog = true,
                TwoFactorAuth = true,
                CustomLogo = true,
                CustomColors = true,
                CustomFavicon = true,
                CustomDomain = true,
                RemoveWatermark = true,
                CustomEmailTemplates = true,
                MultiLanguage = true,
                LandingPageBuilder = true,
                BlogModule = true,
                BasicAnalytics = true,
                AdvancedAnalytics = true,
                FinancialReports = true,
                ClientReports = true,
                DataExport = true,
                AiWorkoutGenerator = true,
                AiMealPlanner = true,
                AiChatbot = true,
                EmailSupport = true,
                PrioritySupport = true,
                PhoneSupport = true,
                OnboardingCall = true,
                SlaGuarantee = true,
                IntegrationPayU = true,
                IntegrationPrzelewy24 = true,
                IntegrationStripe = true,
                IntegrationAutoPay = true,
                IntegrationKlarna = true,
                IntegrationBlik = true,
                VideoProvider = "bunny",
                IntegrationGoogleCalendar = true,
                IntegrationOutlookCalendar = true,
                IntegrationAppleCalendar = true,
                IntegrationZoom = true,
                IntegrationGoogleMeet = true,
                IntegrationTeams = true,
                IntegrationMailerLite = true,
                IntegrationMailchimp = true,
                IntegrationZapier = true,
                IntegrationMake = true,
                IntegrationCustomWebhooks = true,
                IntegrationSlack = true,
                IntegrationTelegram = true,
                IntegrationWhatsApp = true,
                IntegrationDiscord = true,
                IntegrationGoogleAnalytics = true,
                IntegrationMetaPixel = true,
                SortOrder = 3,
                IsFeatured = false
            }
        );
    }
}
