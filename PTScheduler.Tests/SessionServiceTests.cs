using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class SessionServiceTests
{
    [Fact]
    public async Task CreateSession_WithActivePackage_UsesPackageAndSetsScheduled()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 10, UsedSessions = 0, Status = PackageStatus.Active
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var result = await svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1)
        });

        result.Status.Should().Be(SessionStatus.Scheduled);

        await using var verify = factory.CreateDbContext();
        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.UsedSessions.Should().Be(1);
        pkg.Status.Should().Be(PackageStatus.Active);
    }

    [Fact]
    public async Task CreateSession_PackageFull_TransitionsToDepleted()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 1, UsedSessions = 0, Status = PackageStatus.Active
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1)
        });

        await using var verify = factory.CreateDbContext();
        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.UsedSessions.Should().Be(1);
        pkg.Status.Should().Be(PackageStatus.Depleted);
    }

    [Fact]
    public async Task CreateSession_NoPackage_AllowAwaiting_SetsAwaitingPackage()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var result = await svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1)
        });

        result.Status.Should().Be(SessionStatus.AwaitingPackage);
    }

    [Fact]
    public async Task CreateSession_NoPackage_DisallowAwaiting_Throws()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);

        var act = () => svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1)
        }, allowAwaitingPackage: false);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateSession_PicksEarliestExpiringPackage()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Later",
            TotalSessions = 10, UsedSessions = 0, Status = PackageStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 2, ClientId = 1, SessionTypeId = 1, Name = "Sooner",
            TotalSessions = 10, UsedSessions = 0, Status = PackageStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(5)
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1)
        });

        await using var verify = factory.CreateDbContext();
        var sooner = await verify.SessionPackages.FindAsync(2);
        var later = await verify.SessionPackages.FindAsync(1);
        sooner!.UsedSessions.Should().Be(1);
        later!.UsedSessions.Should().Be(0);
    }

    [Fact]
    public async Task Cancel_ReturnsCreditToPackage()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 5, UsedSessions = 3, Status = PackageStatus.Active
        });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1), Status = SessionStatus.Scheduled,
            PackageId = 1
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.UpdateStatusAsync(1, SessionStatus.Cancelled, "test");

        await using var verify = factory.CreateDbContext();
        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.UsedSessions.Should().Be(2);
    }

    [Fact]
    public async Task Cancel_DepletedPackage_FlipsBackToActive()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 5, UsedSessions = 5, Status = PackageStatus.Depleted
        });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1), Status = SessionStatus.Scheduled,
            PackageId = 1
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.UpdateStatusAsync(1, SessionStatus.Cancelled);

        await using var verify = factory.CreateDbContext();
        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.Status.Should().Be(PackageStatus.Active);
        pkg.UsedSessions.Should().Be(4);
    }

    [Fact]
    public async Task Restore_CancelledSession_ReIncrementsCreditAndSetsScheduled()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 5, UsedSessions = 2, Status = PackageStatus.Active
        });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1), Status = SessionStatus.Cancelled,
            PackageId = 1, CancelledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.RestoreAsync(1);

        await using var verify = factory.CreateDbContext();
        var session = await verify.Sessions.FindAsync(1);
        session!.Status.Should().Be(SessionStatus.Scheduled);
        session.CancelledAt.Should().BeNull();

        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.UsedSessions.Should().Be(3);
    }

    [Fact]
    public async Task Restore_ScheduledSession_Throws()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1), Status = SessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var act = () => svc.RestoreAsync(1);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Restore_CancelledPackage_DetachesAndSetsAwaitingPackage()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 5, UsedSessions = 2, Status = PackageStatus.Cancelled
        });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1), Status = SessionStatus.Cancelled,
            PackageId = 1, CancelledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.RestoreAsync(1);

        await using var verify = factory.CreateDbContext();
        var session = await verify.Sessions.FindAsync(1);
        session!.Status.Should().Be(SessionStatus.AwaitingPackage);
        session.PackageId.Should().BeNull();
    }

    [Fact]
    public async Task Complete_StoresCompletionNotes()
    {
        var (factory, db) = TestDb.CreateFresh();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddHours(-1), Status = SessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.UpdateStatusAsync(1, SessionStatus.Completed, completionNotes: "Dobra forma");

        await using var verify = factory.CreateDbContext();
        var session = await verify.Sessions.FindAsync(1);
        session!.Status.Should().Be(SessionStatus.Completed);
        session.Notes.Should().Be("Dobra forma");
    }

    private static void SeedSessionType(Infrastructure.Data.ApplicationDbContext db)
    {
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
    }

    private static SessionService MakeService(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.Data.ApplicationDbContext> factory,
        PTScheduler.Application.Interfaces.IAppClock? clock = null)
    {
        return new SessionService(
            factory,
            new Mock<IEmailService>().Object,
            new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object,
            new Mock<IGoogleMeetService>().Object,
            clock ?? Helpers.TestClock.AtWallClock(DateTime.Now),
            NullLogger<SessionService>.Instance);
    }
}
