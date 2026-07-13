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
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<WebPushSettings> WebPushSettings => Set<WebPushSettings>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<EmailTemplate>()
            .HasIndex(e => e.Key)
            .IsUnique();

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
    }
}
