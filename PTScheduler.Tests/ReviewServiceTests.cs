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

public class ReviewServiceTests
{
    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync(int completedSessions, bool enabled = true)
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "u1", FirstName = "Marek", LastName = "nowak", TrainerUserId = "t1" });
            for (var i = 0; i < completedSessions; i++)
                db.Sessions.Add(new Session { ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", Status = SessionStatus.Completed, StartTime = new DateTime(2026, 9, 1 + i, 10, 0, 0) });
            db.Sessions.Add(new Session { ClientId = 1, SessionTypeId = 1, TrainerUserId = "t1", Status = SessionStatus.Cancelled, StartTime = new DateTime(2026, 9, 25, 10, 0, 0) });
            await db.SaveChangesAsync();
        }
        await new MarketingSettingsService(f).SaveAsync(new MarketingSettingsDto
        {
            ReviewsEnabled = enabled, ReviewAskAfterSessions = 3, GoogleReviewUrl = "https://g.page/r/abc/review"
        });
        return f;
    }

    private static ReviewService Make(IDbContextFactory<ApplicationDbContext> f, Mock<IWebPushService>? push = null) =>
        new(f, new MarketingSettingsService(f), (push ?? new Mock<IWebPushService>()).Object,
            TestClock.AtWallClock(new DateTime(2026, 10, 1, 12, 0, 0)), NullLogger<ReviewService>.Instance);

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public async Task Asks_Only_After_Configured_Number_Of_Completed_Sessions(int completed, bool expected)
    {
        var svc = Make(await SeedAsync(completed));
        var prompt = await svc.GetPromptAsync(1);
        prompt.ShouldAsk.Should().Be(expected);
        prompt.GoogleReviewUrl.Should().Be("https://g.page/r/abc/review");
    }

    [Fact]
    public async Task Disabled_Never_Asks_And_Publishes_Nothing()
    {
        var f = await SeedAsync(10, enabled: false);
        var svc = Make(f);
        (await svc.GetPromptAsync(1)).ShouldAsk.Should().BeFalse();
        await svc.SubmitAsync(1, 5, "Super", true);
        await svc.SetPublishedAsync((await svc.GetAllAsync()).Single().Id, true);
        (await svc.GetPublicAsync()).Count.Should().Be(0);
    }

    [Fact]
    public async Task Snooze_Hides_Prompt()
    {
        var svc = Make(await SeedAsync(5));
        await svc.SnoozeAsync(1);
        (await svc.GetPromptAsync(1)).ShouldAsk.Should().BeFalse();
    }

    [Fact]
    public async Task Publishing_Requires_Consent_And_Trainer_Approval()
    {
        var f = await SeedAsync(5);
        var push = new Mock<IWebPushService>();
        var svc = Make(f, push);

        (await svc.SubmitAsync(1, 5, "  Świetny trener!  ", publishConsent: false)).Ok.Should().BeTrue();
        push.Verify(p => p.SendAsync("t1", It.IsAny<PushMessageDto>()), Times.Once);
        var review = (await svc.GetAllAsync("t1")).Single();
        review.DisplayName.Should().Be("Marek N.");
        review.Text.Should().Be("Świetny trener!");
        (await svc.SetPublishedAsync(review.Id, true)).Should().BeFalse("brak zgody klienta");

        await svc.SubmitAsync(1, 5, "Świetny trener!", publishConsent: true);
        (await svc.GetPublicAsync()).Count.Should().Be(0, "czeka na akceptację trenera");
        (await svc.SetPublishedAsync(review.Id, true)).Should().BeTrue();

        var pub = await svc.GetPublicAsync();
        pub.Count.Should().Be(1);
        pub.Average.Should().Be(5);
        pub.Items.Single().DisplayName.Should().Be("Marek N.");
        push.Verify(p => p.SendAsync("t1", It.IsAny<PushMessageDto>()), Times.Once, "push tylko przy nowej opinii");
    }

    [Fact]
    public async Task Editing_Text_Unpublishes_Until_Approved_Again()
    {
        var f = await SeedAsync(5);
        var svc = Make(f);
        await svc.SubmitAsync(1, 5, "Dobrze", true);
        var id = (await svc.GetAllAsync()).Single().Id;
        await svc.SetPublishedAsync(id, true);

        await svc.SubmitAsync(1, 2, "Jednak słabo", true);

        (await svc.GetAllAsync()).Single().IsPublished.Should().BeFalse();
        (await svc.GetPromptAsync(1)).Existing!.Rating.Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Rejects_Rating_Out_Of_Range(int rating)
    {
        var svc = Make(await SeedAsync(5));
        (await svc.SubmitAsync(1, rating, null, false)).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Settings_Keep_Only_Https_Google_Link()
    {
        var f = await SeedAsync(1);
        var settings = new MarketingSettingsService(f);
        var s = await settings.GetAsync();
        s.GoogleReviewUrl = "javascript:alert(1)";
        await settings.SaveAsync(s);
        (await settings.GetAsync()).GoogleReviewUrl.Should().BeNull();
    }
}
