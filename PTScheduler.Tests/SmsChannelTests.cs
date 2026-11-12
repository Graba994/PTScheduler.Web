using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Messaging;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Portal.Components.Home;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>SMS-y: push zamiast SMS u klientów z aplikacją i ukrycie SMS-ów, gdy platforma nie ma bramki.</summary>
public class SmsChannelTests
{
    [Theory]
    [InlineData(true, 1, true, false, true)]
    [InlineData(true, 0, true, false, false)]   // brak aplikacji z powiadomieniami — SMS
    [InlineData(true, 2, false, false, false)]  // klient wyłączył ten rodzaj powiadomień — SMS
    [InlineData(true, 1, true, true, false)]    // klient chce SMS-y także przy aplikacji
    [InlineData(false, 1, true, false, false)]  // push nieskonfigurowany w studiu
    public void Push_replaces_sms_only_with_working_app_notifications(bool configured, int subs, bool optIn, bool smsWithApp, bool expected) =>
        SmsPolicy.PushReplacesSms(configured, subs, optIn, smsWithApp).Should().Be(expected);

    [Fact]
    public async Task Client_preference_decides_whether_sms_goes_with_app()
    {
        var (f, _) = TestDb.CreateFresh();
        var prefs = new NotificationPreferencesService(f);
        var push = new Mock<IWebPushService>();
        push.Setup(p => p.GetSubscriptionCountAsync("u1")).ReturnsAsync(1);

        (await SmsPolicy.PushReplacesSmsAsync(push.Object, prefs, true, "u1", NotificationTypes.PushReminders)).Should().BeTrue("domyślnie push zastępuje SMS");
        (await SmsPolicy.PushReplacesSmsAsync(push.Object, prefs, true, "u2", NotificationTypes.PushReminders)).Should().BeFalse("bez subskrypcji — SMS");

        var dto = await prefs.GetAsync("u1");
        dto.SmsWithApp = true;
        await prefs.SaveAsync("u1", dto);
        (await SmsPolicy.PushReplacesSmsAsync(push.Object, prefs, true, "u1", NotificationTypes.PushReminders)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 1, 0)]
    [InlineData(true, 0, 1)]   // klient chce SMS-y — reguła tylko z SMS-em nie dokłada pusha
    public async Task Automation_sends_push_instead_of_sms_to_client_with_app(bool smsWithApp, int pushes, int smses)
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0);
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Users.AddRange(
                new ApplicationUser { Id = "u1", Email = "ola@example.com", FirstName = "Ola" },
                new ApplicationUser { Id = "t1", Email = "trener@example.com", FirstName = "Jan" });
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "u1", FirstName = "Ola", Phone = "600100200", TrainerUserId = "t1",
                CreatedAt = now.AddDays(-200), DateOfBirth = new DateOnly(1995, 9, 28) });
            db.Sessions.Add(new Session { ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", StartTime = now.AddDays(-2), Status = SessionStatus.Completed, CreatedAt = now.AddDays(-5) });
            db.NotificationPreferences.Add(new NotificationPreferences { UserId = "u1", SmsWithApp = smsWithApp });
            await db.SaveChangesAsync();
        }
        var push = new Mock<IWebPushService>();
        push.Setup(p => p.GetSubscriptionCountAsync("u1")).ReturnsAsync(1);
        push.Setup(p => p.SendWithReportAsync("u1", It.IsAny<PushMessageDto>())).ReturnsAsync(new PushSendReport(1, 0, null));
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.SendReminderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new SmsResult(true, false, null));
        var branding = new Mock<IBrandingService>();
        branding.Setup(b => b.GetAsync()).ReturnsAsync(new AppBrandingDto { CompanyName = "Studio" });
        var svc = new AutomationService(f, TestClock.AtWallClock(now), new Mock<IEmailService>().Object, new Mock<IEmailTemplateService>().Object,
            push.Object, sms.Object, new NotificationPreferencesService(f), branding.Object, NullLogger<AutomationService>.Instance);

        var rule = (await svc.GetRulesAsync()).Single(r => r.Kind == AutomationKinds.Birthday);
        rule.Enabled = true;
        rule.ViaEmail = false;
        rule.ViaPush = false;
        rule.ViaSms = true;
        await svc.SaveRuleAsync(rule);

        (await svc.RunAsync(new AutomationChannels(false, true, true, 100, "https://studio.example"))).Should().Be(1);
        push.Verify(p => p.SendWithReportAsync("u1", It.IsAny<PushMessageDto>()), Times.Exactly(pushes));
        sms.Verify(s => s.SendReminderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(smses));
    }

    [Fact]
    public void Without_sms_gateway_studios_and_homepage_show_no_sms()
    {
        var pro = new Plan { Id = "pro", Name = "Pro", SmsReminders = true, MaxSmsPerMonth = 150, PushNotifications = true, MaxClients = 75 };
        var none = ExtraLimits.None;

        var off = JsonDocument.Parse(TenantService.SerializeEntitlements(pro, none, smsOn: false)).RootElement;
        off.GetProperty("smsAvailable").GetBoolean().Should().BeFalse();
        off.GetProperty("smsReminders").GetBoolean().Should().BeFalse();
        off.GetProperty("maxSmsPerMonth").GetInt32().Should().Be(0);
        var ent = JsonSerializer.Deserialize<PTScheduler.Web.Services.Entitlements>(off.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        ent.SmsAvailable.Should().BeFalse();

        var on = JsonDocument.Parse(TenantService.SerializeEntitlements(pro, none, smsOn: true)).RootElement;
        on.GetProperty("smsAvailable").GetBoolean().Should().BeTrue();
        on.GetProperty("maxSmsPerMonth").GetInt32().Should().Be(150);
        PTScheduler.Web.Services.Entitlements.Unlimited().SmsAvailable.Should().BeTrue("instancja samodzielna z własnym kontem SMSAPI");

        HomeShared.PlanFeatures(pro, sms: false).Select(x => x.Label).Should().Contain("Powiadomienia na telefonie").And.NotContain(l => l.Contains("SMS"));
        HomeShared.PlanFeatures(pro, sms: true).Select(x => x.Label).Should().Contain("Przypomnienia SMS (150/mies.)");
    }
}
