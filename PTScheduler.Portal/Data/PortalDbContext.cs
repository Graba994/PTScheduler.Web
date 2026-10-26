using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options)
    : IdentityDbContext<IdentityUser>(options), IDataProtectionKeyContext
{
    // Klucze ASP.NET Data Protection w bazie — aktualizacja portalu (odtworzenie
    // kontenera przez Guardiana) nie wylogowuje administratorów.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

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
    public DbSet<TenantCredit> TenantCredits => Set<TenantCredit>();
    public DbSet<AppFeedback> AppFeedbacks => Set<AppFeedback>();
    public DbSet<BugReport> BugReports => Set<BugReport>();
    public DbSet<GoogleCalendarGrant> GoogleCalendarGrants => Set<GoogleCalendarGrant>();
    public DbSet<TenantMailCounter> TenantMailCounters => Set<TenantMailCounter>();
    public DbSet<TenantAddon> TenantAddons => Set<TenantAddon>();
    public DbSet<TenantOfferItem> TenantOfferItems => Set<TenantOfferItem>();
    public DbSet<TenantSmsCounter> TenantSmsCounters => Set<TenantSmsCounter>();
    public DbSet<TenantBill> TenantBills => Set<TenantBill>();
    public DbSet<TenantBillLine> TenantBillLines => Set<TenantBillLine>();
    public DbSet<ResourceSample> ResourceSamples => Set<ResourceSample>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<FunnelCounter> FunnelCounters => Set<FunnelCounter>();
    public DbSet<TrainerMeeting> TrainerMeetings => Set<TrainerMeeting>();
    public DbSet<TrainerSurveyResponse> TrainerSurveyResponses => Set<TrainerSurveyResponse>();
    public DbSet<MascotAsset> MascotAssets => Set<MascotAsset>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<TenantMetricSnapshot> TenantMetricSnapshots => Set<TenantMetricSnapshot>();
    public DbSet<LegalAcceptance> LegalAcceptances => Set<LegalAcceptance>();
    public DbSet<InboxThread> InboxThreads => Set<InboxThread>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<TenantVideo> TenantVideos => Set<TenantVideo>();
    public DbSet<TenantVideoFolder> TenantVideoFolders => Set<TenantVideoFolder>();
    public DbSet<CommunityExercise> CommunityExercises => Set<CommunityExercise>();
    public DbSet<SalesDocument> SalesDocuments => Set<SalesDocument>();
    public DbSet<SalesDocumentLine> SalesDocumentLines => Set<SalesDocumentLine>();
    public DbSet<SalesLedgerEntry> SalesLedger => Set<SalesLedgerEntry>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<FinanceEvent> FinanceEvents => Set<FinanceEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<InviteCode>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(40);
        });

        b.Entity<LegalAcceptance>(e =>
        {
            e.HasIndex(a => new { a.TenantId, a.DocKey, a.Version });
            e.Property(a => a.DocKey).HasMaxLength(40);
            e.Property(a => a.Version).HasMaxLength(20);
        });

        b.Entity<TenantMetricSnapshot>(e => e.HasIndex(m => new { m.TenantId, m.Date }).IsUnique());

        b.Entity<MascotAsset>(e =>
        {
            e.HasKey(m => m.Mood);
            e.Property(m => m.Mood).HasMaxLength(20);
            e.Property(m => m.ContentType).HasMaxLength(40);
        });

        b.Entity<AiUsageLog>(e =>
        {
            e.HasIndex(l => l.CreatedAt);
            e.HasIndex(l => new { l.TenantId, l.CreatedAt });
            e.Property(l => l.Feature).HasMaxLength(40);
            e.Property(l => l.Model).HasMaxLength(120);
            e.Property(l => l.Error).HasMaxLength(500);
            e.Property(l => l.CostUsd).HasPrecision(12, 6);
        });

        b.Entity<FunnelCounter>(e =>
        {
            e.HasIndex(c => new { c.Day, c.Kind }).IsUnique();
            e.Property(c => c.Kind).HasMaxLength(32);
        });

        b.Entity<Tenant>(e =>
        {
            e.HasIndex(t => t.Slug).IsUnique();
            e.HasIndex(t => t.ReferredByTenantId);
            e.HasIndex(t => t.RegistrationKey);
            e.HasIndex(t => t.Domain).IsUnique();
            e.HasIndex(t => t.Port).IsUnique();
            e.HasOne(t => t.Plan).WithMany(p => p.Tenants).HasForeignKey(t => t.PlanId);
            e.Property(t => t.InvoiceName).HasMaxLength(300);
            e.Property(t => t.InvoiceAddress).HasMaxLength(500);
            e.Property(t => t.InvoiceTaxId).HasMaxLength(32);
            e.Property(t => t.InvoiceEmail).HasMaxLength(256);
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

        b.Entity<ResourceSample>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.At });
            e.HasIndex(x => x.At);
        });

        b.Entity<BackupEntry>(e =>
        {
            e.HasIndex(x => x.Slug);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.VerifyInfo).HasMaxLength(1000);
            e.Property(x => x.OffsiteInfo).HasMaxLength(1000);
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

        b.Entity<TenantVideo>(e =>
        {
            e.HasIndex(x => x.VideoGuid).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Kind });
            e.Property(x => x.VideoGuid).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TenantVideoFolder>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Kind }).IsUnique();
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.CollectionId).HasMaxLength(64);
        });

        b.Entity<CommunityExercise>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.SourceExerciseId }).IsUnique();
            e.HasIndex(x => new { x.Hidden, x.NamePl });
            e.Property(x => x.NamePl).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.PrimaryMuscles).HasMaxLength(300);
            e.Property(x => x.SecondaryMuscles).HasMaxLength(300);
            e.Property(x => x.Category).HasMaxLength(40);
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.Equipment).HasMaxLength(60);
            e.Property(x => x.Force).HasMaxLength(20);
            e.Property(x => x.Mechanic).HasMaxLength(20);
            e.Property(x => x.Tracking).HasMaxLength(30);
            e.Property(x => x.ImageUrls).HasMaxLength(2000);
            e.Property(x => x.VideoType).HasMaxLength(20);
            e.Property(x => x.VideoRef).HasMaxLength(500);
            e.Property(x => x.AuthorName).HasMaxLength(200);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InboxThread>(e =>
        {
            e.HasIndex(x => new { x.Folder, x.LastAt });
            e.HasIndex(x => new { x.Counterpart, x.SubjectKey });
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.SubjectKey).HasMaxLength(300);
            e.Property(x => x.Counterpart).HasMaxLength(320);
            e.Property(x => x.CounterpartName).HasMaxLength(200);
            e.Property(x => x.Snippet).HasMaxLength(200);
            e.Property(x => x.Folder).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<InboxMessage>(e =>
        {
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => x.MessageId);
            e.Property(x => x.MessageId).HasMaxLength(500);
            e.Property(x => x.InReplyTo).HasMaxLength(500);
            e.Property(x => x.FromAddress).HasMaxLength(320);
            e.Property(x => x.FromName).HasMaxLength(200);
            e.Property(x => x.ToAddress).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.SentBy).HasMaxLength(320);
            e.HasOne(x => x.Thread).WithMany(t => t.Messages).HasForeignKey(x => x.ThreadId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppFeedback>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(2000);
            e.Property(x => x.AuthorEmail).HasMaxLength(256);
            e.Property(x => x.ContactEmail).HasMaxLength(256);
            e.Property(x => x.PublicAuthor).HasMaxLength(120);
            e.HasIndex(x => x.CreatedAt);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BugReport>(e =>
        {
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.PageUrl).HasMaxLength(500);
            e.Property(x => x.PageTitle).HasMaxLength(200);
            e.Property(x => x.AuthorEmail).HasMaxLength(256);
            e.Property(x => x.AuthorName).HasMaxLength(120);
            e.Property(x => x.AuthorRole).HasMaxLength(40);
            e.Property(x => x.UserAgent).HasMaxLength(400);
            e.Property(x => x.Viewport).HasMaxLength(40);
            e.Property(x => x.AppVersion).HasMaxLength(80);
            e.Property(x => x.ClientErrors).HasMaxLength(8000);
            e.Property(x => x.ScreenshotType).HasMaxLength(20);
            e.Property(x => x.AdminNote).HasMaxLength(2000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TenantMailCounter>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Day }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GoogleCalendarGrant>(e =>
        {
            e.Property(x => x.UserKey).HasMaxLength(64);
            e.Property(x => x.GoogleEmail).HasMaxLength(256);
            e.Property(x => x.RefreshToken).HasMaxLength(1024);
            e.HasIndex(x => new { x.TenantId, x.UserKey }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
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
            e.HasIndex(x => x.FulfillmentType);
            e.Property(x => x.FulfillmentType).HasDefaultValue("manual");
            e.Property(x => x.StripePriceId).HasMaxLength(100);
        });

        b.Entity<TenantAddon>(e =>
        {
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.StripeSubscriptionItemId).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<TenantOfferItem>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.InternalNote).HasMaxLength(1000);
            e.Property(x => x.Billing).HasMaxLength(16);
            e.Property(x => x.Effect).HasMaxLength(32);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Ignore(x => x.Total);
            e.Ignore(x => x.TotalEffect);
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ServiceItem).WithMany().HasForeignKey(x => x.ServiceItemId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TenantBill>(e =>
        {
            e.Property(x => x.Number).HasMaxLength(32);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.PaidVia).HasMaxLength(32);
            e.Property(x => x.PayToken).HasMaxLength(64);
            e.Property(x => x.PaymentGateway).HasMaxLength(32);
            e.Property(x => x.PaymentExternalId).HasMaxLength(200);
            e.Property(x => x.AdminNote).HasMaxLength(1000);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => x.PayToken).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.PeriodStart });
            e.Property(x => x.PaymentSessionId).HasMaxLength(64);
            e.HasIndex(x => x.PaymentExternalId);
            e.HasIndex(x => x.PaymentSessionId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Lines).WithOne(l => l.Bill).HasForeignKey(l => l.BillId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SalesDocument>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Number).HasMaxLength(64);
            e.Property(x => x.PaymentMethod).HasMaxLength(40);
            e.Property(x => x.Mode).HasMaxLength(16);
            e.Property(x => x.CorrectionReason).HasMaxLength(500);
            e.Property(x => x.Seller).HasMaxLength(1000);
            e.Property(x => x.BuyerName).HasMaxLength(300);
            e.Property(x => x.BuyerAddress).HasMaxLength(500);
            e.Property(x => x.BuyerTaxId).HasMaxLength(32);
            e.Property(x => x.BuyerEmail).HasMaxLength(256);
            e.Property(x => x.Net).HasPrecision(12, 2);
            e.Property(x => x.Vat).HasPrecision(12, 2);
            e.Property(x => x.Gross).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.VatNote).HasMaxLength(300);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.Provider).HasMaxLength(16);
            e.Property(x => x.ExternalId).HasMaxLength(64);
            e.Property(x => x.ExternalUrl).HasMaxLength(500);
            e.Property(x => x.ExternalError).HasMaxLength(1000);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            // Jeden numer w serii raz na rok — chroni przed dziurą i duplikatem przy równoczesnym wystawianiu.
            e.Property(x => x.Series).HasMaxLength(16);
            e.HasIndex(x => new { x.Series, x.Year, x.Seq }).IsUnique().HasFilter("\"Seq\" > 0");
            // Jedna wpłata = najwyżej jeden dokument sprzedaży (korekty mają własne wpisy).
            e.HasIndex(x => x.PaymentRecordId).IsUnique().HasFilter("\"PaymentRecordId\" IS NOT NULL AND \"Kind\" <> 'korekta' AND \"Status\" <> 2");
            e.HasIndex(x => x.IssueDate);
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Corrects).WithMany().HasForeignKey(x => x.CorrectsId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.Document).HasForeignKey(l => l.DocumentId);
        });

        b.Entity<SalesDocumentLine>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.VatRate).HasMaxLength(8);
            e.Property(x => x.Quantity).HasPrecision(12, 3);
            e.Property(x => x.UnitGross).HasPrecision(12, 2);
            e.Property(x => x.Net).HasPrecision(12, 2);
            e.Property(x => x.Vat).HasPrecision(12, 2);
            e.Property(x => x.Gross).HasPrecision(12, 2);
        });

        b.Entity<SalesLedgerEntry>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            // Wpłata trafia do ewidencji dokładnie raz, nawet gdy synchronizacja ruszy dwa razy naraz.
            e.HasIndex(x => x.PaymentRecordId).IsUnique().HasFilter("\"PaymentRecordId\" IS NOT NULL AND \"Source\" = 'payment'");
            e.HasIndex(x => x.Date);
        });

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Vendor).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Category).HasMaxLength(40);
            e.Property(x => x.DocumentNumber).HasMaxLength(100);
            e.Property(x => x.AttachmentName).HasMaxLength(200);
            e.Property(x => x.AttachmentType).HasMaxLength(100);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.DeletedReason).HasMaxLength(500);
            e.HasIndex(x => x.Date);
        });

        b.Entity<FinanceEvent>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.Entity).HasMaxLength(40);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => x.At);
        });

        b.Entity<TenantBillLine>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(12, 2);
        });

        b.Entity<TenantSmsCounter>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.Month }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TenantCredit>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.CreditType }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId);
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
                TrainingPlansEnabled = true,
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
                ReferralProgram = true,
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
                TrainingPlansEnabled = true,
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
                ReferralProgram = true,
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
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
                CreatedAt = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc),
                Name = "Pakiet Wsparcie Premium",
                Description = "Bez limitu drobnych zmian, priorytet + telefon, 1 szkolenie/mies.",
                Category = "support",
                DefaultPrice = 79,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-star",
                SortOrder = 11
            },
            // SMS credit packs
            new ServiceItem
            {
                Id = 100,
                CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "SMS +100 miesięcznie",
                Description = "100 SMS-ów więcej co miesiąc na przypomnienia o treningach. Doliczane do abonamentu — rezygnujesz, kiedy chcesz.",
                Category = "addon",
                DefaultPrice = 19,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-chat-dots",
                FulfillmentType = "credit_sms",
                CreditAmount = 100,
                SortOrder = 20
            },
            new ServiceItem
            {
                Id = 101,
                CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "SMS +300 miesięcznie",
                Description = "300 SMS-ów więcej co miesiąc — dla studiów z dużą liczbą klientów. Doliczane do abonamentu.",
                Category = "addon",
                DefaultPrice = 49,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-chat-dots",
                FulfillmentType = "credit_sms",
                CreditAmount = 300,
                SortOrder = 21
            },
            new ServiceItem
            {
                Id = 102,
                CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "Jednorazowe doładowanie 100 SMS",
                Description = "Na wyjątkowo pracowity miesiąc: SMS-y nie wygasają i schodzą dopiero po wyczerpaniu miesięcznego limitu.",
                Category = "addon",
                DefaultPrice = 29,
                PriceType = "one_time",
                Unit = "100 szt.",
                Icon = "bi-chat-dots",
                FulfillmentType = "credit_sms",
                CreditAmount = 100,
                SortOrder = 22
            },
            // CDN storage packs
            new ServiceItem
            {
                Id = 110,
                CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "Wideo +10 GB miejsca",
                Description = "10 GB więcej na lekcje i instruktaże wideo, dopóki dodatek jest aktywny. Doliczane do abonamentu.",
                Category = "addon",
                DefaultPrice = 15,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-cloud-upload",
                FulfillmentType = "credit_cdn_storage",
                CreditAmount = 10,
                SortOrder = 30
            },
            new ServiceItem
            {
                Id = 111,
                CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "Wideo +50 GB miejsca",
                Description = "50 GB więcej na kursy wideo — dla rozbudowanej biblioteki ćwiczeń i programów. Doliczane do abonamentu.",
                Category = "addon",
                DefaultPrice = 49,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-cloud-upload",
                FulfillmentType = "credit_cdn_storage",
                CreditAmount = 50,
                SortOrder = 31
            },
            // CDN bandwidth packs
            new ServiceItem
            {
                Id = 120,
                CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Name = "Transfer wideo +100 GB miesięcznie",
                Description = "Podnosi miesięczny limit odtwarzania kursów wideo o 100 GB. Doliczane do abonamentu — rezygnujesz, kiedy chcesz.",
                Category = "addon",
                DefaultPrice = 25,
                PriceType = "monthly",
                Unit = "miesiąc",
                Icon = "bi-speedometer2",
                FulfillmentType = "credit_cdn_bandwidth",
                CreditAmount = 100,
                SortOrder = 32
            }
        );
    }
}
