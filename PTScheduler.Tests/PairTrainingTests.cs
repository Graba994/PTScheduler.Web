using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Trening w parze: dwie powiązane wizyty i poprawne rozliczanie pakietów.</summary>
public class PairTrainingTests
{
    private static (IDbContextFactory<ApplicationDbContext> F, SessionService Svc) Setup(Action<ApplicationDbContext> seed)
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Duet", DurationMinutes = 60, IsPair = true });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "ola", FirstName = "Ola" });
        db.Clients.Add(new Client { Id = 2, ApplicationUserId = "piotr", FirstName = "Piotr" });
        seed(db);
        db.SaveChanges();
        var svc = new SessionService(factory,
            new Mock<IEmailService>().Object, new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object, new Mock<IGoogleMeetService>().Object,
            new TrainerAvailabilityService(factory), TestClock.AtWallClock(DateTime.Now),
            NullLogger<SessionService>.Instance);
        return (factory, svc);
    }

    private static SessionPackage PairPackage(int total = 10) => new()
    {
        Id = 1, ClientId = 1, PartnerClientId = 2, SessionTypeId = 1, Name = "Duet 10",
        TotalSessions = total, Status = PackageStatus.Active
    };

    private static CreateSessionDto Dto(int clientId = 1) => new()
    {
        ClientId = clientId, SessionTypeId = 1, TrainerUserId = "t1", StartTime = DateTime.Now.AddDays(2)
    };

    [Fact]
    public async Task PairPackage_ConsumesOnePerJointTraining()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        var lead = await svc.CreatePairSessionAsync(Dto(), 2);

        await using var db = f.CreateDbContext();
        var rows = await db.Sessions.OrderBy(s => s.Id).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.PairGroupId).Distinct().Should().HaveCount(1);
        rows.Should().OnlyContain(r => r.Status == SessionStatus.Scheduled && r.PackageId == 1);
        rows[1].SharesPackageSlot.Should().BeTrue();
        (await db.SessionPackages.FindAsync(1))!.UsedSessions.Should().Be(1);
        lead.PartnerName.Should().Be("Piotr");
        lead.DisplayName.Should().Be("Ola + Piotr");
    }

    [Fact]
    public async Task PartnerCanBookWithPackageBoughtByOther()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        await svc.CreatePairSessionAsync(Dto(clientId: 2), 1);
        await using var db = f.CreateDbContext();
        (await db.SessionPackages.FindAsync(1))!.UsedSessions.Should().Be(1);
    }

    [Fact]
    public async Task LeadCancels_ConsumptionMovesToPartner()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        var lead = await svc.CreatePairSessionAsync(Dto(), 2);
        await svc.UpdateStatusAsync(lead.Id, SessionStatus.Cancelled);

        await using var db = f.CreateDbContext();
        (await db.SessionPackages.FindAsync(1))!.UsedSessions.Should().Be(1, "druga osoba nadal trenuje");
        var partner = await db.Sessions.SingleAsync(s => s.Id == lead.PartnerSessionId);
        partner.SharesPackageSlot.Should().BeFalse();

        var dto = await svc.GetSessionAsync(partner.Id);
        dto!.IsPairFollower.Should().BeFalse("po odwołaniu prowadzącej druga wizyta pokazuje trening");
    }

    [Fact]
    public async Task BothCancelled_PackageFullyRefunded()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        var lead = await svc.CreatePairSessionAsync(Dto(), 2);
        await svc.UpdateStatusAsync(lead.Id, SessionStatus.Cancelled, includePartner: true);

        await using var db = f.CreateDbContext();
        (await db.SessionPackages.FindAsync(1))!.UsedSessions.Should().Be(0);
        (await db.Sessions.CountAsync(s => s.Status == SessionStatus.Cancelled)).Should().Be(2);
    }

    [Fact]
    public async Task OneByOneCancel_ThenRestore_KeepsCountRight()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        var lead = await svc.CreatePairSessionAsync(Dto(), 2);
        var partnerId = lead.PartnerSessionId!.Value;
        await svc.UpdateStatusAsync(partnerId, SessionStatus.Cancelled);
        await svc.UpdateStatusAsync(lead.Id, SessionStatus.Cancelled);
        await svc.RestoreAsync(partnerId);

        await using var db = f.CreateDbContext();
        (await db.SessionPackages.FindAsync(1))!.UsedSessions.Should().Be(1);
        await svc.RestoreAsync(lead.Id);
        await using var db2 = f.CreateDbContext();
        (await db2.SessionPackages.FindAsync(1))!.UsedSessions.Should().Be(1, "wspólny trening to wciąż 1");
    }

    [Fact]
    public async Task WithoutPairPackage_EachUsesOwn()
    {
        var (f, svc) = Setup(db =>
        {
            db.SessionPackages.Add(new SessionPackage { Id = 5, ClientId = 1, SessionTypeId = 1, Name = "A", TotalSessions = 5, Status = PackageStatus.Active });
            db.SessionPackages.Add(new SessionPackage { Id = 6, ClientId = 2, SessionTypeId = 1, Name = "B", TotalSessions = 5, Status = PackageStatus.Active });
        });
        await svc.CreatePairSessionAsync(Dto(), 2);
        await using var db = f.CreateDbContext();
        (await db.SessionPackages.FindAsync(5))!.UsedSessions.Should().Be(1);
        (await db.SessionPackages.FindAsync(6))!.UsedSessions.Should().Be(1);
    }

    [Fact]
    public async Task SoloBooking_DoesNotUsePairPackage()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        var s = await svc.CreateSessionAsync(Dto());
        s.Status.Should().Be(SessionStatus.AwaitingPackage);
    }

    [Fact]
    public async Task Reschedule_MovesBoth()
    {
        var (f, svc) = Setup(db => db.SessionPackages.Add(PairPackage()));
        var lead = await svc.CreatePairSessionAsync(Dto(), 2);
        var newTime = lead.StartTime.AddHours(3);
        await svc.RescheduleAsync(lead.Id, newTime);
        await using var db = f.CreateDbContext();
        (await db.Sessions.Select(x => x.StartTime).Distinct().ToListAsync()).Should().Equal(newTime);
    }

    [Fact]
    public async Task PairPackageFillsWaitingJointTraining()
    {
        var (f, svc) = Setup(_ => { });
        await svc.CreatePairSessionAsync(Dto(), 2);   // obie wizyty czekają na pakiet
        await using var db = f.CreateDbContext();
        var pkg = PairPackage();
        db.SessionPackages.Add(pkg);
        await db.SaveChangesAsync();
        await PackageAllocation.FillAwaitingAsync(db, pkg);
        await db.SaveChangesAsync();
        pkg.UsedSessions.Should().Be(1);
        (await db.Sessions.CountAsync(s => s.Status == SessionStatus.Scheduled)).Should().Be(2);
    }
}
