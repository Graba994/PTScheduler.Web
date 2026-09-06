using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Entities;

namespace PTScheduler.Infrastructure.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionType> SessionTypes => Set<SessionType>();
    public DbSet<BodyMeasurement> BodyMeasurements => Set<BodyMeasurement>();
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
    }
}
