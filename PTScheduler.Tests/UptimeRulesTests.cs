using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Monitor dostępności: kiedy awaria, kiedy powrót, uptime i dane do wykresu.</summary>
public class UptimeRulesTests
{
    [Fact]
    public void Down_only_after_configured_failures_in_a_row()
    {
        var first = UptimeRules.Next(UptimeState.Up, 0, retriesBeforeDown: 2, up: false);
        first.State.Should().Be(UptimeState.Up);
        first.WentDown.Should().BeFalse();
        first.FailuresInRow.Should().Be(1);

        var second = UptimeRules.Next(first.State, first.FailuresInRow, 2, up: false);
        second.State.Should().Be(UptimeState.Down);
        second.WentDown.Should().BeTrue();

        var third = UptimeRules.Next(second.State, second.FailuresInRow, 2, up: false);
        third.WentDown.Should().BeFalse("alarm idzie raz, na początku awarii");
        third.State.Should().Be(UptimeState.Down);
    }

    [Fact]
    public void One_success_ends_the_outage_and_resets_failures()
    {
        var back = UptimeRules.Next(UptimeState.Down, 7, 2, up: true);
        back.State.Should().Be(UptimeState.Up);
        back.CameBack.Should().BeTrue();
        back.FailuresInRow.Should().Be(0);

        UptimeRules.Next(UptimeState.Up, 1, 2, up: true).CameBack.Should().BeFalse("chwilowa czerwona kreska to nie awaria");
        UptimeRules.Next(UptimeState.Pending, 0, 1, up: false).WentDown.Should().BeTrue();
    }

    [Fact]
    public void Percent_and_labels()
    {
        UptimeRules.Percent(0, 0).Should().BeNull();
        UptimeRules.Percent(1439, 1440).Should().Be(99.93);
        UptimeRules.Show(100).Should().Be("100%");
        UptimeRules.Show(99.999).Should().Be("100%");
        UptimeRules.Show(99.93).Should().Be("99,93%".Replace(",", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator));
        UptimeRules.Show(null).Should().Be("—");
    }

    [Fact]
    public void Buckets_average_successful_beats_and_flag_failures()
    {
        var from = new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc);
        var beats = new List<UptimeBeat>
        {
            new() { At = from.AddMinutes(1), Up = true, Ms = 100 },
            new() { At = from.AddMinutes(2), Up = true, Ms = 300 },
            new() { At = from.AddMinutes(11), Up = false, Ms = null },
        };
        var b = UptimeRules.Buckets(beats, from, from.AddMinutes(30), 3);
        b.Should().HaveCount(3);
        b[0].AvgMs.Should().Be(200);
        b[0].AnyDown.Should().BeFalse();
        b[1].AvgMs.Should().BeNull();
        b[1].AnyDown.Should().BeTrue();
        b[2].AvgMs.Should().BeNull();
    }

    [Fact]
    public void Tenant_url_uses_active_custom_domain()
    {
        UptimeRules.TenantUrl(new Tenant { Domain = "anna.hantly.pl" }).Should().Be("https://anna.hantly.pl/health");
        UptimeRules.TenantUrl(new Tenant { Domain = "anna.hantly.pl", CustomDomain = "trening-anna.pl", CustomDomainStatus = "active" })
            .Should().Be("https://trening-anna.pl/health");
        UptimeRules.TenantUrl(new Tenant { Domain = "anna.hantly.pl", CustomDomain = "trening-anna.pl", CustomDomainStatus = "pending" })
            .Should().Be("https://anna.hantly.pl/health");
    }

    [Fact]
    public void Url_validation()
    {
        UptimeRules.ValidateUrl("https://hantly.pl").Should().BeNull();
        UptimeRules.ValidateUrl("hantly.pl").Should().NotBeNull();
        UptimeRules.ValidateUrl("ftp://x").Should().NotBeNull();
        UptimeRules.ValidateUrl(" ").Should().NotBeNull();
    }
}
