using FluentAssertions;
using PTScheduler.Application.Demo;
using Xunit;

namespace PTScheduler.Tests;

public class DemoScenariosTests
{
    [Fact]
    public void Scenarios_have_unique_keys_and_sane_rates()
    {
        DemoScenarios.All.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        DemoScenarios.All.Should().OnlyContain(s => s.Months >= 1 && s.Clients > 0
            && s.CancelRate + s.NoShowRate < 0.3 && s.LateCancelRate <= s.CancelRate && s.UnpaidRate < 0.5);
        DemoScenarios.Find("kryzys")!.Downturn.Should().BeTrue();
        DemoScenarios.Find("studio")!.TeamTrainers.Should().BeGreaterThan(0);
        DemoScenarios.Find("nie-ma")!.Should().BeNull();
    }

    [Theory]
    [InlineData("anna.nowak@demo.invalid", true)]
    [InlineData("ANNA@DEMO.INVALID", true)]
    [InlineData("jan.kowalski@demo.pl", false)]
    [InlineData("klient@gmail.com", false)]
    [InlineData(null, false)]
    public void Demo_addresses_use_reserved_domain(string? email, bool demo) =>
        DemoScenarios.IsDemoEmail(email).Should().Be(demo);

    [Theory]
    [InlineData("Kowalski", true, "Kowalska")]
    [InlineData("Nowicki", true, "Nowicka")]
    [InlineData("Nowak", true, "Nowak")]
    [InlineData("Kowalski", false, "Kowalski")]
    public void Female_surnames_get_feminine_form(string male, bool female, string expected) =>
        DemoScenarios.Surname(male, female).Should().Be(expected);

    [Fact]
    public void Poisson_mean_is_close_to_requested_and_never_negative()
    {
        var rng = new Random(7);
        var samples = Enumerable.Range(0, 20_000).Select(_ => DemoScenarios.Poisson(rng, 2.0)).ToList();
        samples.Should().OnlyContain(x => x >= 0);
        samples.Average().Should().BeApproximately(2.0, 0.08);
        DemoScenarios.Poisson(rng, 0).Should().Be(0);
    }

    [Fact]
    public void Visit_hours_stay_within_working_day()
    {
        var rng = new Random(1);
        Enumerable.Range(0, 500).Select(_ => DemoScenarios.VisitHour(rng)).Should().OnlyContain(h => h >= 7 && h <= 20);
    }
}
