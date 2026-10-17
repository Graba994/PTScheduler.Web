using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Marketing;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class ReferralServiceTests
{
    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync(Action<MarketingSettingsDto>? configure = null)
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
            db.Clients.AddRange(
                new Client { Id = 1, ApplicationUserId = "u1", FirstName = "Anna", LastName = "Polecająca", TrainerUserId = "t1" },
                new Client { Id = 2, ApplicationUserId = "u2", FirstName = "Bartek", LastName = "Nowy", TrainerUserId = "t1" },
                new Client { Id = 3, ApplicationUserId = "u3", FirstName = "Celina", LastName = "Nowa", TrainerUserId = "t1" });
            await db.SaveChangesAsync();
        }
        var dto = new MarketingSettingsDto { ReferralEnabled = true, ReferrerRewardKind = ReferralRewardKind.FreeSessions, ReferrerRewardValue = 1 };
        configure?.Invoke(dto);
        await new MarketingSettingsService(f).SaveAsync(dto);
        return f;
    }

    private static ReferralService Make(IDbContextFactory<ApplicationDbContext> f, Mock<IWebPushService>? push = null) =>
        new(f, new MarketingSettingsService(f), (push ?? new Mock<IWebPushService>()).Object,
            TestClock.AtWallClock(new DateTime(2026, 10, 1, 12, 0, 0)), NullLogger<ReferralService>.Instance);

    private static async Task CompleteSessionAsync(IDbContextFactory<ApplicationDbContext> f, int clientId, SessionStatus status = SessionStatus.Completed)
    {
        await using var db = f.CreateDbContext();
        db.Sessions.Add(new Session
        {
            ClientId = clientId, SessionTypeId = 1, TrainerUserId = "t1", Status = status,
            StartTime = new DateTime(2026, 9, 30, 10, 0, 0)
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Code_Is_Stable_And_Readable()
    {
        var svc = Make(await SeedAsync());
        var code = await svc.GetOrCreateCodeAsync(1);

        code.Should().HaveLength(8).And.MatchRegex("^[A-HJ-NP-Z2-9]+$");
        (await svc.GetOrCreateCodeAsync(1)).Should().Be(code);
        (await svc.IsValidCodeAsync(code.ToLowerInvariant())).Should().BeTrue();
        (await svc.IsValidCodeAsync("NIEMA1234")).Should().BeFalse();
    }

    [Fact]
    public async Task Record_Ignores_Self_Duplicates_Unknown_And_Disabled()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        var code = await svc.GetOrCreateCodeAsync(1);

        (await svc.RecordAsync(1, code)).Should().BeFalse("nie można polecić samego siebie");
        (await svc.RecordAsync(2, "XXXXXXXX")).Should().BeFalse();
        (await svc.RecordAsync(2, code)).Should().BeTrue();
        (await svc.RecordAsync(2, code)).Should().BeFalse("klient może być polecony tylko raz");

        var off = await SeedAsync(s => s.ReferralEnabled = false);
        var offSvc = Make(off);
        (await offSvc.RecordAsync(2, await offSvc.GetOrCreateCodeAsync(1))).Should().BeFalse();
    }

    [Fact]
    public async Task Friend_Gets_Single_Use_Welcome_Coupon()
    {
        var f = await SeedAsync(s => s.FriendDiscountPercent = 15);
        var svc = Make(f);
        await svc.RecordAsync(2, await svc.GetOrCreateCodeAsync(1));

        var mine = await svc.GetMineAsync(2);
        mine.WelcomeCouponCode.Should().StartWith("WITAJ-");
        mine.WelcomeDiscountPercent.Should().Be(15);
        await using var db = f.CreateDbContext();
        var coupon = await db.Coupons.SingleAsync();
        (coupon.DiscountType, coupon.DiscountValue, coupon.MaxUses).Should().Be(("percent", 15m, 1));
    }

    [Fact]
    public async Task Reward_Waits_For_First_Completed_Session_Then_Grants_Free_Package()
    {
        var f = await SeedAsync(s => s.ReferrerRewardValue = 2);
        var push = new Mock<IWebPushService>();
        var svc = Make(f, push);
        await svc.RecordAsync(2, await svc.GetOrCreateCodeAsync(1));

        await CompleteSessionAsync(f, 2, SessionStatus.Scheduled);
        (await svc.ProcessPendingAsync()).Should().Be(0, "zaplanowana wizyta to jeszcze nie odbyty trening");

        await CompleteSessionAsync(f, 2);
        (await svc.ProcessPendingAsync()).Should().Be(1);
        (await svc.ProcessPendingAsync()).Should().Be(0, "nagroda przyznawana jest tylko raz");

        await using var db = f.CreateDbContext();
        var pkg = await db.SessionPackages.SingleAsync();
        (pkg.ClientId, pkg.TotalSessions, pkg.IsPaid, pkg.PricePerSession).Should().Be((1, 2, true, 0m));
        var r = await db.Referrals.SingleAsync();
        r.Status.Should().Be(ReferralStatus.Rewarded);
        r.RewardPackageId.Should().Be(pkg.Id);
        push.Verify(p => p.SendAsync("u1", It.IsAny<PushMessageDto>()), Times.Once);
    }

    [Fact]
    public async Task Coupon_Reward_And_Limit_Per_Client()
    {
        var f = await SeedAsync(s =>
        {
            s.ReferrerRewardKind = ReferralRewardKind.DiscountPercent;
            s.ReferrerRewardValue = 20;
            s.MaxRewardsPerClient = 1;
        });
        var svc = Make(f);
        var code = await svc.GetOrCreateCodeAsync(1);
        await svc.RecordAsync(2, code);
        await svc.RecordAsync(3, code);
        await CompleteSessionAsync(f, 2);
        await CompleteSessionAsync(f, 3);

        (await svc.ProcessPendingAsync()).Should().Be(1);

        var mine = (await svc.GetMineAsync(1)).Referrals;
        mine.Should().ContainSingle(r => r.Status == ReferralStatus.Rewarded)
            .Which.RewardCouponCode.Should().StartWith("POLEC-");
        mine.Should().ContainSingle(r => r.Status == ReferralStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelled_Referral_Gets_No_Reward()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        await svc.RecordAsync(2, await svc.GetOrCreateCodeAsync(1));
        var id = (await svc.GetAllAsync()).Single().Id;

        (await svc.CancelAsync(id)).Should().BeTrue();
        await CompleteSessionAsync(f, 2);
        (await svc.ProcessPendingAsync()).Should().Be(0);
    }
}
