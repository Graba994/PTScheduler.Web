using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class ChatServiceTests
{
    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync()
    {
        var (f, _) = TestDb.CreateFresh();
        await using var db = f.CreateDbContext();
        db.Users.AddRange(
            new ApplicationUser { Id = "t1", UserName = "t1", FirstName = "Anna", LastName = "Trener" },
            new ApplicationUser { Id = "t2", UserName = "t2", FirstName = "Piotr", LastName = "Inny" },
            new ApplicationUser { Id = "c1", UserName = "c1", FirstName = "Jan", LastName = "Klient" },
            new ApplicationUser { Id = "c2", UserName = "c2", FirstName = "Ola", LastName = "Klient" });
        db.Clients.AddRange(
            new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "Klient", TrainerUserId = "t1" },
            new Client { Id = 2, ApplicationUserId = "c2", FirstName = "Ola", LastName = "Klient", TrainerUserId = "t2" });
        await db.SaveChangesAsync();
        return f;
    }

    private static ChatService Make(IDbContextFactory<ApplicationDbContext> f, Mock<IWebPushService>? push = null, IChatNotifier? notifier = null) =>
        new(f, (push ?? new Mock<IWebPushService>()).Object, notifier ?? new ChatNotifier(),
            TestClock.AtWallClock(new DateTime(2026, 10, 1, 12, 0, 0)), NullLogger<ChatService>.Instance);

    [Fact]
    public async Task Access_Only_For_Own_Client_Or_Their_Trainer_Or_Admin()
    {
        var svc = Make(await SeedAsync());

        (await svc.CanAccessAsync(1, "c1", false)).Should().BeTrue();
        (await svc.CanAccessAsync(1, "t1", false)).Should().BeTrue();
        (await svc.CanAccessAsync(1, "c2", false)).Should().BeFalse();
        (await svc.CanAccessAsync(1, "t2", false)).Should().BeFalse();
        (await svc.CanAccessAsync(1, "anyone", true)).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_Rejects_Empty(string body)
    {
        var svc = Make(await SeedAsync());
        var (ok, error) = await svc.SendAsync(1, "c1", false, body);
        ok.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public async Task Send_Rejects_Too_Long()
    {
        var svc = Make(await SeedAsync());
        var (ok, _) = await svc.SendAsync(1, "c1", false, new string('a', ChatService.MaxLength + 1));
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task Client_Message_Pushes_To_Trainer_And_Notifies()
    {
        var f = await SeedAsync();
        var push = new Mock<IWebPushService>();
        var notifier = new ChatNotifier();
        var notified = new List<int>();
        notifier.ConversationChanged += notified.Add;
        var svc = Make(f, push, notifier);

        var (ok, _) = await svc.SendAsync(1, "c1", false, "  Dzień dobry!  ");

        ok.Should().BeTrue();
        notified.Should().Contain(1);
        push.Verify(p => p.SendAsync("t1", It.Is<PushMessageDto>(m => m.Url == "/chat?client=1")), Times.Once);
        var msg = (await svc.GetMessagesAsync(1)).Single();
        msg.Body.Should().Be("Dzień dobry!");
        msg.SenderName.Should().Be("Jan Klient");
    }

    [Fact]
    public async Task Unread_Counts_Per_Side_And_MarkRead_Clears_Them()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        await svc.SendAsync(1, "c1", false, "Pytanie 1");
        await svc.SendAsync(1, "c1", false, "Pytanie 2");
        await svc.SendAsync(2, "c2", false, "Inny trener");
        await svc.SendAsync(1, "t1", true, "Odpowiedź");

        (await svc.GetUnreadCountAsync("t1", isStaff: true, isAdmin: false)).Should().Be(2);
        (await svc.GetUnreadCountAsync("t2", isStaff: true, isAdmin: false)).Should().Be(1);
        (await svc.GetUnreadCountAsync("admin", isStaff: true, isAdmin: true)).Should().Be(3);
        (await svc.GetUnreadCountAsync("c1", isStaff: false, isAdmin: false)).Should().Be(1);

        await svc.MarkReadAsync(1, readerIsStaff: true);

        (await svc.GetUnreadCountAsync("t1", true, false)).Should().Be(0);
        (await svc.GetUnreadCountAsync("c1", false, false)).Should().Be(1, "trener przeczytał tylko wiadomości klienta");
    }

    [Fact]
    public async Task Conversations_Are_Scoped_To_Trainer_And_Sorted_By_Last_Message()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        await svc.SendAsync(1, "c1", false, "Hej");

        var mine = await svc.GetConversationsAsync("t1");
        mine.Should().ContainSingle(c => c.ClientId == 1);
        mine[0].LastMessage.Should().Be("Hej");
        mine[0].Unread.Should().Be(1);

        var all = await svc.GetConversationsAsync(null);
        all.Select(c => c.ClientId).Should().Equal(1, 2);
    }
}
