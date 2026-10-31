using FluentAssertions;
using PTScheduler.Application.Training;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Podsumowanie po treningu dla klienta: rekordy, poprawa, seria tygodni, opis „ostatnio”.</summary>
public class WorkoutHighlightsTests
{
    private static SetRecord S(int reps, decimal kg) => new(reps, kg);

    [Fact]
    public void Heavier_than_ever_is_a_record()
    {
        var r = WorkoutHighlights.Compute([new(1, "Przysiad", [S(5, 100), S(5, 105)], 100, [S(5, 100)])], 0, 1);
        r.Records.Should().Be(1);
        r.Items.Should().ContainSingle(i => i.Kind == "rekord").Which.Text.Should().Be("Rekord: Przysiad — 105 kg (poprzednio 100 kg)");
        r.Fallback.Should().StartWith("Nowy rekord (Przysiad: 105 kg)!");
        r.Facts.Should().ContainKey("najlepszy_rekord");
    }

    [Fact]
    public void More_reps_at_the_same_weight_is_progress()
    {
        var r = WorkoutHighlights.Compute([new(1, "Wiosłowanie", [S(10, 40), S(9, 40)], 40, [S(8, 40), S(8, 40)])], 0, 1);
        r.Records.Should().Be(0);
        r.Improved.Should().Be(1);
        r.Items.Single().Text.Should().Be("Wiosłowanie: 10 powt. przy 40 kg (ostatnio 8)");
    }

    [Fact]
    public void First_time_exercise_is_neither_record_nor_progress()
    {
        var r = WorkoutHighlights.Compute([new(1, "Wykroki", [S(10, 20)], null, null)], 0, 1);
        r.Items.Should().BeEmpty();
        r.Fallback.Should().Be("Trening zaliczony — każdy kolejny buduje formę.");
    }

    [Fact]
    public void Streak_and_month_count_are_mentioned()
    {
        var r = WorkoutHighlights.Compute([new(1, "Plank", [S(1, 0)], null, null)], 3, 6);
        r.Items.Select(i => i.Text).Should().BeEquivalentTo(["3. tydzień z rzędu z treningiem", "6. trening w tym miesiącu"]);
        r.Fallback.Should().StartWith("3. tydzień z rzędu");
    }

    [Fact]
    public void Week_streak_counts_consecutive_weeks()
    {
        var today = new DateOnly(2026, 10, 15); // czwartek
        var days = new[] { new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 8), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 16) };
        WorkoutHighlights.WeekStreak(days, today).Should().Be(3);
        // Bez treningu w tym tygodniu liczymy od poprzedniego.
        WorkoutHighlights.WeekStreak([new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 1)], today).Should().Be(2);
        WorkoutHighlights.WeekStreak([], today).Should().Be(0);
    }

    [Theory]
    [InlineData(new[] { 10, 10, 10 }, new[] { 60.0, 60.0, 60.0 }, "3×10 · 60 kg")]
    [InlineData(new[] { 10, 8 }, new[] { 60.0, 62.5 }, "10×60 · 8×62,5 kg")]
    [InlineData(new[] { 12, 12 }, new[] { 0.0, 0.0 }, "2×12")]
    [InlineData(new[] { 15, 12 }, new[] { 0.0, 0.0 }, "15 · 12 powt.")]
    public void Describe_last_time(int[] reps, double[] kg, string expected) =>
        WorkoutHighlights.Describe(reps.Select((r, i) => new SetRecord(r, (decimal)kg[i])).ToList()).Should().Be(expected);
}
