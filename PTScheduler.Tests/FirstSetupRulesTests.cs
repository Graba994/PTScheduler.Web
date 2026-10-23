using FluentAssertions;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using Xunit;
using W = PTScheduler.Domain.Rules.FirstSetupRules.Window;

namespace PTScheduler.Tests;

public class FirstSetupRulesTests
{
    private static W Win(DayOfWeek d, int from, int to) => new(d, new TimeOnly(from, 0), new TimeOnly(to, 0));

    [Fact]
    public void Presets_Are_Valid_And_Recognised()
    {
        foreach (var p in FirstSetupRules.Presets)
        {
            FirstSetupRules.Problem(p.Windows).Should().BeNull(p.Key);
            FirstSetupRules.MatchingPreset(p.Windows.Reverse().ToList()).Should().Be(p.Key);
        }
        FirstSetupRules.TotalHours(FirstSetupRules.Presets[0].Windows).Should().Be(60);
        FirstSetupRules.WorkDays(FirstSetupRules.Presets[^1].Windows).Should().Be(6);
    }

    [Fact]
    public void Custom_Hours_Match_No_Preset() =>
        FirstSetupRules.MatchingPreset([Win(DayOfWeek.Monday, 8, 20)]).Should().BeNull();

    [Fact]
    public void Detects_End_Before_Start_And_Overlaps()
    {
        FirstSetupRules.Problem([Win(DayOfWeek.Tuesday, 18, 9)]).Should().Be("Wtorek: koniec musi być po początku.");
        FirstSetupRules.Problem([Win(DayOfWeek.Friday, 8, 12), Win(DayOfWeek.Friday, 11, 15)]).Should().Be("Piątek: godziny nachodzą na siebie.");
        FirstSetupRules.Problem([Win(DayOfWeek.Friday, 8, 12), Win(DayOfWeek.Friday, 12, 15)]).Should().BeNull();
    }

    [Fact]
    public void Next_Slots_Start_Tomorrow_And_Fit_The_Session()
    {
        // Środa 21.10, okna: czwartek 8–10, piątek 16–18 → czw. 8:00 i 9:00, pt. 16:00 i 17:00 (ostatni kończy się o 18:00).
        var now = new DateTime(2026, 10, 21, 12, 0, 0);
        var slots = FirstSetupRules.NextSlots([Win(DayOfWeek.Friday, 16, 18), Win(DayOfWeek.Thursday, 8, 10)], now, 60, 4);
        slots.Should().Equal(
            new DateTime(2026, 10, 22, 8, 0, 0), new DateTime(2026, 10, 22, 9, 0, 0),
            new DateTime(2026, 10, 23, 16, 0, 0), new DateTime(2026, 10, 23, 17, 0, 0));
    }

    [Fact]
    public void Next_Slots_Skip_Windows_Shorter_Than_The_Session() =>
        FirstSetupRules.NextSlots([Win(DayOfWeek.Thursday, 8, 9)], new DateTime(2026, 10, 21), 90, 3).Should().BeEmpty();

    [Theory]
    [InlineData(24, LateCancellationPolicy.ChargeSession, true, "Trening odwołasz bez kosztów do 24 godzin przed treningiem. Później trening przepada. Nieobecność bez odwołania = trening przepada.")]
    [InlineData(1, LateCancellationPolicy.Block, false, "Trening odwołasz bez kosztów do 1 godziny przed treningiem. Później napisz do trenera.")]
    [InlineData(12, LateCancellationPolicy.Refund, true, "Trening możesz odwołać w każdej chwili, bez kosztów.")]
    public void Client_Cancellation_Text(int hours, LateCancellationPolicy late, bool noShow, string expected) =>
        FirstSetupRules.ClientCancellationText(hours, late, noShow).Should().Be(expected);
}
