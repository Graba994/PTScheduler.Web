using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    // ── Kontrola kolizji terminów (punkt 02 audytu) ────────────────────────

    private static async Task SeedExistingSessionAsync(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.Data.ApplicationDbContext> factory,
        DateTime start)
    {
        await using var db = factory.CreateDbContext();
        SeedSessionType(db);
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "Kowalski" });
        db.Clients.Add(new Client { Id = 2, ApplicationUserId = "c2", FirstName = "Anna", LastName = "Nowak" });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = start, Status = SessionStatus.Scheduled
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateSession_OverlappingSlot_ThrowsSlotConflict()
    {
        var (factory, _) = TestDb.CreateFresh();
        await SeedExistingSessionAsync(factory, new DateTime(2026, 6, 1, 10, 0, 0)); // 10:00–11:00

        var svc = MakeService(factory);
        var act = () => svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 2, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = new DateTime(2026, 6, 1, 10, 30, 0) // wchodzi w 10:00–11:00
        });

        var ex = await act.Should().ThrowAsync<PTScheduler.Application.Exceptions.SlotConflictException>();
        ex.Which.Conflict.ClientName.Should().Be("Jan Kowalski");
    }

    [Fact]
    public async Task CreateSession_OverlappingSlot_WithAllowOverlap_Succeeds()
    {
        var (factory, _) = TestDb.CreateFresh();
        await SeedExistingSessionAsync(factory, new DateTime(2026, 6, 1, 10, 0, 0));

        var svc = MakeService(factory);
        var result = await svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 2, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = new DateTime(2026, 6, 1, 10, 30, 0)
        }, allowOverlap: true);

        result.Should().NotBeNull();

        await using var verify = factory.CreateDbContext();
        (await verify.Sessions.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateSession_BackToBackSlot_IsAllowed()
    {
        var (factory, _) = TestDb.CreateFresh();
        await SeedExistingSessionAsync(factory, new DateTime(2026, 6, 1, 10, 0, 0)); // kończy 11:00

        var svc = MakeService(factory);
        var act = () => svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 2, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = new DateTime(2026, 6, 1, 11, 0, 0) // styk koniec-w-koniec
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateSession_DifferentTrainer_NoConflict()
    {
        var (factory, _) = TestDb.CreateFresh();
        await SeedExistingSessionAsync(factory, new DateTime(2026, 6, 1, 10, 0, 0));

        var svc = MakeService(factory);
        var act = () => svc.CreateSessionAsync(new CreateSessionDto
        {
            ClientId = 2, SessionTypeId = 1, TrainerUserId = "t2", // inny trener
            StartTime = new DateTime(2026, 6, 1, 10, 30, 0)
        });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Reschedule_OntoOwnAdjacentTime_DoesNotConflictWithItself()
    {
        var (factory, _) = TestDb.CreateFresh();
        await SeedExistingSessionAsync(factory, new DateTime(2026, 6, 1, 10, 0, 0));

        var svc = MakeService(factory);
        // Przesunięcie o 30 min — nowy termin nachodzi na STARY termin tej samej sesji.
        var act = () => svc.RescheduleAsync(1, new DateTime(2026, 6, 1, 10, 30, 0));

        await act.Should().NotThrowAsync();

        await using var verify = factory.CreateDbContext();
        (await verify.Sessions.FindAsync(1))!.StartTime.Should().Be(new DateTime(2026, 6, 1, 10, 30, 0));
    }

    [Fact]
    public async Task Reschedule_OntoAnotherSessionsSlot_ThrowsSlotConflict()
    {
        var (factory, _) = TestDb.CreateFresh();
        await SeedExistingSessionAsync(factory, new DateTime(2026, 6, 1, 10, 0, 0)); // sesja 1, klient Jan
        await using (var db = factory.CreateDbContext())
        {
            db.Sessions.Add(new Session
            {
                Id = 2, ClientId = 2, SessionTypeId = 1, TrainerUserId = "t1",
                StartTime = new DateTime(2026, 6, 1, 14, 0, 0), Status = SessionStatus.Scheduled
            });
            await db.SaveChangesAsync();
        }

        var svc = MakeService(factory);
        // Przenosimy sesję 2 na termin nachodzący na sesję 1.
        var act = () => svc.RescheduleAsync(2, new DateTime(2026, 6, 1, 10, 30, 0));

        var ex = await act.Should().ThrowAsync<PTScheduler.Application.Exceptions.SlotConflictException>();
        ex.Which.Conflict.SessionId.Should().Be(1);
    }

    private static void SeedSessionType(Infrastructure.Data.ApplicationDbContext db)
    {
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
    }

    private static SessionService MakeService(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.Data.ApplicationDbContext> factory,
        PTScheduler.Application.Interfaces.IAppClock? clock = null)
    {
        // Prawdziwy TrainerAvailabilityService na tej samej bazie in-memory —
        // dzięki temu testy kolizji sprawdzają realną logikę nakładania, a nie atrapę.
        var availability = new TrainerAvailabilityService(factory);
        return new SessionService(
            factory,
            new Mock<IEmailService>().Object,
            new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object,
            new Mock<IGoogleMeetService>().Object,
            availability,
            clock ?? Helpers.TestClock.AtWallClock(DateTime.Now),
            NullLogger<SessionService>.Instance);
    }
}
