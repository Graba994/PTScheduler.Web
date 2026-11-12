using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Automatyczne wiadomości: kto i kiedy je dostaje, kupony i liczenie powrotów.</summary>
public class AutomationTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0);
    private static readonly AutomationChannels EmailOnly = new(true, false, false, 0, "https://studio.example");

    [Fact]
    public void Rules_Birthday_WinBack_And_PackageEnded()
    {
        var today = new DateOnly(2026, 9, 28);
        AutomationRules.IsBirthday(new DateOnly(1990, 9, 28), today).Should().BeTrue();
        AutomationRules.IsBirthday(new DateOnly(1992, 2, 29), new DateOnly(2027, 2, 28)).Should().BeTrue("29 lutego świętujemy 28 lutego");
        AutomationRules.IsBirthday(null, today).Should().BeFalse();

        AutomationRules.WinBackDue(Now.AddDays(-20), false, null, 14, 60, Now).Should().BeTrue();
        AutomationRules.WinBackDue(Now.AddDays(-20), true, null, 14, 60, Now).Should().BeFalse("ma zaplanowaną wizytę");
        AutomationRules.WinBackDue(Now.AddDays(-20), false, Now.AddDays(-10), 14, 60, Now).Should().BeFalse("niedawno dostał wiadomość");
        AutomationRules.WinBackDue(null, false, null, 14, 60, Now).Should().BeFalse("nigdy nie trenował");

        AutomationRules.PackageEndedDue(Now.AddDays(-4), false, null, 3, Now).Should().BeTrue();
        AutomationRules.PackageEndedDue(Now.AddDays(-4), true, null, 3, Now).Should().BeFalse("kupił nowy pakiet");
        AutomationRules.PackageEndedDue(Now.AddDays(-60), false, null, 3, Now).Should().BeFalse("stary koniec — to już „dawno Cię nie było”");

        AutomationRules.WelcomeDue(Now.AddDays(-2), Now.AddDays(-5), 2, Now).Should().BeTrue();
        AutomationRules.WelcomeDue(Now.AddDays(-2), Now.AddDays(-1), 2, Now).Should().BeFalse("klient sprzed włączenia reguły");

        AutomationRules.Fill("Cześć {Imie}! Kod: {Kupon}{Nieznane}", new Dictionary<string, string> { ["Imie"] = "Ola", ["Kupon"] = "X" })
            .Should().Be("Cześć Ola! Kod: X");
    }

    [Fact]
    public async Task Run_Sends_WinBack_With_Coupon_Birthday_And_Counts_Return()
    {
        var (f, _) = TestDb.CreateFresh();
        var clock = TestClock.AtWallClock(Now);
        await using (var db = f.CreateDbContext())
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "u1", Email = "anna@example.com", FirstName = "Anna" },
                new ApplicationUser { Id = "u2", Email = "ola@example.com", FirstName = "Ola" },
                new ApplicationUser { Id = "t1", Email = "trener@example.com", FirstName = "Jan", LastName = "Trener" });
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "u1", FirstName = "Anna", TrainerUserId = "t1", CreatedAt = Now.AddDays(-200) });
            db.Clients.Add(new Client { Id = 2, ApplicationUserId = "u2", FirstName = "Ola", TrainerUserId = "t1", CreatedAt = Now.AddDays(-200),
                DateOfBirth = new DateOnly(1995, 9, 28) });
            db.Sessions.Add(new Session { ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = Now.AddDays(-20), Status = SessionStatus.Completed, CreatedAt = Now.AddDays(-25) });
            db.Sessions.Add(new Session { ClientId = 2, SessionTypeId = 1, TrainerUserId = "t1", StartTime = Now.AddDays(-2), Status = SessionStatus.Completed, CreatedAt = Now.AddDays(-5) });
            await db.SaveChangesAsync();
        }

        var email = new Mock<IEmailService>();
        var templates = new Mock<IEmailTemplateService>();
        templates.Setup(t => t.RenderAsync("automation", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync((string _, Dictionary<string, string> v) => (v["Subject"], v["Body"]));
        var prefs = new Mock<INotificationPreferencesService>();
        prefs.Setup(p => p.IsEnabledAsync(It.IsAny<string>(), NotificationTypes.TrainerMessages)).ReturnsAsync(true);
        var branding = new Mock<IBrandingService>();
        branding.Setup(b => b.GetAsync()).ReturnsAsync(new AppBrandingDto { CompanyName = "Studio Jan" });
        var svc = new AutomationService(f, clock, email.Object, templates.Object, new Mock<IWebPushService>().Object,
            new Mock<ISmsService>().Object, prefs.Object, branding.Object, NullLogger<AutomationService>.Instance);

        foreach (var kind in new[] { AutomationKinds.WinBack, AutomationKinds.Birthday, AutomationKinds.Welcome1 })
        {
            var rule = (await svc.GetRulesAsync()).Single(r => r.Kind == kind);
            rule.Enabled = true;
            await svc.SaveRuleAsync(rule);
        }

        (await svc.RunAsync(EmailOnly)).Should().Be(2, "Anna — powrót, Ola — urodziny; powitanie nie idzie do starych klientów");
        (await svc.RunAsync(EmailOnly)).Should().Be(0, "każda wiadomość wychodzi raz");

        email.Verify(e => e.SendAsync("anna@example.com", It.IsAny<string>(), It.Is<string>(s => s.Contains("Anna")), It.Is<string>(h => h.Contains("WRACAJ-"))), Times.Once);
        email.Verify(e => e.SendAsync("ola@example.com", It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(h => h.Contains("URODZINY-"))), Times.Once);

        var log = await svc.GetLogAsync();
        var annaCode = log.Single(l => l.ClientId == 1).CouponCode!;
        await using (var db = f.CreateDbContext())
        {
            var coupon = db.Coupons.Single(c => c.Code == annaCode);
            coupon.MaxUses.Should().Be(1);
            coupon.DiscountValue.Should().Be(10);
            // Anna rezerwuje trening po wiadomości — to „powrót”.
            db.Sessions.Add(new Session { ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = Now.AddDays(3), Status = SessionStatus.Scheduled, CreatedAt = clock.UtcNow.AddMinutes(30) });
            await db.SaveChangesAsync();
        }

        await svc.RunAsync(EmailOnly);
        (await svc.GetLogAsync()).Single(l => l.ClientId == 1).ReturnedAtUtc.Should().NotBeNull();
        (await svc.GetStatsAsync()).Single(s => s.Kind == AutomationKinds.WinBack).Returned.Should().Be(1);
    }

    [Fact]
    public async Task Client_Who_Turned_Off_Trainer_Messages_Gets_Nothing()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Users.Add(new ApplicationUser { Id = "u2", Email = "ola@example.com" });
            db.Clients.Add(new Client { Id = 2, ApplicationUserId = "u2", FirstName = "Ola", DateOfBirth = new DateOnly(1995, 9, 28) });
            await db.SaveChangesAsync();
        }
        var email = new Mock<IEmailService>();
        var prefs = new Mock<INotificationPreferencesService>();
        prefs.Setup(p => p.IsEnabledAsync("u2", NotificationTypes.TrainerMessages)).ReturnsAsync(false);
        var branding = new Mock<IBrandingService>();
        branding.Setup(b => b.GetAsync()).ReturnsAsync(new AppBrandingDto());
        var svc = new AutomationService(f, TestClock.AtWallClock(Now), email.Object, new Mock<IEmailTemplateService>().Object,
            new Mock<IWebPushService>().Object, new Mock<ISmsService>().Object, prefs.Object, branding.Object, NullLogger<AutomationService>.Instance);
        var rule = (await svc.GetRulesAsync()).Single(r => r.Kind == AutomationKinds.Birthday);
        rule.Enabled = true;
        await svc.SaveRuleAsync(rule);

        (await svc.RunAsync(EmailOnly)).Should().Be(0);
        email.VerifyNoOtherCalls();
    }
}
