using FluentAssertions;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Reguły wysyłki przypomnień. Sedno naprawy 04: kanał już wysłany nie jest
/// wysyłany ponownie, a po limicie prób przypomnienie jest porzucane.
/// </summary>
public class ReminderPolicyTests
{
    [Fact]
    public void ShouldSend_When_Applicable_And_Not_Yet_Sent()
    {
        ReminderPolicy.ShouldSend(channelApplicable: true, alreadySent: false).Should().BeTrue();
    }

    [Fact]
    public void ShouldNotSend_When_Already_Sent()
    {
        // Kluczowe: kanał już wysłany nie leci ponownie — nawet jeśli drugi kanał zawiódł.
        ReminderPolicy.ShouldSend(channelApplicable: true, alreadySent: true).Should().BeFalse();
    }

    [Fact]
    public void ShouldNotSend_When_Not_Applicable()
    {
        ReminderPolicy.ShouldSend(channelApplicable: false, alreadySent: false).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void ShouldGiveUp_AtOrAbove_MaxAttempts(int attempts, bool expected)
    {
        ReminderPolicy.ShouldGiveUp(attempts).Should().Be(expected);
    }

    [Fact]
    public void MaxAttempts_Is_Positive()
    {
        ReminderPolicy.MaxAttempts.Should().BeGreaterThan(0);
    }
}
