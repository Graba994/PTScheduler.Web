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

    public DbSet<AcademyCourse> AcademyCourses => Set<AcademyCourse>();
    public DbSet<AcademyModule> AcademyModules => Set<AcademyModule>();
    public DbSet<AcademyLesson> AcademyLessons => Set<AcademyLesson>();
    public DbSet<AcademyEnrollment> AcademyEnrollments => Set<AcademyEnrollment>();
    public DbSet<AcademyLessonProgress> AcademyLessonProgress => Set<AcademyLessonProgress>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Supervisor)
            .WithMany(u => u.Subordinates)
            .HasForeignKey(u => u.SupervisorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<SessionPackage>()
            .Property(p => p.PricePerSession)
            .HasPrecision(10, 2);

        builder.Entity<Session>()
            .HasOne(s => s.Package)
            .WithMany(p => p.Sessions)
            .HasForeignKey(s => s.PackageId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<AcademyModule>()
            .HasOne(m => m.Course)
            .WithMany(c => c.Modules)
            .HasForeignKey(m => m.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AcademyLesson>()
            .HasOne(l => l.Module)
            .WithMany(m => m.Lessons)
            .HasForeignKey(l => l.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AcademyEnrollment>()
            .HasOne(e => e.Course)
            .WithMany(c => c.Enrollments)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AcademyEnrollment>()
            .HasIndex(e => new { e.ApplicationUserId, e.CourseId })
            .IsUnique();

        builder.Entity<AcademyLessonProgress>()
            .HasOne(p => p.Lesson)
            .WithMany(l => l.Progress)
            .HasForeignKey(p => p.LessonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AcademyLessonProgress>()
            .HasIndex(p => new { p.ApplicationUserId, p.LessonId })
            .IsUnique();

        builder.Entity<Product>()
            .HasOne(p => p.Course)
            .WithMany()
            .HasForeignKey(p => p.CourseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(10, 2);

        builder.Entity<Order>()
            .HasOne(o => o.Product)
            .WithMany(p => p.Orders)
            .HasForeignKey(o => o.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Order>()
            .Property(o => o.Amount)
            .HasPrecision(10, 2);

        builder.Entity<Order>()
            .HasIndex(o => o.ExternalPaymentId);
    }
}
