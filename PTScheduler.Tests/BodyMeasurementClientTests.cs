using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Pomiary wpisywane przez klienta: walidacja, powiadomienie trenera, usuwanie tylko własnych.</summary>
public class BodyMeasurementClientTests
{
    private static readonly DateOnly Today = new(2026, 9, 28);

    [Fact]
    public void Rules_Catch_Typos_And_Empty_Entries()
    {
        BodyMeasurementRules.Validate(Today, Today, 72.5m, null).Should().BeEmpty();
        BodyMeasurementRules.Validate(Today, Today, null, null, ("Talia", null)).Should().ContainSingle(e => e.Contains("przynajmniej jedną"));
        BodyMeasurementRules.Validate(Today, Today, 725m, null).Should().ContainSingle(e => e.Contains("20–400"));
        BodyMeasurementRules.Validate(Today.AddDays(1), Today, 70m, null).Should().ContainSingle(e => e.Contains("przyszłości"));
        BodyMeasurementRules.Validate(Today, Today, null, null, ("Talia", 800m)).Should().ContainSingle(e => e.StartsWith("Talia"));
    }

    [Fact]
    public async Task Client_Entry_Notifies_Trainer_And_Only_Own_Entries_Can_Be_Deleted()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", LastName = "Nowak", TrainerUserId = "t1" });
            db.Clients.Add(new Client { Id = 2, ApplicationUserId = "cu2", FirstName = "Ola", TrainerUserId = "t1" });
            await db.SaveChangesAsync();
        }
        var push = new Mock<IWebPushService>();
        var svc = new BodyMeasurementService(f, TestClock.AtWallClock(new DateTime(2026, 9, 28, 12, 0, 0)), push.Object,
            NullLogger<BodyMeasurementService>.Instance);

        var own = await svc.AddAsync(new CreateBodyMeasurementDto { ClientId = 1, MeasurementDate = Today, WeightKg = 72.5m, AddedByClient = true });
        var byTrainer = await svc.AddAsync(new CreateBodyMeasurementDto { ClientId = 1, MeasurementDate = Today, WaistCm = 80m });

        own.AddedByClient.Should().BeTrue();
        push.Verify(p => p.SendAsync("t1", It.Is<PushMessageDto>(m => m.Url == "/clients/1?tab=measurements" && m.Body.Contains("72"))), Times.Once);

        (await svc.DeleteOwnAsync(byTrainer.Id, 1)).Should().BeFalse("wpis trenera zostaje");
        (await svc.DeleteOwnAsync(own.Id, 2)).Should().BeFalse("inny klient nie usunie cudzego pomiaru");
        (await svc.DeleteOwnAsync(own.Id, 1)).Should().BeTrue();
        (await svc.GetAsync(1)).Should().ContainSingle(m => m.Id == byTrainer.Id);

        var act = () => svc.AddAsync(new CreateBodyMeasurementDto { ClientId = 1, MeasurementDate = Today, WeightKg = 725m, AddedByClient = true });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*20–400*");
    }
}
