using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTScheduler.Application.Exceptions;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Infrastructure.Services.Google;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class GoogleCalendarSyncTests
{
    // Poniedziałek, czas warszawski (CEST, UTC+2).
    private static readonly DateTime Now = new(2026, 10, 5, 8, 0, 0);
    private const string Trainer = "t1";

    /// <summary>Kalendarz Google w pamięci — zapisuje wszystko, co wysyła synchronizacja.</summary>
    private sealed class FakeCalendar : IGoogleCalendarApi
    {
        public readonly Dictionary<string, GoogleEvent> Events = [];
        public readonly Dictionary<string, GoogleEventWrite> Written = [];
        public int Inserts, Patches, Deletes;
        private int _seq;

        public Task<List<GoogleEvent>> ListAsync(string token, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
            Task.FromResult(Events.Values.ToList());

        public Task<string> InsertAsync(string token, GoogleEventWrite e, CancellationToken ct = default)
        {
            var id = "ev" + ++_seq;
            Store(id, e);
            Inserts++;
            return Task.FromResult(id);
        }

        public Task<bool> PatchAsync(string token, string eventId, GoogleEventWrite e, CancellationToken ct = default)
        {
            if (!Events.ContainsKey(eventId)) return Task.FromResult(false);
            Store(eventId, e);
            Patches++;
            return Task.FromResult(true);
        }

        public Task DeleteAsync(string token, string eventId, CancellationToken ct = default)
        {
            if (Events.Remove(eventId)) Deletes++;
            Written.Remove(eventId);
            return Task.CompletedTask;
        }

        private void Store(string id, GoogleEventWrite e)
        {
            Written[id] = e;
            Events[id] = new GoogleEvent(id, "confirmed", "opaque", new DateTimeOffset(e.StartUtc, TimeSpan.Zero),
                new DateTimeOffset(e.EndUtc, TimeSpan.Zero), null, null, e.App, e.SessionId);
        }

        public void AddExternal(string id, DateTime startUtc, DateTime endUtc, string? transparency = null) =>
            Events[id] = new GoogleEvent(id, "confirmed", transparency, new DateTimeOffset(startUtc, TimeSpan.Zero),
                new DateTimeOffset(endUtc, TimeSpan.Zero), null, null, null, null);
    }

    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync()
    {
        var (f, _) = TestDb.CreateFresh();
        await using var db = f.CreateDbContext();
        db.SessionTypes.Add(new SessionType { Id = 1, Name = "Trening personalny", DurationMinutes = 60 });
        db.Clients.Add(new Client { Id = 1, FirstName = "Ola", LastName = "Kowal", TrainerUserId = Trainer });
        db.Sessions.Add(new Session { Id = 10, ClientId = 1, SessionTypeId = 1, TrainerUserId = Trainer, StartTime = Now.AddDays(1).Date.AddHours(9), Status = SessionStatus.Scheduled });
        db.Sessions.Add(new Session { Id = 11, ClientId = 1, SessionTypeId = 1, TrainerUserId = Trainer, StartTime = Now.AddDays(2).Date.AddHours(18), Status = SessionStatus.Scheduled });
        db.Sessions.Add(new Session { Id = 12, ClientId = 1, SessionTypeId = 1, TrainerUserId = "t2", StartTime = Now.AddDays(1).Date.AddHours(9), Status = SessionStatus.Scheduled });
        db.CalendarConnections.Add(new CalendarConnection { UserId = Trainer, Mode = CalendarConnectionModes.Platform, GoogleEmail = "t1@gmail.com" });
        await db.SaveChangesAsync();
        return f;
    }

    private static GoogleCalendarService Make(IDbContextFactory<ApplicationDbContext> f, FakeCalendar cal, bool revoked = false)
    {
        var broker = new Mock<IGoogleTokenBroker>();
        broker.SetupGet(b => b.IsManaged).Returns(true);
        broker.Setup(b => b.GetAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(revoked ? (null, true) : ("token", false));
        return new GoogleCalendarService(f, cal, broker.Object, new Mock<IGoogleMeetService>().Object,
            TestClock.AtWallClock(Now), NullLogger<GoogleCalendarService>.Instance);
    }

    [Fact]
    public async Task Pushes_Trainer_Sessions_Once_With_Utc_Times_And_Tags()
    {
        var f = await SeedAsync();
        var cal = new FakeCalendar();
        var svc = Make(f, cal);

        var r = await svc.SyncAsync(Trainer);
        r.Ok.Should().BeTrue(r.Error);
        r.Created.Should().Be(2, "wizyta innego trenera nie trafia do tego kalendarza");

        var first = cal.Written.Values.Single(w => w.SessionId == 10);
        first.Summary.Should().Be("Trening personalny — Ola Kowal");
        first.StartUtc.Should().Be(new DateTime(2026, 10, 6, 7, 0, 0), "9:00 w Warszawie to 7:00 UTC");
        first.App.Should().Be(GoogleCalendarApi.InstanceKey);

        (await svc.SyncAsync(Trainer)).Should().Match<Application.DTOs.CalendarSyncResult>(x => x.Created == 0 && x.Updated == 0 && x.Deleted == 0);
        cal.Patches.Should().Be(0, "bez zmian nie wysyłamy nic do Google");
    }

    [Fact]
    public async Task Reschedule_Patches_And_Cancellation_Deletes_The_Event()
    {
        var f = await SeedAsync();
        var cal = new FakeCalendar();
        var svc = Make(f, cal);
        await svc.SyncAsync(Trainer);

        await using (var db = f.CreateDbContext())
        {
            (await db.Sessions.FindAsync(10))!.StartTime = Now.AddDays(1).Date.AddHours(11);
            (await db.Sessions.FindAsync(11))!.Status = SessionStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var r = await svc.SyncAsync(Trainer);
        (r.Updated, r.Deleted).Should().Be((1, 1));
        cal.Written.Values.Should().ContainSingle().Which.StartUtc.Should().Be(new DateTime(2026, 10, 6, 9, 0, 0));
        await using var check = f.CreateDbContext();
        (await check.Sessions.FindAsync(11))!.CalendarEventId.Should().BeNull();
    }

    [Fact]
    public async Task Event_Removed_By_Hand_Is_Recreated_And_Deleted_Session_Event_Is_Cleaned_Up()
    {
        var f = await SeedAsync();
        var cal = new FakeCalendar();
        var svc = Make(f, cal);
        await svc.SyncAsync(Trainer);

        var ev10 = cal.Written.Single(w => w.Value.SessionId == 10).Key;
        cal.Events.Remove(ev10);
        cal.Written.Remove(ev10);
        // Wydarzenie po wizycie usuniętej z bazy (np. przez administratora).
        cal.Events["orphan"] = new GoogleEvent("orphan", "confirmed", "opaque", new DateTimeOffset(Now.AddDays(3), TimeSpan.Zero),
            new DateTimeOffset(Now.AddDays(3).AddHours(1), TimeSpan.Zero), null, null, GoogleCalendarApi.InstanceKey, 999);
        // Wizyta innego trenera z tego samego konta Google (tryb „own”) — nie ruszamy.
        cal.Events["foreign"] = new GoogleEvent("foreign", "confirmed", "opaque", new DateTimeOffset(Now.AddDays(1), TimeSpan.Zero),
            new DateTimeOffset(Now.AddDays(1).AddHours(1), TimeSpan.Zero), null, null, GoogleCalendarApi.InstanceKey, 12);

        await svc.SyncAsync(Trainer);
        cal.Written.Values.Should().Contain(w => w.SessionId == 10);
        cal.Events.Should().NotContainKey("orphan").And.ContainKey("foreign");
    }

    [Fact]
    public async Task Busy_Google_Events_Block_Slots_But_Free_And_Own_Events_Do_Not()
    {
        var f = await SeedAsync();
        await using (var db = f.CreateDbContext())
        {
            db.TrainerAvailabilities.Add(new TrainerAvailability { TrainerUserId = Trainer, DayOfWeek = DayOfWeek.Wednesday, StartTime = new(9, 0), EndTime = new(13, 0), IsActive = true });
            await db.SaveChangesAsync();
        }
        var cal = new FakeCalendar();
        // Środa 7.10: 10:00–11:00 czasu PL zajęte (8:00–9:00 UTC), 12:00 „dostępny” — nie blokuje.
        cal.AddExternal("dentist", new DateTime(2026, 10, 7, 8, 0, 0), new DateTime(2026, 10, 7, 9, 0, 0));
        cal.AddExternal("reminder", new DateTime(2026, 10, 7, 10, 0, 0), new DateTime(2026, 10, 7, 11, 0, 0), transparency: "transparent");
        var svc = Make(f, cal);

        (await svc.SyncAsync(Trainer)).BusyBlocks.Should().Be(1);
        var block = (await svc.GetBusyBlocksAsync(Trainer, Now, Now.AddDays(7))).Single();
        (block.StartTime, block.EndTime).Should().Be((new DateTime(2026, 10, 7, 10, 0, 0), new DateTime(2026, 10, 7, 11, 0, 0)));

        var availability = new TrainerAvailabilityService(f);
        var slots = await availability.GetAvailableSlotsAsync(Trainer, new DateOnly(2026, 10, 7), 60);
        slots.Where(s => !s.IsAvailable).Select(s => s.Start.TimeOfDay).Should().BeEquivalentTo(new[] { new TimeSpan(9, 30, 0), new TimeSpan(10, 0, 0), new TimeSpan(10, 30, 0) });
        slots.Single(s => s.Start.Hour == 12 && s.Start.Minute == 0).IsAvailable.Should().BeTrue();

        var conflict = await availability.FindConflictAsync(Trainer, new DateTime(2026, 10, 7, 10, 30, 0), 60);
        conflict!.SessionId.Should().Be(0);
        new SlotConflictException(conflict).Message.Should().Contain("kalendarzu Google");
    }

    [Fact]
    public async Task Turning_Off_Blocking_Removes_Blocks_And_Revoked_Access_Asks_To_Reconnect()
    {
        var f = await SeedAsync();
        var cal = new FakeCalendar();
        cal.AddExternal("x", new DateTime(2026, 10, 7, 8, 0, 0), new DateTime(2026, 10, 7, 9, 0, 0));
        var svc = Make(f, cal);
        await svc.SyncAsync(Trainer);
        await svc.SaveOptionsAsync(Trainer, pushSessions: true, blockBusy: false, showClientName: false, createMeetLinks: false);
        (await svc.GetBusyBlocksAsync(Trainer, Now, Now.AddDays(7))).Should().BeEmpty();

        await svc.SyncAsync(Trainer);
        cal.Written.Values.Should().OnlyContain(w => w.Summary == "Trening personalny", "bez nazwiska klienta po zmianie ustawienia");

        var revoked = Make(f, cal, revoked: true);
        (await revoked.SyncAsync(Trainer)).Ok.Should().BeFalse();
        var status = await revoked.GetStatusAsync(Trainer);
        (status.Connected, status.NeedsReconnect).Should().Be((true, true));
    }
}
