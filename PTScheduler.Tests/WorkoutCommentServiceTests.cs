using FluentAssertions;
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

/// <summary>
/// Komentarze do treningów: zapis z powiadomieniem drugiej strony, odczyt,
/// oznaczanie przeczytanych i usuwanie tylko przez autora.
/// </summary>
public class WorkoutCommentServiceTests
{
    private static readonly DateOnly Day = new(2026, 6, 1);

    private static async Task<(WorkoutCommentService Svc, Mock<IWebPushService> Push, Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> F)> MakeAsync()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "client-user", FirstName = "Anna", LastName = "Nowak", TrainerUserId = "trainer-1" });
            db.Users.Add(new ApplicationUser { Id = "trainer-1", FirstName = "Tomek", LastName = "Trener" });
            db.Users.Add(new ApplicationUser { Id = "client-user", FirstName = "Anna", LastName = "Nowak" });
            await db.SaveChangesAsync();
        }
        var users = MockUserManagerHelper.Create();
        users.Setup(u => u.FindByIdAsync("trainer-1")).ReturnsAsync(new ApplicationUser { Id = "trainer-1", FirstName = "Tomek", LastName = "Trener" });
        users.Setup(u => u.FindByIdAsync("client-user")).ReturnsAsync(new ApplicationUser { Id = "client-user", FirstName = "Anna", LastName = "Nowak" });
        var push = new Mock<IWebPushService>();
        var svc = new WorkoutCommentService(f, users.Object, push.Object,
            TestClock.AtWallClock(new DateTime(2026, 6, 1, 18, 0, 0)), NullLogger<WorkoutCommentService>.Instance);
        return (svc, push, f);
    }

    [Fact]
    public async Task Trainer_Comment_Notifies_Client_And_Is_Marked_Read_By_Client()
    {
        var (svc, push, _) = await MakeAsync();

        var added = await svc.AddAsync(1, Day, "trainer-1", byTrainer: true, "  Świetnie! +2,5 kg  ");

        added.Text.Should().Be("Świetnie! +2,5 kg");
        added.AuthorName.Should().Be("Tomek Trener");
        push.Verify(p => p.SendAsync("client-user", It.Is<PushMessageDto>(m => m.Url == "/my/workouts")), Times.Once);

        // Trener czytający własny komentarz go nie „odczytuje”; klient — tak.
        await svc.MarkReadAsync(1, readerIsTrainer: true);
        (await svc.GetForClientAsync(1)).Single().IsRead.Should().BeFalse();
        await svc.MarkReadAsync(1, readerIsTrainer: false);
        (await svc.GetForClientAsync(1)).Single().IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task Client_Reply_Notifies_Trainer()
    {
        var (svc, push, _) = await MakeAsync();

        await svc.AddAsync(1, Day, "client-user", byTrainer: false, "Dzięki!");

        push.Verify(p => p.SendAsync("trainer-1", It.Is<PushMessageDto>(m => m.Url == "/trainer/activity/1")), Times.Once);
    }

    [Fact]
    public async Task Only_Author_Can_Delete()
    {
        var (svc, _, _) = await MakeAsync();
        var added = await svc.AddAsync(1, Day, "trainer-1", byTrainer: true, "Uwaga na kolana");

        (await svc.DeleteAsync(added.Id, "client-user")).Should().BeFalse();
        (await svc.DeleteAsync(added.Id, "trainer-1")).Should().BeTrue();
        (await svc.GetForClientAsync(1)).Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_Comment_Is_Rejected()
    {
        var (svc, _, _) = await MakeAsync();
        var act = () => svc.AddAsync(1, Day, "trainer-1", byTrainer: true, "   ");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
