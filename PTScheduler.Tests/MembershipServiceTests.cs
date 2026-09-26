using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Memberships;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class MembershipServiceTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync(bool carryOver = false)
    {
        var (f, _) = TestDb.CreateFresh();
        await using var db = f.CreateDbContext();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K", TrainerUserId = "t1" });
        db.MembershipPlans.Add(new MembershipPlan
        {
            Id = 1, Name = "8 treningów", SessionTypeId = 1, SessionsPerPeriod = 8, PeriodMonths = 1,
            Price = 800m, CarryOverUnused = carryOver, CreatedByUserId = "t1"
        });
        await db.SaveChangesAsync();
        return f;
    }

    private static MembershipService Make(IDbContextFactory<ApplicationDbContext> f, DateOnly today) =>
        new(f, new Mock<IWebPushService>().Object, TestClock.AtWallClock(today.ToDateTime(new TimeOnly(8, 0))),
            NullLogger<MembershipService>.Instance);

    [Fact]
    public async Task Subscribe_Creates_First_Period_With_Package_And_Due_Payment()
    {
        var f = await SeedAsync();
        var svc = Make(f, Start);

        var (ok, _) = await svc.SubscribeAsync(1, 1, Start, null);

        ok.Should().BeTrue();
        var m = (await svc.GetClientMembershipsAsync(1)).Single();
        m.Status.Should().Be(MembershipStatus.Active);
        m.NextBillingDate.Should().Be(new DateOnly(2026, 11, 1));
        var p = m.Periods.Single();
        p.PeriodEnd.Should().Be(new DateOnly(2026, 10, 31));
        p.Status.Should().Be(MembershipPeriodStatus.Due);
        p.SessionsTotal.Should().Be(8);
        m.UnpaidAmount.Should().Be(800m);
    }

    [Fact]
    public async Task Billing_Creates_Next_Period_And_Marks_Overdue()
    {
        var f = await SeedAsync();
        await Make(f, Start).SubscribeAsync(1, 1, Start, null);

        // 2 listopada: nowy okres + pierwszy okres po terminie (1.10 + 7 dni).
        var changes = await Make(f, new DateOnly(2026, 11, 2)).ProcessBillingAsync();

        changes.Should().BeGreaterThan(0);
        var m = (await Make(f, new DateOnly(2026, 11, 2)).GetClientMembershipsAsync(1)).Single();
        m.Periods.Should().HaveCount(2);
        m.Status.Should().Be(MembershipStatus.PastDue);
        m.NextBillingDate.Should().Be(new DateOnly(2026, 12, 1));

        // Drugi przebieg tego samego dnia niczego nie dubluje.
        await Make(f, new DateOnly(2026, 11, 2)).ProcessBillingAsync();
        (await Make(f, new DateOnly(2026, 11, 2)).GetClientMembershipsAsync(1)).Single().Periods.Should().HaveCount(2);
    }

    [Fact]
    public async Task Paying_All_Periods_Restores_Active_Status_And_Package()
    {
        var f = await SeedAsync();
        var svc = Make(f, new DateOnly(2026, 11, 2));
        await Make(f, Start).SubscribeAsync(1, 1, Start, null);
        await svc.ProcessBillingAsync();

        foreach (var p in (await svc.GetClientMembershipsAsync(1)).Single().Periods)
            await svc.MarkPeriodPaidAsync(p.Id, "gotówka");

        var m = (await svc.GetClientMembershipsAsync(1)).Single();
        m.Status.Should().Be(MembershipStatus.Active);
        m.UnpaidAmount.Should().Be(0);
        await using var db = f.CreateDbContext();
        (await db.SessionPackages.AllAsync(x => x.IsPaid)).Should().BeTrue();
    }

    [Fact]
    public async Task Carry_Over_Moves_Unused_Sessions_To_Next_Period()
    {
        var f = await SeedAsync(carryOver: true);
        await Make(f, Start).SubscribeAsync(1, 1, Start, null);
        await using (var db = f.CreateDbContext())
        {
            (await db.SessionPackages.SingleAsync()).UsedSessions = 5;
            await db.SaveChangesAsync();
        }

        await Make(f, new DateOnly(2026, 11, 1)).ProcessBillingAsync();

        var current = (await Make(f, new DateOnly(2026, 11, 1)).GetClientMembershipsAsync(1)).Single().Periods.First();
        current.SessionsTotal.Should().Be(8 + 3);
    }

    [Fact]
    public async Task Cancel_At_Period_End_Stops_Billing()
    {
        var f = await SeedAsync();
        await Make(f, Start).SubscribeAsync(1, 1, Start, null);
        var id = (await Make(f, Start).GetClientMembershipsAsync(1)).Single().Id;
        await Make(f, Start).CancelAsync(id, immediately: false);

        await Make(f, new DateOnly(2026, 11, 1)).ProcessBillingAsync();

        var m = (await Make(f, new DateOnly(2026, 11, 1)).GetClientMembershipsAsync(1)).Single();
        m.Status.Should().Be(MembershipStatus.Cancelled);
        m.Periods.Should().ContainSingle();
    }

    [Fact]
    public void Period_End_Handles_Month_Lengths()
    {
        MembershipBilling.PeriodEnd(new DateOnly(2026, 1, 31), 1).Should().Be(new DateOnly(2026, 2, 27));
        MembershipBilling.PeriodEnd(new DateOnly(2026, 2, 1), 3).Should().Be(new DateOnly(2026, 4, 30));
    }
}
