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

    [Fact]
    public void Client_Limit_Is_Silent_With_Room_Or_Unlimited()
    {
        FirstSetupRules.ClientLimitNote(10, 50, 10, true).Should().BeNull();
        FirstSetupRules.ClientLimitNote(500, int.MaxValue, 3, false).Should().BeNull();
    }

    [Fact]
    public void Client_Limit_Tells_A_Trainer_The_Whole_Studio_Counts()
    {
        // Trener ma 3 swoich klientów, ale studio jest pełne — bez wyjaśnienia „49 z 50” byłoby niezrozumiałe.
        var note = FirstSetupRules.ClientLimitNote(50, 50, 3, false)!.Value;
        note.Reached.Should().BeTrue();
        note.Text.Should().Be("Plan studia obejmuje 50 klientów — liczą się klienci wszystkich trenerów (Twoich: 3) i wszystkie miejsca są zajęte. Kolejne osoby dodasz, gdy właściciel studia zmieni plan.");
    }

    [Theory]
    [InlineData(50, 50, "Twój plan obejmuje 50 klientów i wszystkie miejsca są zajęte. Kolejne osoby dodasz po zmianie planu.", true)]
    [InlineData(49, 50, "Twój plan obejmuje 50 klientów. Zostało 1 miejsce.", false)]
    [InlineData(18, 20, "Twój plan obejmuje 20 klientów. Zostały 2 miejsca.", false)]
    public void Client_Limit_For_The_Owner(int studio, int max, string text, bool reached) =>
        FirstSetupRules.ClientLimitNote(studio, max, studio, true).Should().Be((text, reached));
}
