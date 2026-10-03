using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Scheduling;
using PTScheduler.Domain.Enums;
using Xunit;

namespace PTScheduler.Tests;

public class CancellationRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 10, 0, 0);

    [Theory]
    [InlineData(LateCancellationPolicy.Block, 30, true, false, true)]
    [InlineData(LateCancellationPolicy.Block, 23, false, true, false)]
    [InlineData(LateCancellationPolicy.ChargeSession, 23, true, true, false)]
    [InlineData(LateCancellationPolicy.Refund, 23, true, true, true)]
    public void Evaluates_Window_And_Policy(LateCancellationPolicy policy, int hoursBefore,
        bool allowed, bool late, bool refund)
    {
        var cfg = new TrainerConfigDto { CancellationWindowHours = 24, LateCancellationPolicy = policy };

        var d = CancellationRules.ForClient(cfg, Now.AddHours(hoursBefore), Now);

        d.Allowed.Should().Be(allowed);
        d.IsLate.Should().Be(late);
        d.RefundsSession.Should().Be(refund);
    }

    [Fact]
    public void Zero_Window_Allows_Until_Start()
    {
        var cfg = new TrainerConfigDto { CancellationWindowHours = 0 };
        CancellationRules.ForClient(cfg, Now.AddMinutes(5), Now).Allowed.Should().BeTrue();
        CancellationRules.ForClient(cfg, Now.AddMinutes(-5), Now).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Describe_Mentions_Window_And_NoShow()
    {
        var cfg = new TrainerConfigDto { CancellationWindowHours = 12, LateCancellationPolicy = LateCancellationPolicy.ChargeSession };
        CancellationRules.Describe(cfg).Should().Contain("12 h").And.Contain("Nieobecność");
    }
}
