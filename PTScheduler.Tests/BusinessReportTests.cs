using FluentAssertions;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Raporty biznesowe: godziny pracy, retencja, przychód na klienta, prognoza i odwołania.</summary>
public class BusinessReportTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0); // poniedziałek

    [Fact]
    public void AvailableMinutes_Merges_Overlaps_And_Respects_Dates()
    {
        var monday = new DateOnly(2026, 9, 28);
        var rules = new List<TrainerAvailability>
        {
            new() { DayOfWeek = DayOfWeek.Monday, StartTime = new(8, 0), EndTime = new(12, 0) },
            new() { DayOfWeek = DayOfWeek.Monday, StartTime = new(11, 0), EndTime = new(14, 0) }, // nakłada się
            new() { DayOfWeek = DayOfWeek.Monday, StartTime = new(16, 0), EndTime = new(18, 0), ValidFrom = monday.AddDays(1) }, // jeszcze nie obowiązuje
            new() { SpecificDate = monday, StartTime = new(18, 0), EndTime = new(19, 0) },
            new() { DayOfWeek = DayOfWeek.Monday, StartTime = new(20, 0), EndTime = new(21, 0), IsActive = false }
        };
        BusinessReportRules.AvailableMinutes(rules, monday).Should().Be(6 * 60 + 60);
        BusinessReportRules.AvailableMinutes(rules, monday.AddDays(1)).Should().Be(0);
    }

    [Fact]
    public void Retention_And_Repurchase()
    {
        BusinessReportRules.Retention([1, 2, 3, 4], new HashSet<int> { 1, 2, 9 }).Should().Be(50);
        BusinessReportRules.Retention([], new HashSet<int> { 1 }).Should().BeNull();

        var t = new DateTime(2026, 1, 1);
        BusinessReportRules.RepurchaseRate([
            (1, t, true), (1, t.AddMonths(2), false), // klient 1 kupił kolejny
            (2, t, true)                              // klient 2 nie
        ]).Should().Be(50);
    }

    [Fact]
    public async Task Report_Counts_Retention_Revenue_Occupancy_Forecast_And_Cancellations()
    {
        var (f, _) = TestDb.CreateFresh();
        var clock = TestClock.AtWallClock(Now);
        await using (var db = f.CreateDbContext())
        {
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60, SinglePrice = 150 });
            db.Clients.AddRange(
                new Client { Id = 1, FirstName = "Anna", TrainerUserId = "t1" },
                new Client { Id = 2, FirstName = "Ola", TrainerUserId = "t1" },
                new Client { Id = 3, FirstName = "Ewa", TrainerUserId = "t1" });
            // Poprzednie 30 dni: Anna i Ola. Bieżące 30 dni: Anna (Ola przestała przychodzić) + nowa Ewa.
            void Visit(int client, DateTime start, SessionStatus status = SessionStatus.Completed) =>
                db.Sessions.Add(new Session { ClientId = client, SessionTypeId = 1, TrainerUserId = "t1", StartTime = start, Status = status });
            Visit(1, Now.AddDays(-45)); Visit(2, Now.AddDays(-40));
            Visit(1, Now.AddDays(-7).Date.AddHours(18)); Visit(3, Now.AddDays(-5));
            Visit(1, Now.AddDays(-14).Date.AddHours(18), SessionStatus.Cancelled);
            Visit(3, Now.AddDays(-21).Date.AddHours(18), SessionStatus.NoShow);
            Visit(3, Now.AddDays(2).Date.AddHours(9), SessionStatus.Scheduled);

            db.TrainerAvailabilities.Add(new TrainerAvailability { TrainerUserId = "t1", DayOfWeek = DayOfWeek.Monday, StartTime = new(8, 0), EndTime = new(20, 0) });

            var nowUtc = clock.UtcNow;
            db.SessionPackages.AddRange(
                new SessionPackage { ClientId = 1, Name = "10 treningów", SessionTypeId = 1, TotalSessions = 10, UsedSessions = 9, PricePerSession = 100, IsPaid = true, PaidAt = nowUtc.AddDays(-10), PurchasedAt = nowUtc.AddDays(-10) },
                new SessionPackage { ClientId = 3, Name = "5 treningów", SessionTypeId = 1, TotalSessions = 5, UsedSessions = 1, PricePerSession = 120, IsPaid = true, PaidAt = nowUtc.AddDays(-5), PurchasedAt = nowUtc.AddDays(-5) },
                new SessionPackage { ClientId = 2, Name = "Stary", SessionTypeId = 1, TotalSessions = 4, UsedSessions = 4, PricePerSession = 100, IsPaid = true, PaidAt = nowUtc.AddDays(-50), PurchasedAt = nowUtc.AddDays(-50), Status = PackageStatus.Depleted },
                new SessionPackage { ClientId = 1, Name = "Stary Anny", SessionTypeId = 1, TotalSessions = 4, UsedSessions = 4, PricePerSession = 100, IsPaid = true, PaidAt = nowUtc.AddDays(-80), PurchasedAt = nowUtc.AddDays(-80), Status = PackageStatus.Depleted });
            db.MembershipPlans.Add(new MembershipPlan { Id = 1, Name = "Karnet 8", SessionTypeId = 1, SessionsPerPeriod = 8, Price = 640 });
            db.Memberships.Add(new Membership { ClientId = 2, PlanId = 1, Status = MembershipStatus.Active, NextBillingDate = clock.Today.AddDays(10) });
            await db.SaveChangesAsync();
        }

        var report = await new BusinessReportService(f, clock).GetAsync(30, "t1");

        report.ActiveClients.Should().Be(2);
        report.NewClients.Should().Be(1, "Ewa trenuje pierwszy raz");
        report.RetentionPercent.Should().Be(50, "z Anny i Oli została Anna");
        report.LostClients.Select(c => c.Name).Should().Equal("Ola");

        report.Revenue.Should().Be(1000 + 600);
        report.PayingClients.Should().Be(2);
        report.AvgRevenuePerClient.Should().Be(800);

        report.PastOccupancy.AvailableMinutes.Should().Be(4 * 12 * 60, "4 poniedziałki po 12 godzin");
        report.PastOccupancy.BookedMinutes.Should().Be(3 * 60, "odwołana wizyta nie zajmuje grafiku, nieobecność tak");

        report.Forecast.MembershipAmount.Should().Be(640);
        report.Forecast.EndingPackages.Should().Be(1, "Annie został 1 trening");
        report.Forecast.RepurchaseRatePercent.Should().Be(50, "Anna dokupiła, Ola nie");
        report.Forecast.ExpectedPackageAmount.Should().Be(500);
        report.Forecast.PrepaidSessions.Should().Be(1 + 4);

        report.CancelledCount.Should().Be(1);
        report.NoShowCount.Should().Be(1);
        report.TopCancelledSlots.Should().ContainSingle(s => s.Day == "Pon" && s.Hour == 18 && s.Cancelled == 2 && s.Total == 3);
    }
}
