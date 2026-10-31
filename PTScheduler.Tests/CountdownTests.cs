using FluentAssertions;
using PTScheduler.Application.Scheduling;
using Xunit;

namespace PTScheduler.Tests;

public class CountdownTests
{
    private static readonly DateTime Start = new(2026, 10, 19, 10, 0, 0);
    private static readonly DateTime End = Start.AddMinutes(60);

    [Theory]
    [InlineData(-25, "Trening za 25 min")]
    [InlineData(-90, "Trening za 1 godz. 30 min")]
    [InlineData(-120, "Trening za 2 godz.")]
    [InlineData(-1, "Trening za chwilę")]
    [InlineData(20, "Trwa · jeszcze 40 min")]
    public void Label_counts_down_and_shows_running_visit(int minutesFromStart, string expected) =>
        Countdown.Label(Start, End, Start.AddMinutes(minutesFromStart)).Should().Be(expected);

    [Fact]
    public void Label_disappears_after_visit_and_soon_flag_kicks_in_15_minutes_before()
    {
        Countdown.Label(Start, End, End).Should().BeNull();
        Countdown.IsSoon(Start, End, Start.AddMinutes(-16)).Should().BeFalse();
        Countdown.IsSoon(Start, End, Start.AddMinutes(-15)).Should().BeTrue();
        Countdown.IsSoon(Start, End, Start.AddMinutes(10)).Should().BeTrue();
        Countdown.IsSoon(Start, End, End).Should().BeFalse();
    }
}
