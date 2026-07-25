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

public class PublicBookingServiceTests
{
    [Fact]
    public async Task GetActiveOffer_NoConfig_ReturnsUnavailable()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = MakeService(factory);

        var offer = await svc.GetActiveOfferAsync();

        offer.IsAvailable.Should().BeFalse();
        offer.UnavailableReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetActiveOffer_OnlyInactiveConfig_ReturnsUnavailable()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.IntroSessionConfigs.Add(new IntroSessionConfig
        {
            TrainerUserId = "t1",
            IsActive = false,
            DurationMinutes = 60
        });
        await db.SaveChangesAsync();

        var svc = MakeService(factory);

        var offer = await svc.GetActiveOfferAsync();
        offer.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveOffer_PicksFirstActiveAndResolvesTrainerName()
    {
        var (factory, db) = TestDb.CreateFresh();
        // Inactive config first (ID-wise), so we can verify the service skips it.
        db.IntroSessionConfigs.Add(new IntroSessionConfig
        {
            TrainerUserId = "trainer-inactive",
            IsActive = false,
            DurationMinutes = 30
        });
        db.IntroSessionConfigs.Add(new IntroSessionConfig
        {
            TrainerUserId = "trainer-real",
            IsActive = true,
            DurationMinutes = 60,
            IsFree = false,
            Price = 150m,
            Description = "Pierwsza sesja diagnostyczna"
        });
        await db.SaveChangesAsync();

        var um = MockUserManagerHelper.Create();
        um.Setup(m => m.FindByIdAsync("trainer-real")).ReturnsAsync(new ApplicationUser
        {
            Id = "trainer-real",
            FirstName = "Anna",
            LastName = "Kowalska",
            Email = "anna@example.com"
        });

        var svc = MakeService(factory, um);

        var offer = await svc.GetActiveOfferAsync();

        offer.IsAvailable.Should().BeTrue();
        offer.TrainerUserId.Should().Be("trainer-real");
        offer.TrainerName.Should().Be("Anna Kowalska");
        offer.DurationMinutes.Should().Be(60);
        offer.Price.Should().Be(150m);
        offer.Description.Should().Be("Pierwsza sesja diagnostyczna");
    }

    [Fact]
    public async Task GetActiveOffer_ConfigExistsButTrainerMissing_ReturnsUnavailable()
    {
        var (factory, db) = TestDb.CreateFresh();
        db.IntroSessionConfigs.Add(new IntroSessionConfig
        {
            TrainerUserId = "ghost",
            IsActive = true,
            DurationMinutes = 60
        });
        await db.SaveChangesAsync();

        var um = MockUserManagerHelper.Create();
        um.Setup(m => m.FindByIdAsync("ghost")).ReturnsAsync((ApplicationUser?)null);

        var svc = MakeService(factory, um);

        var offer = await svc.GetActiveOfferAsync();

        offer.IsAvailable.Should().BeFalse();
        offer.UnavailableReason.Should().Contain("Trener");
    }

    [Fact]
    public async Task GetUpcomingSlots_OnlyDaysWithSlotsReturned()
    {
        var (factory, _) = TestDb.CreateFresh();

        var avail = new Mock<ITrainerAvailabilityService>();
        // Trainer is only available on Mondays. All other days return empty list.
        avail.Setup(s => s.GetAvailableSlotsAsync("trainer", It.IsAny<DateOnly>(), 60))
            .ReturnsAsync((string _, DateOnly d, int _) =>
                d.DayOfWeek == DayOfWeek.Monday
                    ? new List<AvailableSlotDto>
                    {
                        new() { Start = d.ToDateTime(new TimeOnly(10, 0)), IsAvailable = true },
                        new() { Start = d.ToDateTime(new TimeOnly(11, 0)), IsAvailable = true }
                    }
                    : new List<AvailableSlotDto>());

        var svc = MakeService(factory, availabilityService: avail);

        var days = await svc.GetUpcomingSlotsAsync("trainer", 60, daysAhead: 14);

        days.Should().NotBeEmpty();
        days.Should().OnlyContain(d => d.Date.DayOfWeek == DayOfWeek.Monday);
        days.Should().OnlyContain(d => d.Slots.Count == 2);
        days.Should().OnlyContain(d => !string.IsNullOrEmpty(d.DayLabel));
    }

    [Fact]
    public async Task GetUpcomingSlots_RespectsAvailableFlag()
    {
        var (factory, _) = TestDb.CreateFresh();

        var avail = new Mock<ITrainerAvailabilityService>();
        // Mix of available and unavailable slots — only available should pass through.
        avail.Setup(s => s.GetAvailableSlotsAsync("trainer", It.IsAny<DateOnly>(), 60))
            .ReturnsAsync((string _, DateOnly d, int _) => new List<AvailableSlotDto>
            {
                new() { Start = d.ToDateTime(new TimeOnly(9, 0)), IsAvailable = true },
                new() { Start = d.ToDateTime(new TimeOnly(10, 0)), IsAvailable = false },
                new() { Start = d.ToDateTime(new TimeOnly(11, 0)), IsAvailable = true }
            });

        var svc = MakeService(factory, availabilityService: avail);

        var days = await svc.GetUpcomingSlotsAsync("trainer", 60, daysAhead: 1);

        days.Should().HaveCount(1);
        days[0].Slots.Should().HaveCount(2);   // 09:00 and 11:00, not 10:00
        days[0].Slots.Select(s => s.Label).Should().BeEquivalentTo(["09:00", "11:00"]);
    }

    private static PublicBookingService MakeService(
        Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> factory,
        Mock<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>? userManager = null,
        Mock<ITrainerAvailabilityService>? availabilityService = null,
        Mock<IEmailService>? emailService = null)
    {
        userManager ??= MockUserManagerHelper.Create();
        availabilityService ??= new Mock<ITrainerAvailabilityService>();
        emailService ??= new Mock<IEmailService>();
        var emailTemplateService = new Mock<IEmailTemplateService>();
        return new PublicBookingService(
            factory,
            userManager.Object,
            availabilityService.Object,
            emailService.Object,
            emailTemplateService.Object,
            NullLogger<PublicBookingService>.Instance);
    }
}
