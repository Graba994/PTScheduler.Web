using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Lista „Wymaga uwagi”: dawno bez wizyty, kończący się / wygasający / pusty pakiet,
/// plan bez treningów — oraz reguły przypomnień i polska odmiana liczebników.
/// </summary>
public class ClientAttentionTests
{
    private const string TRAINER = "trainer-1";
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0);

    private static ClientAttentionService Make(Microsoft.EntityFrameworkCore.IDbContextFactory<PTScheduler.Infrastructure.Data.ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(Now));

    private static async Task SeedClientAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<PTScheduler.Infrastructure.Data.ApplicationDbContext> f, int id, string first)
    {
        await using var db = f.CreateDbContext();
        if (!db.SessionTypes.Any()) db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = id, ApplicationUserId = $"u{id}", FirstName = first, LastName = "Test", TrainerUserId = TRAINER, Status = ClientStatus.Active });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoVisit_Flagged_After_14_Days_Without_Upcoming()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedClientAsync(f, 1, "Anna");
        await SeedClientAsync(f, 2, "Bartek");
        await using (var db = f.CreateDbContext())
        {
            db.Sessions.Add(new Session { ClientId = 1, SessionTypeId = 1, TrainerUserId = TRAINER, StartTime = Now.AddDays(-20), Status = SessionStatus.Completed });
            // Bartek: dawno był, ale ma zaplanowaną wizytę — nie flagujemy.
            db.Sessions.Add(new Session { ClientId = 2, SessionTypeId = 1, TrainerUserId = TRAINER, StartTime = Now.AddDays(-20), Status = SessionStatus.Completed });
            db.Sessions.Add(new Session { ClientId = 2, SessionTypeId = 1, TrainerUserId = TRAINER, StartTime = Now.AddDays(2), Status = SessionStatus.Scheduled });
            await db.SaveChangesAsync();
        }

        var list = await Make(f).GetAsync(TRAINER, includeTraining: false);

        list.Should().ContainSingle();
        list[0].ClientId.Should().Be(1);
        list[0].Reasons.Should().ContainSingle(r => r.Kind == AttentionKind.NoVisit);
    }

    [Fact]
    public async Task Packages_LowCredits_Expiring_And_Empty()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedClientAsync(f, 1, "Low");
        await SeedClientAsync(f, 2, "Expiring");
        await SeedClientAsync(f, 3, "Empty");
        var utc = TestClock.AtWallClock(Now).UtcNow;
        await using (var db = f.CreateDbContext())
        {
            db.SessionPackages.Add(new SessionPackage { ClientId = 1, SessionTypeId = 1, Name = "P1", TotalSessions = 10, UsedSessions = 8, Status = PackageStatus.Active, PurchasedAt = utc.AddDays(-30) });
            db.SessionPackages.Add(new SessionPackage { ClientId = 2, SessionTypeId = 1, Name = "P2", TotalSessions = 10, UsedSessions = 2, Status = PackageStatus.Active, PurchasedAt = utc.AddDays(-30), ExpiresAt = utc.AddDays(2) });
            db.SessionPackages.Add(new SessionPackage { ClientId = 3, SessionTypeId = 1, Name = "P3", TotalSessions = 10, UsedSessions = 10, Status = PackageStatus.Depleted, PurchasedAt = utc.AddDays(-10) });
            await db.SaveChangesAsync();
        }

        var list = await Make(f).GetAsync(TRAINER, includeTraining: false);

        list.Single(c => c.ClientId == 1).Reasons.Should().ContainSingle(r => r.Kind == AttentionKind.LowCredits);
        var expiring = list.Single(c => c.ClientId == 2);
        expiring.Reasons.Should().ContainSingle(r => r.Kind == AttentionKind.PackageExpiring);
        expiring.Severity.Should().Be(AttentionSeverity.Danger);
        list.Single(c => c.ClientId == 3).Reasons.Should().ContainSingle(r => r.Kind == AttentionKind.PackageEmpty);
        list.First().Severity.Should().Be(AttentionSeverity.Danger); // najpilniejsi na górze
    }

    [Fact]
    public async Task NotTraining_Only_With_Training_Module()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedClientAsync(f, 1, "Anna");
        var utc = TestClock.AtWallClock(Now).UtcNow;
        await using (var db = f.CreateDbContext())
        {
            db.TrainingPlans.Add(new TrainingPlan { TrainerUserId = TRAINER, ClientId = 1, Name = "Plan", CreatedAt = utc.AddDays(-10) });
            await db.SaveChangesAsync();
        }

        (await Make(f).GetAsync(TRAINER, includeTraining: false)).Should().BeEmpty();
        var list = await Make(f).GetAsync(TRAINER, includeTraining: true);
        list.Single().Reasons.Should().ContainSingle(r => r.Kind == AttentionKind.NotTraining);
    }

    [Theory]
    [InlineData(1, "1 trening")]
    [InlineData(2, "2 treningi")]
    [InlineData(5, "5 treningów")]
    [InlineData(12, "12 treningów")]
    [InlineData(22, "22 treningi")]
    public void Trainings_Polish_Plural(int n, string expected) =>
        AttentionRules.Trainings(n).Should().Be(expected);

    [Theory]
    [InlineData(10, 8, true)]   // zostały 2
    [InlineData(10, 9, true)]   // został 1
    [InlineData(2, 0, false)]   // świeży pakiet 2-wizytowy — nie przypominamy od razu
    [InlineData(10, 10, false)] // nic nie zostało
    [InlineData(10, 5, false)]
    public void LowCredits_Notification_Rule(int total, int used, bool expected) =>
        AttentionRules.ShouldNotifyLowCredits(total, used).Should().Be(expected);
}
