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
                PaymentsEnabled = false,
                CoursesEnabled = false,
                CustomDomain = false,
                PrioritySupport = false,
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
                PaymentsEnabled = true,
                CoursesEnabled = true,
                CustomDomain = false,
                PrioritySupport = false,
                SortOrder = 2,
                IsFeatured = true
            },
            new Plan
            {
                Id = "studio",
                Name = "Studio",
                Description = "Bez limitów, własna domena, priorytetowe wsparcie",
                MonthlyPrice = 149,
                YearlyPrice = 1490,
                MaxClients = int.MaxValue,
                PaymentsEnabled = true,
                CoursesEnabled = true,
                CustomDomain = true,
                PrioritySupport = true,
                SortOrder = 3,
                IsFeatured = false
            }
        );
    }
}
