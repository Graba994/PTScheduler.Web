using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class SessionPackageServiceAuditTests
{
    [Fact]
    public async Task ExpireOldPackagesAsync_ExpiresPastDue_WritesPackagesExpiredAuditLog()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "client-1", FirstName = "Jan", LastName = "Kowalski" });
        db.SessionPackages.Add(new SessionPackage
        {
            ClientId = 1,
            SessionTypeId = 1,
            Name = "Pakiet testowy",
            TotalSessions = 10,
            UsedSessions = 2,
            Status = PackageStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var auditLog = new Mock<IAuditLogService>();
        var svc = new SessionPackageService(
            factory,
            new Mock<IEmailService>().Object,
            new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object,
            auditLog.Object,
            NullLogger<SessionPackageService>.Instance);

        var count = await svc.ExpireOldPackagesAsync();

        count.Should().Be(1);
        auditLog.Verify(a => a.LogAsync(
            "system", "system", "System", "PackagesExpired", "SessionPackage", null, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExpireOldPackagesAsync_NothingToExpire_DoesNotWriteAuditLog()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "client-1", FirstName = "Jan", LastName = "Kowalski" });
        db.SessionPackages.Add(new SessionPackage
        {
            ClientId = 1,
            SessionTypeId = 1,
            Name = "Pakiet aktywny",
            TotalSessions = 10,
            UsedSessions = 2,
            Status = PackageStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();

        var auditLog = new Mock<IAuditLogService>();
        var svc = new SessionPackageService(
            factory,
            new Mock<IEmailService>().Object,
            new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object,
            auditLog.Object,
            NullLogger<SessionPackageService>.Instance);

        var count = await svc.ExpireOldPackagesAsync();

        count.Should().Be(0);
        auditLog.Verify(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
