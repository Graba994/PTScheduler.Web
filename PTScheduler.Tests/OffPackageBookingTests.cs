using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Rezerwacja treningu spoza pakietu: płatność online albo u trenera (od razu / do akceptacji).</summary>
public class OffPackageBookingTests
{
    // ── Reguły ──

    [Theory]
    [InlineData(false, false, false, 1, 0, AtTrainerMode.Disabled)]   // trener nie przyjmuje płatności u siebie
    [InlineData(true, true, false, 1, 5, AtTrainerMode.Instant)]      // zaufany klient — zawsze od razu
    [InlineData(true, false, true, 1, 0, AtTrainerMode.NeedsApproval)] // zawsze do akceptacji
    [InlineData(true, false, false, 1, 0, AtTrainerMode.Instant)]     // bez akceptacji, pod limitem
    [InlineData(true, false, false, 1, 1, AtTrainerMode.NeedsApproval)] // limit osiągnięty
    [InlineData(true, false, false, 0, 9, AtTrainerMode.Instant)]     // 0 = bez limitu
    public void AtTrainer_Decision(bool atTrainer, bool trusted, bool needsApproval, int limit, int unpaid, AtTrainerMode expected) =>
        OffPackageRules.AtTrainer(new OffPackagePolicy(true, atTrainer, needsApproval, limit), trusted, unpaid).Should().Be(expected);

    [Theory]
    [InlineData(true, true, 120, true)]
    [InlineData(false, true, 120, false)]  // trener wyłączył
    [InlineData(true, false, 120, false)]  // płatności online nie działają
    [InlineData(true, true, null, false)]  // brak ceny pojedynczego treningu
    public void CanPayOnline_Decision(bool online, bool payments, int? price, bool expected) =>
        OffPackageRules.CanPayOnline(new OffPackagePolicy(online, true, true, 1), payments, price).Should().Be(expected);

    // ── Serwis ──

    private sealed record Ctx(IDbContextFactory<ApplicationDbContext> F, OffPackageService Svc, SessionService Sessions);

    private static Ctx Setup(bool needsApproval = true, int limit = 1, bool trusted = false, bool requiresPackage = false)
    {
        var (factory, db) = TestDb.CreateFresh();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening personalny", DurationMinutes = 60, SinglePrice = 150, RequiresPackage = requiresPackage });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "ola", FirstName = "Ola", TrainerUserId = "t1", TrustedForDeferredPayment = trusted });
        db.SaveChanges();

        var sessions = new SessionService(factory,
            new Mock<IEmailService>().Object, new Mock<IEmailTemplateService>().Object,
            new Mock<INotificationPreferencesService>().Object, new Mock<IGoogleMeetService>().Object,
            new TrainerAvailabilityService(factory), TestClock.AtWallClock(DateTime.Now),
            NullLogger<SessionService>.Instance);
        var cfg = new Mock<ITrainerConfigService>();
        cfg.Setup(c => c.GetAsync("t1")).ReturnsAsync(new TrainerConfigDto
        {
            OffPackageOnline = true, OffPackageAtTrainer = true, OffPackageNeedsApproval = needsApproval, OffPackageUnpaidLimit = limit
        });
        var payments = new Mock<IPaymentService>();
        payments.Setup(p => p.GetEnabledOptionsAsync()).ReturnsAsync([]);
        var svc = new OffPackageService(factory, sessions, payments.Object, cfg.Object,
            new Mock<IWebPushService>().Object, new Mock<IEmailService>().Object, new Mock<IAuditLogService>().Object,
            NullLogger<OffPackageService>.Instance);
        return new(factory, svc, sessions);
    }

    private static CreateSessionDto Dto(int days = 2) => new()
    {
        ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = DateTime.Now.Date.AddDays(days).AddHours(10)
    };

    [Fact]
    public async Task PayAtTrainer_WithApproval_CreatesRequest_ThenApproveConfirms()
    {
        var c = Setup(needsApproval: true);
        var s = await c.Svc.BookAtTrainerAsync("ola", Dto());

        s.Status.Should().Be(SessionStatus.AwaitingPackage);
        s.AwaitingApproval.Should().BeTrue();
        (await c.Svc.GetCountsAsync("t1")).Requests.Should().Be(1);

        await c.Svc.ApproveAsync(s.Id, "t1");
        var after = (await c.Sessions.GetSessionAsync(s.Id))!;
        after.Status.Should().Be(SessionStatus.Scheduled);
        after.UnpaidAtTrainer.Should().BeTrue();
        (await c.Svc.GetCountsAsync("t1")).Should().Be(new OffPackageCountsDto(0, 1));
    }

    [Fact]
    public async Task Reject_CancelsAndFreesSlot()
    {
        var c = Setup(needsApproval: true);
        var s = await c.Svc.BookAtTrainerAsync("ola", Dto());
        await c.Svc.RejectAsync(s.Id, "t1", "Ten termin mam zajęty");

        var after = (await c.Sessions.GetSessionAsync(s.Id))!;
        after.Status.Should().Be(SessionStatus.Cancelled);
        after.CancellationReason.Should().Be("Ten termin mam zajęty");
        // Termin znów wolny: można zarezerwować tę samą godzinę.
        var again = await c.Svc.BookAtTrainerAsync("ola", Dto());
        again.Id.Should().NotBe(s.Id);
    }

    [Fact]
    public async Task WithoutApproval_InstantUntilLimit_ThenRequest()
    {
        var c = Setup(needsApproval: false, limit: 1);
        var first = await c.Svc.BookAtTrainerAsync("ola", Dto(2));
        first.Status.Should().Be(SessionStatus.Scheduled);
        first.UnpaidAtTrainer.Should().BeTrue();

        var second = await c.Svc.BookAtTrainerAsync("ola", Dto(3));
        second.AwaitingApproval.Should().BeTrue("klient ma już 1 nieopłacony trening — limit");
    }

    [Fact]
    public async Task TrustedClient_AlwaysInstant()
    {
        var c = Setup(needsApproval: true, trusted: true);
        var s = await c.Svc.BookAtTrainerAsync("ola", Dto());
        s.Status.Should().Be(SessionStatus.Scheduled);
        s.AwaitingApproval.Should().BeFalse();
    }

    [Fact]
    public async Task PackageOnly_Type_CannotBeBookedWithoutPackage()
    {
        var c = Setup(requiresPackage: true);
        var act = () => c.Svc.BookAtTrainerAsync("ola", Dto());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CannotBookForSomeoneElse()
    {
        var c = Setup();
        var act = () => c.Svc.BookAtTrainerAsync("ktos-inny", Dto());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task MarkPaid_ClearsUnpaid_AndConfirmsLegacyNoPackageVisit()
    {
        var c = Setup(needsApproval: false, limit: 0);
        var s = await c.Svc.BookAtTrainerAsync("ola", Dto());
        await c.Svc.MarkPaidAsync(s.Id, "t1", "gotówka");
        var paid = (await c.Sessions.GetSessionAsync(s.Id))!;
        paid.PaidVia.Should().Be("gotówka");
        paid.UnpaidAtTrainer.Should().BeFalse();

        // Dawna wizyta „bez pakietu” (sprzed tej funkcji) po opłacie staje się zwykłą wizytą.
        var legacy = await c.Sessions.CreateSessionAsync(Dto(5));
        legacy.Status.Should().Be(SessionStatus.AwaitingPackage);
        await c.Svc.MarkPaidAsync(legacy.Id, "t1", "przelew");
        (await c.Sessions.GetSessionAsync(legacy.Id))!.Status.Should().Be(SessionStatus.Scheduled);
    }

    [Fact]
    public async Task ExpiredOnlineHold_IsCancelled()
    {
        var c = Setup();
        var s = await c.Sessions.CreateSessionAsync(Dto(), sendConfirmation: false);
        await using (var db = c.F.CreateDbContext())
        {
            var row = await db.Sessions.FirstAsync(x => x.Id == s.Id);
            row.OffPackagePayment = OffPackageRules.PayOnline;
            row.HoldUntil = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        (await c.Svc.ExpireHoldsAsync()).Should().Be(1);
        (await c.Sessions.GetSessionAsync(s.Id))!.Status.Should().Be(SessionStatus.Cancelled);
    }

    [Fact]
    public async Task NewPackage_CoversPendingRequest()
    {
        var c = Setup(needsApproval: true);
        var s = await c.Svc.BookAtTrainerAsync("ola", Dto());
        await using (var db = c.F.CreateDbContext())
        {
            var pkg = new SessionPackage { ClientId = 1, SessionTypeId = 1, Name = "10 treningów", TotalSessions = 10, Status = PackageStatus.Active };
            db.SessionPackages.Add(pkg);
            await db.SaveChangesAsync();
            await PackageAllocation.FillAwaitingAsync(db, pkg);
            await db.SaveChangesAsync();
        }
        var after = (await c.Sessions.GetSessionAsync(s.Id))!;
        after.Status.Should().Be(SessionStatus.Scheduled);
        after.AwaitingApproval.Should().BeFalse("pakiet opłacił trening — prośba nie jest już potrzebna");
        after.OffPackagePayment.Should().BeNull();
    }
}
