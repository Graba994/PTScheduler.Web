using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Entities;

namespace PTScheduler.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    // Klucze ASP.NET Data Protection (szyfrują ciasteczka logowania i tokeny
    // antiforgery). Trzymane w bazie tenanta, a nie w systemie plików kontenera,
    // żeby odtworzenie kontenera przy aktualizacji nie wylogowywało użytkowników.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionType> SessionTypes => Set<SessionType>();
    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<AutomationLog> AutomationLogs => Set<AutomationLog>();
    public DbSet<ClientDocument> ClientDocuments => Set<ClientDocument>();
    public DbSet<DocumentAcceptance> DocumentAcceptances => Set<DocumentAcceptance>();
    public DbSet<TrainerNote> TrainerNotes => Set<TrainerNote>();
    public DbSet<SessionPackage> SessionPackages => Set<SessionPackage>();
    public DbSet<IntroSessionConfig> IntroSessionConfigs => Set<IntroSessionConfig>();
    public DbSet<AppBranding> AppBrandings => Set<AppBranding>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TrainerAvailability> TrainerAvailabilities => Set<TrainerAvailability>();
    public DbSet<TrainerConfig> TrainerConfigs => Set<TrainerConfig>();
    public DbSet<SessionSeries> SessionSeries => Set<SessionSeries>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<SessionInvitation> SessionInvitations => Set<SessionInvitation>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<SmsSettings> SmsSettings => Set<SmsSettings>();
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();
    public DbSet<TrainerStyle> TrainerStyles => Set<TrainerStyle>();
    public DbSet<ClientMedal> ClientMedals => Set<ClientMedal>();
    public DbSet<TrainerStyleExample> TrainerStyleExamples => Set<TrainerStyleExample>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<WebPushSettings> WebPushSettings => Set<WebPushSettings>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<FinancePin> FinancePins => Set<FinancePin>();
    public DbSet<FinanceTaxConfig> FinanceTaxConfigs => Set<FinanceTaxConfig>();
    public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseEnrollment> CourseEnrollments => Set<CourseEnrollment>();
    public DbSet<CourseModule> CourseModules => Set<CourseModule>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizOption> QuizOptions => Set<QuizOption>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<PaymentSettings> PaymentSettings => Set<PaymentSettings>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<PackageOffer> PackageOffers => Set<PackageOffer>();
    public DbSet<MembershipPlan> MembershipPlans => Set<MembershipPlan>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipPeriod> MembershipPeriods => Set<MembershipPeriod>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ProgressPhoto> ProgressPhotos => Set<ProgressPhoto>();
    public DbSet<MarketingSettings> MarketingSettings => Set<MarketingSettings>();
    public DbSet<Referral> Referrals => Set<Referral>();
    public DbSet<ClientReview> ClientReviews => Set<ClientReview>();
    public DbSet<GiftVoucher> GiftVouchers => Set<GiftVoucher>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();

    // Moduł treningowy (kreator planów)
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<TrainerExercisePref> TrainerExercisePrefs => Set<TrainerExercisePref>();
    public DbSet<TrainingPlan> TrainingPlans => Set<TrainingPlan>();
    public DbSet<PlanDay> PlanDays => Set<PlanDay>();
    public DbSet<PlanExercise> PlanExercises => Set<PlanExercise>();
    public DbSet<WorkoutLog> WorkoutLogs => Set<WorkoutLog>();
    public DbSet<WorkoutSetLog> WorkoutSetLogs => Set<WorkoutSetLog>();
    public DbSet<WorkoutSession> WorkoutSessions => Set<WorkoutSession>();
    public DbSet<WorkoutComment> WorkoutComments => Set<WorkoutComment>();
    public DbSet<SurveyTemplate> SurveyTemplates => Set<SurveyTemplate>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<CalendarConnection> CalendarConnections => Set<CalendarConnection>();
    public DbSet<CalendarBusyBlock> CalendarBusyBlocks => Set<CalendarBusyBlock>();
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();
    public DbSet<SlotInvitation> SlotInvitations => Set<SlotInvitation>();
    public DbSet<SlotWatch> SlotWatches => Set<SlotWatch>();
    public DbSet<WearableLink> WearableLinks => Set<WearableLink>();
    public DbSet<WearableDay> WearableDays => Set<WearableDay>();
    public DbSet<WearableActivity> WearableActivities => Set<WearableActivity>();
    public DbSet<WearableWorkout> WearableWorkouts => Set<WearableWorkout>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmailTemplate>()
            .HasIndex(e => e.Key)
            .IsUnique();

        builder.Entity<Coupon>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.DiscountValue).HasPrecision(10, 2);
        });

        builder.Entity<CouponRedemption>(e =>
        {
            e.HasOne(r => r.Coupon)
             .WithMany(c => c.Redemptions)
             .HasForeignKey(r => r.CouponId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(r => r.OriginalAmount).HasPrecision(10, 2);
            e.Property(r => r.DiscountAmount).HasPrecision(10, 2);
            e.Property(r => r.FinalAmount).HasPrecision(10, 2);
            e.HasIndex(r => r.RedeemedAt);
        });

        builder.Entity<ApplicationUser>().Property(u => u.CalendarFeedToken).HasMaxLength(64);
        builder.Entity<ApplicationUser>().HasIndex(u => u.CalendarFeedToken).IsUnique();

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Supervisor)
            .WithMany(u => u.Subordinates)
            .HasForeignKey(u => u.SupervisorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<SessionPackage>()
            .Property(p => p.PricePerSession)
            .HasPrecision(10, 2);

        builder.Entity<Course>()
            .Property(c => c.Price)
            .HasPrecision(10, 2);

        builder.Entity<CourseEnrollment>()
            .HasOne(e => e.Course)
            .WithMany(c => c.Enrollments)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<CourseEnrollment>()
            .HasIndex(e => new { e.CourseId, e.ApplicationUserId });

        builder.Entity<CourseModule>()
            .HasOne(m => m.Course)
            .WithMany(c => c.Modules)
            .HasForeignKey(m => m.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Lesson>()
            .HasOne(l => l.Module)
            .WithMany(m => m.Lessons)
            .HasForeignKey(l => l.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<LessonProgress>()
            .HasOne(p => p.Lesson)
            .WithMany()
            .HasForeignKey(p => p.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<LessonProgress>()
            .HasIndex(p => new { p.ApplicationUserId, p.LessonId })
            .IsUnique();

        builder.Entity<QuizQuestion>()
            .HasOne(q => q.Lesson)
            .WithMany(l => l.QuizQuestions)
            .HasForeignKey(q => q.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QuizOption>()
            .HasOne(o => o.Question)
            .WithMany(q => q.Options)
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QuizAttempt>()
            .HasOne(a => a.Lesson)
            .WithMany()
            .HasForeignKey(a => a.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QuizAttempt>()
            .HasIndex(a => new { a.ApplicationUserId, a.LessonId })
            .IsUnique();

        builder.Entity<Order>()
            .Property(o => o.Amount)
            .HasPrecision(10, 2);

        builder.Entity<Order>()
            .HasOne(o => o.Course)
            .WithMany()
            .HasForeignKey(o => o.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Order>()
            .HasOne(o => o.PackageOffer)
            .WithMany()
            .HasForeignKey(o => o.PackageOfferId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Order>()
            .HasIndex(o => o.ExtOrderId)
            .IsUnique();

        builder.Entity<PackageOffer>()
            .Property(p => p.Price)
            .HasPrecision(10, 2);

        builder.Entity<PackageOffer>()
            .HasOne(p => p.SessionType)
            .WithMany()
            .HasForeignKey(p => p.SessionTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // StartTime jest zegarem ściennym, nie instantem: sesja o 14:00 odbywa się
        // o 14:00 w studiu, niezależnie od strefy kontenera. Kolumna bez strefy
        // wymusza to na poziomie bazy i uniezależnia godziny od ustawienia TZ.
        // Zob. IAppClock po opis konwencji.
        builder.Entity<Session>()
            .Property(s => s.StartTime)
            .HasColumnType("timestamp without time zone");

        // StartTime to główny filtr zakresowy najgorętszych zapytań (kalendarz,
        // statystyki, przypomnienia), a tabela nie miała na nim żadnego indeksu.
        // (TrainerUserId, StartTime) pokrywa kalendarz trenera po dacie;
        // (StartTime) pokrywa zapytania globalne bez filtra trenera.
        builder.Entity<Session>()
            .HasIndex(s => new { s.TrainerUserId, s.StartTime });
        builder.Entity<Session>()
            .HasIndex(s => s.StartTime);
        // Dzień planu wybrany na wizytę — usunięcie dnia z planu nie kasuje wizyty, tylko czyści wybór.
        builder.Entity<Session>()
            .HasOne(s => s.PlanDay).WithMany()
            .HasForeignKey(s => s.PlanDayId)
            .OnDelete(DeleteBehavior.SetNull);

        // Optymistyczna współbieżność na liczniku kredytów pakietu. UsedSessions
        // jest inkrementowane/dekrementowane z wielu ścieżek (booking, seria,
        // anulowanie, płatność); bez tokena dwa równoległe zapisy gubiły jeden
        // z nich (lost update). xmin to systemowa kolumna Postgresa — nie dodaje
        // realnej kolumny, więc migracja nie ma DDL. Tylko dla Npgsql: prowider
        // InMemory (testy) go nie obsługuje i nie potrzebuje.
        //
        // Mapujemy shadow property "xmin" ręcznie przez rdzeniowe API EF zamiast
        // rozszerzenia UseXminAsConcurrencyToken (niedostępne w tej wersji
        // providera). Efekt jest identyczny: store-generated token współbieżności.
        if (Database.IsNpgsql())
            builder.Entity<SessionPackage>()
                .Property<uint>("xmin")
                .IsConcurrencyToken()
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("xid")
                .HasColumnName("xmin");

        builder.Entity<Session>()
            .HasOne(s => s.Package)
            .WithMany(p => p.Sessions)
            .HasForeignKey(s => s.PackageId)
            .OnDelete(DeleteBehavior.SetNull);

        // Trening w parze: wizyty obu osób łączy PairGroupId.
        builder.Entity<Session>()
            .HasIndex(s => s.PairGroupId);

        builder.Entity<SessionPackage>()
            .HasOne(p => p.PartnerClient)
            .WithMany()
            .HasForeignKey(p => p.PartnerClientId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Session>()
            .HasOne(s => s.Series)
            .WithMany(sr => sr.Sessions)
            .HasForeignKey(s => s.SeriesId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ClientContact>()
            .HasOne(cc => cc.Client1)
            .WithMany()
            .HasForeignKey(cc => cc.Client1Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ClientContact>()
            .HasOne(cc => cc.Client2)
            .WithMany()
            .HasForeignKey(cc => cc.Client2Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ClientContact>()
            .HasIndex(cc => new { cc.Client1Id, cc.Client2Id })
            .IsUnique();

        builder.Entity<SessionInvitation>()
            .HasOne(i => i.Session)
            .WithMany(s => s.Invitations)
            .HasForeignKey(i => i.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SessionInvitation>()
            .HasOne(i => i.InvitedClient)
            .WithMany()
            .HasForeignKey(i => i.InvitedClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<TrainerConfig>()
            .HasIndex(tc => tc.TrainerUserId)
            .IsUnique();

        builder.Entity<NotificationPreferences>()
            .HasIndex(n => n.UserId)
            .IsUnique();

        builder.Entity<ClientMedal>(e =>
        {
            e.HasIndex(x => new { x.ClientId, x.Key }).IsUnique();
            e.Property(x => x.Key).HasMaxLength(40);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainerStyle>(e =>
        {
            e.HasIndex(x => x.TrainerUserId).IsUnique();
            e.Property(x => x.Address).HasMaxLength(10);
            e.Property(x => x.Emoji).HasMaxLength(10);
            e.Property(x => x.Length).HasMaxLength(10);
            e.Property(x => x.Greeting).HasMaxLength(40);
            e.Property(x => x.Signature).HasMaxLength(80);
            e.Property(x => x.Notes).HasMaxLength(300);
        });
        builder.Entity<ShortLink>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.TargetPath).HasMaxLength(500);
        });
        // Zegarek klienta (Garmin przez Portal): jedno połączenie na klienta i dostawcę, dzień i aktywność bez duplikatów.
        builder.Entity<WearableLink>(e =>
        {
            e.HasIndex(x => new { x.ClientId, x.Provider }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.DeviceName).HasMaxLength(100);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<WearableDay>(e =>
        {
            e.HasIndex(x => new { x.ClientId, x.Provider, x.Date }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(20);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<WearableActivity>(e =>
        {
            e.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.ClientId, x.StartUtc });
            e.Property(x => x.Provider).HasMaxLength(20);
            e.Property(x => x.ExternalId).HasMaxLength(80);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Type).HasMaxLength(60);
            e.Property(x => x.DeviceName).HasMaxLength(100);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<WearableWorkout>(e =>
        {
            e.HasIndex(x => new { x.ClientId, x.Date });
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<SlotWatch>(e =>
        {
            e.HasIndex(x => x.ClientId).IsUnique();
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SlotInvitation>(e =>
        {
            e.HasIndex(x => new { x.ClientId, x.SentAt });
            e.HasIndex(x => new { x.TrainerUserId, x.SlotStart });
            e.Property(x => x.Channel).HasMaxLength(10);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ShortLink).WithMany().HasForeignKey(x => x.ShortLinkId).OnDelete(DeleteBehavior.SetNull);
        });
        builder.Entity<TrainerStyleExample>(e =>
        {
            e.HasIndex(x => new { x.TrainerUserId, x.CreatedAt });
            e.Property(x => x.Text).HasMaxLength(1000);
            e.Property(x => x.Purpose).HasMaxLength(20);
            e.Property(x => x.Source).HasMaxLength(20);
        });

        builder.Entity<RolePermission>()
            .HasIndex(rp => new { rp.Role, rp.Permission })
            .IsUnique();

        builder.Entity<PushSubscription>()
            .HasIndex(ps => new { ps.UserId, ps.Endpoint })
            .IsUnique();

        builder.Entity<LoginLog>()
            .HasIndex(l => l.UserId);

        builder.Entity<FinanceTaxConfig>()
            .HasIndex(f => f.Module)
            .IsUnique();

        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.VatRate).HasPrecision(5, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.FlatTaxRate).HasPrecision(5, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.LumpSumRate).HasPrecision(5, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.ScaleTaxThreshold).HasPrecision(12, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.ScaleTaxRateLow).HasPrecision(5, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.ScaleTaxRateHigh).HasPrecision(5, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.ZusMonthlyAmount).HasPrecision(10, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.HealthInsuranceMonthly).HasPrecision(10, 2);
        builder.Entity<FinanceTaxConfig>()
            .Property(f => f.MonthlyFixedCosts).HasPrecision(10, 2);

        // ---- Moduł treningowy (kreator planów) ----

        builder.Entity<Exercise>(e =>
        {
            // Dedup przy (ponownym) seedzie Free Exercise DB. SourceKey null dla
            // ćwiczeń własnych trenera; w Postgresie wiele NULL-i nie łamie unikatu.
            e.HasIndex(x => x.SourceKey).IsUnique();
            // Katalog filtruje po właścicielu (moje/publiczne) i widoczności.
            e.HasIndex(x => x.OwnerTrainerUserId);
            e.HasIndex(x => new { x.Visibility, x.Category });
            // Katalog publiczny pokazuje tylko wybrane źródło (Free Exercise DB albo wger).
            e.HasIndex(x => new { x.Visibility, x.Source });
            e.Property(x => x.Attribution).HasMaxLength(1000);
        });

        builder.Entity<TrainerExercisePref>(e =>
        {
            e.HasOne(p => p.Exercise)
             .WithMany()
             .HasForeignKey(p => p.ExerciseId)
             .OnDelete(DeleteBehavior.Cascade);
            // Jeden wiersz preferencji na parę (trener, ćwiczenie).
            e.HasIndex(p => new { p.TrainerUserId, p.ExerciseId }).IsUnique();
        });

        builder.Entity<TrainingPlan>(e =>
        {
            e.HasOne(p => p.Client)
             .WithMany()
             .HasForeignKey(p => p.ClientId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => p.TrainerUserId);
        });

        builder.Entity<PlanDay>(e =>
        {
            e.HasOne(d => d.Plan)
             .WithMany(p => p.Days)
             .HasForeignKey(d => d.PlanId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CalendarConnection>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.Mode).HasMaxLength(16);
            e.Property(x => x.GoogleEmail).HasMaxLength(256);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.UserId).IsUnique();
        });

        builder.Entity<CalendarBusyBlock>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.HasIndex(x => new { x.UserId, x.StartTime });
        });

        builder.Entity<PlanExercise>(e =>
        {
            e.HasOne(pe => pe.PlanDay)
             .WithMany(d => d.Exercises)
             .HasForeignKey(pe => pe.PlanDayId)
             .OnDelete(DeleteBehavior.Cascade);
            // Nie kasuj pozycji planu przez usunięcie ćwiczenia — blokuj usunięcie
            // ćwiczenia, które jest w użyciu.
            e.HasOne(pe => pe.Exercise)
             .WithMany()
             .HasForeignKey(pe => pe.ExerciseId)
             .OnDelete(DeleteBehavior.Restrict);
            e.Property(pe => pe.TargetWeightKg).HasPrecision(6, 2);
        });

        builder.Entity<WorkoutLog>(e =>
        {
            e.HasOne(w => w.Client)
             .WithMany()
             .HasForeignKey(w => w.ClientId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.PlanExercise)
             .WithMany()
             .HasForeignKey(w => w.PlanExerciseId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(w => w.Exercise)
             .WithMany()
             .HasForeignKey(w => w.ExerciseId)
             .OnDelete(DeleteBehavior.Restrict);
            // Główne zapytania wykresów: po kliencie i dacie.
            e.HasIndex(w => new { w.ClientId, w.WorkoutDate });
        });

        builder.Entity<WorkoutSetLog>(e =>
        {
            e.HasOne(s => s.WorkoutLog)
             .WithMany(w => w.Sets)
             .HasForeignKey(s => s.WorkoutLogId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.WeightKg).HasPrecision(6, 2);
        });

        builder.Entity<WorkoutSession>(e =>
        {
            e.HasOne(s => s.Client)
             .WithMany()
             .HasForeignKey(s => s.ClientId)
             .OnDelete(DeleteBehavior.Cascade);
            // Jeden otwarty draft na klienta (upsert po ClientId).
            e.HasIndex(s => s.ClientId).IsUnique();
        });

        builder.Entity<TrainerConfig>(e => e.HasIndex(c => c.CalendarFeedToken).IsUnique());

        builder.Entity<SurveyTemplate>(e => e.HasIndex(t => t.Kind).IsUnique());

        builder.Entity<AutomationRule>(e =>
        {
            e.HasIndex(r => r.Kind).IsUnique();
            e.Property(r => r.Kind).HasMaxLength(40);
            e.Property(r => r.Subject).HasMaxLength(200);
            e.Property(r => r.Message).HasMaxLength(2000);
            e.Property(r => r.ButtonText).HasMaxLength(60);
        });
        builder.Entity<ClientDocument>(e =>
        {
            e.Property(d => d.Title).HasMaxLength(150);
            e.Property(d => d.Kind).HasMaxLength(20);
        });
        builder.Entity<DocumentAcceptance>(e =>
        {
            e.HasOne(a => a.Document).WithMany().HasForeignKey(a => a.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Client).WithMany().HasForeignKey(a => a.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.DocumentId, a.ClientId });
            e.Property(a => a.IpAddress).HasMaxLength(45);
        });
        builder.Entity<AutomationLog>(e =>
        {
            e.HasOne(l => l.Client).WithMany().HasForeignKey(l => l.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.ClientId, l.Kind });
            e.HasIndex(l => l.SentAt);
            e.Property(l => l.Kind).HasMaxLength(40);
            e.Property(l => l.Channels).HasMaxLength(40);
            e.Property(l => l.CouponCode).HasMaxLength(40);
        });

        builder.Entity<SurveyResponse>(e =>
        {
            e.HasOne(r => r.Client)
             .WithMany()
             .HasForeignKey(r => r.ClientId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.ClientId, r.Kind, r.WorkoutDate });
            e.Property(r => r.FilledByStaffUserId).HasMaxLength(450);
        });

        builder.Entity<ChatMessage>(e =>
        {
            e.Property(m => m.Body).HasMaxLength(4000);
            e.HasOne(m => m.Client).WithMany().HasForeignKey(m => m.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.ClientId, m.SentAt });
        });

        builder.Entity<ProgressPhoto>(e =>
        {
            e.Property(p => p.FileName).HasMaxLength(64);
            e.Property(p => p.Note).HasMaxLength(500);
            e.Property(p => p.UploadedByUserId).HasMaxLength(450);
            e.HasOne(p => p.Client).WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.ClientId, p.TakenOn });
        });

        builder.Entity<Client>(e =>
        {
            e.Property(c => c.ReferralCode).HasMaxLength(16);
            e.HasIndex(c => c.ReferralCode).IsUnique();
        });

        builder.Entity<ClientReview>(e =>
        {
            e.Property(r => r.Text).HasMaxLength(1000);
            e.Property(r => r.DisplayName).HasMaxLength(80);
            e.HasOne(r => r.Client).WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.ClientId).IsUnique();
            e.HasIndex(r => r.IsPublished);
        });

        builder.Entity<GiftVoucher>(e =>
        {
            e.Property(v => v.Code).HasMaxLength(20);
            e.HasIndex(v => v.Code).IsUnique();
            e.Property(v => v.Value).HasPrecision(10, 2);
            e.Property(v => v.Title).HasMaxLength(200);
            e.Property(v => v.RecipientName).HasMaxLength(80);
            e.Property(v => v.FromName).HasMaxLength(80);
            e.Property(v => v.Message).HasMaxLength(300);
            e.HasIndex(v => v.BuyerUserId);
        });

        builder.Entity<MarketingSettings>(e =>
        {
            e.Property(m => m.ReferrerRewardValue).HasPrecision(10, 2);
            e.Property(m => m.GoogleReviewUrl).HasMaxLength(500);
        });

        builder.Entity<Referral>(e =>
        {
            e.HasOne(r => r.ReferrerClient).WithMany().HasForeignKey(r => r.ReferrerClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.ReferredClient).WithMany().HasForeignKey(r => r.ReferredClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => r.ReferredClientId).IsUnique();
            e.HasIndex(r => new { r.ReferrerClientId, r.Status });
            e.Property(r => r.RewardDescription).HasMaxLength(200);
            e.Property(r => r.RewardCouponCode).HasMaxLength(40);
            e.Property(r => r.FriendCouponCode).HasMaxLength(40);
        });

        builder.Entity<MembershipPlan>(e =>
        {
            e.Property(p => p.Price).HasPrecision(10, 2);
            e.HasOne(p => p.SessionType).WithMany().HasForeignKey(p => p.SessionTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Membership>(e =>
        {
            e.Property(m => m.PriceOverride).HasPrecision(10, 2);
            e.HasOne(m => m.Client).WithMany().HasForeignKey(m => m.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.Plan).WithMany().HasForeignKey(m => m.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.Status, m.NextBillingDate });
        });

        builder.Entity<MembershipPeriod>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(10, 2);
            e.HasOne(p => p.Membership).WithMany(m => m.Periods).HasForeignKey(p => p.MembershipId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Package).WithMany().HasForeignKey(p => p.PackageId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => new { p.MembershipId, p.PeriodStart }).IsUnique();
        });

        builder.Entity<WorkoutComment>(e =>
        {
            e.HasOne(c => c.Client)
             .WithMany()
             .HasForeignKey(c => c.ClientId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Text).HasMaxLength(1000);
            e.HasIndex(c => new { c.ClientId, c.WorkoutDate });
        });
    }
}
