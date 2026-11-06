using System.Xml.Linq;
using FluentAssertions;
using PTScheduler.Application.Medals;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Medale klientów: reguły zdobywania, serie tygodni, rekordy i grafiki SVG.</summary>
public class MedalTests
{
    [Fact]
    public void Earned_medals_follow_thresholds()
    {
        var earned = MedalRules.Earned(new MedalStats(Trainings: 27, BestWeekStreak: 5, Records: 1, MonthsTogether: 7, Surveys: 3, AppWorkouts: 12));
        earned.Should().BeEquivalentTo(["treningi_1", "treningi_10", "treningi_25", "seria_4", "rekord_1", "staz_6", "plan_10"]);
    }

    [Fact]
    public void New_client_has_no_medals() =>
        MedalRules.Earned(new MedalStats(0, 0, 0, 0, 0, 0)).Should().BeEmpty();

    [Fact]
    public void Progress_is_capped()
    {
        var m = MedalRules.Find("treningi_50")!;
        MedalRules.Progress(m, new MedalStats(25, 0, 0, 0, 0, 0)).Should().Be(0.5);
        MedalRules.Progress(m, new MedalStats(80, 0, 0, 0, 0, 0)).Should().Be(1);
    }

    [Fact]
    public void Best_week_streak_finds_longest_run()
    {
        var days = new[]
        {
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 17), // 3 tygodnie z rzędu
            new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7),                          // 1 tydzień
        };
        MedalRules.BestWeekStreak(days).Should().Be(3);
        MedalRules.BestWeekStreak([]).Should().Be(0);
    }

    [Fact]
    public void Records_count_only_improvements_after_first_time()
    {
        var sessions = new (int, DateOnly, decimal)[]
        {
            (1, new DateOnly(2026, 9, 1), 60), (1, new DateOnly(2026, 9, 8), 62.5m), (1, new DateOnly(2026, 9, 15), 62.5m), (1, new DateOnly(2026, 9, 22), 65),
            (2, new DateOnly(2026, 9, 1), 100), (2, new DateOnly(2026, 9, 8), 95),
        };
        MedalRules.CountRecords(sessions).Should().Be(2);
    }

    [Fact]
    public void Every_medal_renders_valid_svg_in_every_style_and_site_theme()
    {
        var palettes = new[] { "studio", "premium", "sport", "noc", "natura" }.Select(t => MedalArt.Palette("indigo", t, null)).ToList();
        foreach (var style in MedalArt.Styles)
            foreach (var p in palettes)
                foreach (var m in MedalRules.All)
                    XDocument.Parse(MedalArt.Svg(m, style.Key, p)).Root!.Name.LocalName.Should().Be("svg");
    }

    [Fact]
    public void Palette_uses_brand_colors_and_site_accent()
    {
        var studio = MedalArt.Palette("forest", "studio", null);
        studio.Primary.Should().Be("#16A34A");
        studio.Accent.Should().Be("#16A34A", "motyw „Studio” bierze akcent z koloru marki");
        MedalArt.Palette("forest", "sport", null).Accent.Should().Be("#ff5a1f");
        MedalArt.Palette("forest", "sport", "#123456").Accent.Should().Be("#123456");
    }

    [Theory]
    [InlineData("#ffffff", "#111111")]
    [InlineData("#c6f432", "#111111")]
    [InlineData("#1e293b", "#ffffff")]
    public void Contrast_picks_readable_ink(string bg, string ink) => MedalArt.Contrast(bg).Should().Be(ink);
}
