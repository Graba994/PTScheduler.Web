using PTScheduler.Application.Progress;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

public class ProgressSummaryWeeksTests
{
    private static readonly DateOnly Today = new(2026, 10, 16); // czwartek

    [Fact]
    public void Counts_consecutive_weeks_and_keeps_streak_when_current_week_is_empty()
    {
        var days = new[] { new DateOnly(2026, 10, 7), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 22) };
        Assert.Equal(3, ProgressSummaryWeeks.Streak(days, Today));
    }

    [Fact]
    public void Gap_breaks_the_streak()
    {
        var days = new[] { Today, new DateOnly(2026, 9, 28) };
        Assert.Equal(1, ProgressSummaryWeeks.Streak(days, Today));
    }

    [Theory]
    [InlineData(60, "1 godzina")]
    [InlineData(90, "1,5 godziny")]
    [InlineData(720, "12 godzin")]
    [InlineData(180, "3 godziny")]
    public void Hours_are_polish(int minutes, string expected) => Assert.Equal(expected, ProgressSummaryText.Hours(minutes));
}
