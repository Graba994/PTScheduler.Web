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

public class SessionPackageServiceTests
{
    [Fact]
    public async Task CreatePackage_FulfillsAwaitingSessions()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(1), Status = SessionStatus.AwaitingPackage
        });
        db.Sessions.Add(new Session
        {
            Id = 2, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
            StartTime = DateTime.UtcNow.AddDays(2), Status = SessionStatus.AwaitingPackage
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var result = await svc.CreatePackageAsync(new CreateSessionPackageDto
        {
            ClientId = 1, SessionTypeId = 1, CreatedByUserId = "t1",
            TotalSessions = 10, PricePerSession = 100
        });

        result.UsedSessions.Should().Be(2);
        result.Status.Should().Be(PackageStatus.Active);

        await using var verify = factory.CreateDbContext();
        var s1 = await verify.Sessions.FindAsync(1);
        s1!.Status.Should().Be(SessionStatus.Scheduled);
        s1.PackageId.Should().Be(result.Id);
    }

    [Fact]
    public async Task CreatePackage_FulfillsUpToCapacity()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        for (int i = 1; i <= 5; i++)
        {
            db.Sessions.Add(new Session
            {
                Id = i, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1",
                StartTime = DateTime.UtcNow.AddDays(i), Status = SessionStatus.AwaitingPackage
            });
        }
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var result = await svc.CreatePackageAsync(new CreateSessionPackageDto
        {
            ClientId = 1, SessionTypeId = 1, CreatedByUserId = "t1",
            TotalSessions = 3, PricePerSession = 100
        });

        result.UsedSessions.Should().Be(3);
        result.Status.Should().Be(PackageStatus.Depleted);

        await using var verify = factory.CreateDbContext();
        var awaiting = verify.Sessions.Count(s => s.Status == SessionStatus.AwaitingPackage);
        awaiting.Should().Be(2);
    }

    [Fact]
    public async Task CreatePackage_AutoNamesFromSessionType()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var result = await svc.CreatePackageAsync(new CreateSessionPackageDto
        {
            ClientId = 1, SessionTypeId = 1, CreatedByUserId = "t1",
            TotalSessions = 5, PricePerSession = 100
        });

        result.Name.Should().Contain("Trening");
        result.Name.Should().Contain("5");
    }

    [Fact]
    public async Task ExpireOldPackages_ExpiresOnlyPastDue()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Expired",
            TotalSessions = 10, UsedSessions = 2, Status = PackageStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 2, ClientId = 1, SessionTypeId = 1, Name = "StillActive",
            TotalSessions = 10, UsedSessions = 2, Status = PackageStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        var count = await svc.ExpireOldPackagesAsync();

        count.Should().Be(1);

        await using var verify = factory.CreateDbContext();
        var expired = await verify.SessionPackages.FindAsync(1);
        expired!.Status.Should().Be(PackageStatus.Expired);

        var active = await verify.SessionPackages.FindAsync(2);
        active!.Status.Should().Be(PackageStatus.Active);
    }

    [Fact]
    public async Task MarkPaid_SetsFlag()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 10, Status = PackageStatus.Active
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.MarkPaidAsync(1);

        await using var verify = factory.CreateDbContext();
        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.IsPaid.Should().BeTrue();
        pkg.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelPackage_SetsCancelledStatus()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.SessionPackages.Add(new SessionPackage
        {
            Id = 1, ClientId = 1, SessionTypeId = 1, Name = "Pak",
            TotalSessions = 10, UsedSessions = 3, Status = PackageStatus.Active
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);
        await svc.CancelPackageAsync(1);

        await using var verify = factory.CreateDbContext();
        var pkg = await verify.SessionPackages.FindAsync(1);
        pkg!.Status.Should().Be(PackageStatus.Cancelled);
    }

    private static SessionPackageService MakeService(
        Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.Data.ApplicationDbContext> factory)
    {
        return new SessionPackageService(
            factory,
            new Mock<IEmailService>().Object,
            new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object,
            new Mock<IAuditLogService>().Object,
            NullLogger<SessionPackageService>.Instance);
    }
}
