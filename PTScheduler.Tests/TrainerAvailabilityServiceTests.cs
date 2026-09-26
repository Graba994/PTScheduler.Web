using FluentAssertions;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class TrainerAvailabilityServiceTests
{
    private const string TrainerId = "trainer-1";

    [Fact]
    public async Task GetAvailableSlots_NoRules_ReturnsEmpty()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new TrainerAvailabilityService(factory);

        var slots = await svc.GetAvailableSlotsAsync(TrainerId, DateOnly.FromDateTime(DateTime.UtcNow), 60);

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableSlots_RecurringRule_GeneratesCorrectSlots()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        // 9:00-10:00, 9:30-10:30, 10:00-11:00, 10:30-11:30, 11:00-12:00 (30-min granularity default)
        slots.Should().HaveCount(5);
        slots.Should().OnlyContain(s => s.IsAvailable);
        slots.First().Start.Hour.Should().Be(9);
        slots.Last().Start.Hour.Should().Be(11);
    }

    [Fact]
    public async Task GetAvailableSlots_ExistingSession_MarksOverlappingSlotsUnavailable()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            IsActive = true
        });
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = TrainerId,
            StartTime = monday.ToDateTime(new TimeOnly(10, 0)),
            Status = SessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        var at10 = slots.First(s => s.Start.Hour == 10 && s.Start.Minute == 0);
        at10.IsAvailable.Should().BeFalse();

        var at930 = slots.First(s => s.Start.Hour == 9 && s.Start.Minute == 30);
        at930.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAvailableSlots_CancelledSession_DoesNotBlockSlot()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(11, 0),
            IsActive = true
        });
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = TrainerId,
            StartTime = monday.ToDateTime(new TimeOnly(9, 0)),
            Status = SessionStatus.Cancelled
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        slots.Should().OnlyContain(s => s.IsAvailable);
    }

    [Fact]
    public async Task GetAvailableSlots_WithBreakTime_ExtendsBlockedPeriod()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            IsActive = true
        });
        db.TrainerConfigs.Add(new TrainerConfig
        {
            TrainerUserId = TrainerId,
            BreakAfterSessionMinutes = 15,
            SlotGranularityMinutes = 30
        });
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = TrainerId,
            StartTime = monday.ToDateTime(new TimeOnly(9, 0)),
            Status = SessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        // Session 9:00-10:00, break until 10:15
        // Slot at 9:30 overlaps session -> unavailable
        // Slot at 10:00 ends at 11:00 but starts before break ends (10:00 < 10:15) -> unavailable
        var at10 = slots.First(s => s.Start.Hour == 10 && s.Start.Minute == 0);
        at10.IsAvailable.Should().BeFalse();

        // Slot at 10:30 starts after break -> available
        var at1030 = slots.First(s => s.Start.Hour == 10 && s.Start.Minute == 30);
        at1030.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task GetAvailableSlots_SpecificDate_OverridesDayOfWeek()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            SpecificDate = monday,
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(16, 0),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        slots.Should().NotBeEmpty();
        slots.First().Start.Hour.Should().Be(14);
    }

    [Fact]
    public async Task GetAvailableSlots_InactiveRule_IsIgnored()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            IsActive = false
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableSlots_ValidFromNotYetReached_IsIgnored()
    {
        var (factory, db) = TestDb.CreateFresh();
        var monday = NextWeekday(DayOfWeek.Monday);
        db.TrainerAvailabilities.Add(new TrainerAvailability
        {
            TrainerUserId = TrainerId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 0),
            ValidFrom = monday.AddDays(7),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var slots = await svc.GetAvailableSlotsAsync(TrainerId, monday, 60);

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveConfig_EnforcesMinimumGranularity()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new TrainerAvailabilityService(factory);

        await svc.SaveConfigAsync(TrainerId, new Application.DTOs.TrainerConfigDto
        {
            SlotGranularityMinutes = 5,
            BreakAfterSessionMinutes = 10
        });

        var config = await svc.GetConfigAsync(TrainerId);
        config.SlotGranularityMinutes.Should().Be(15);
        config.BreakAfterSessionMinutes.Should().Be(10);
    }

    [Fact]
    public async Task SaveConfig_DefaultValues_WhenNoConfigExists()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new TrainerAvailabilityService(factory);

        var config = await svc.GetConfigAsync(TrainerId);

        config.BreakAfterSessionMinutes.Should().Be(0);
        config.SlotGranularityMinutes.Should().Be(30);
    }

    [Fact]
    public async Task IsSlotFree_NoSessions_ReturnsTrue()
    {
        var (factory, _) = TestDb.CreateFresh();
        var svc = new TrainerAvailabilityService(factory);

        var free = await svc.IsSlotFreeAsync(TrainerId, DateTime.UtcNow.AddDays(1), 60);

        free.Should().BeTrue();
    }

    [Fact]
    public async Task IsSlotFree_OverlappingSession_ReturnsFalse()
    {
        var (factory, db) = TestDb.CreateFresh();
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(10);
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "K" });
        db.Sessions.Add(new Session
        {
            ClientId = 1, SessionTypeId = 1, TrainerUserId = TrainerId,
            StartTime = start, Status = SessionStatus.Scheduled
        });
        await db.SaveChangesAsync();

        var svc = new TrainerAvailabilityService(factory);
        var free = await svc.IsSlotFreeAsync(TrainerId, start.AddMinutes(30), 60);

        free.Should().BeFalse();
    }

    private static DateOnly NextWeekday(DayOfWeek dow)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        while (d.DayOfWeek != dow) d = d.AddDays(1);
        return d;
    }
}
