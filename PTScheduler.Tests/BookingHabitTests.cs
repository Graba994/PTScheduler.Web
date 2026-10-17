using FluentAssertions;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Enums;
using Xunit;

namespace PTScheduler.Tests;

public class BookingHabitTests
{
    private static (DateTime, int, SessionStatus) S(string at, SessionStatus st = SessionStatus.Completed, int type = 1)
        => (DateTime.Parse(at), type, st);

    [Fact]
    public void Detects_Most_Common_Weekday_And_Time()
    {
        var h = BookingHabit.Detect([
            S("2026-10-13 18:00"), S("2026-10-06 18:00"), S("2026-09-29 18:00"),
            S("2026-10-09 07:00"), S("2026-10-02 07:00", SessionStatus.Cancelled), S("2026-10-02 07:00", SessionStatus.Cancelled)]);
        h.Should().NotBeNull();
        h!.Day.Should().Be(DayOfWeek.Tuesday);
        h.Time.Should().Be(new TimeOnly(18, 0));
        h.Count.Should().Be(3);
    }

    [Fact]
    public void No_Habit_When_Nothing_Repeats()
    {
        BookingHabit.Detect([S("2026-10-13 18:00"), S("2026-10-08 09:00")]).Should().BeNull();
    }

    [Fact]
    public void Next_Occurrence_Skips_Passed_Time_Today()
    {
        var h = new BookingHabit(DayOfWeek.Friday, new TimeOnly(18, 0), 1, 3);
        h.NextAfter(new DateTime(2026, 10, 16, 12, 0, 0)).Should().Be(new DateTime(2026, 10, 16, 18, 0, 0));
        h.NextAfter(new DateTime(2026, 10, 16, 19, 0, 0)).Should().Be(new DateTime(2026, 10, 23, 18, 0, 0));
        h.NextAfter(new DateTime(2026, 10, 18, 9, 0, 0)).Should().Be(new DateTime(2026, 10, 23, 18, 0, 0));
    }
}
