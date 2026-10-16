using FluentAssertions;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

public class ProgressInsightsTests
{
    private static ExerciseDay Day(int daysAgo, decimal kg, params int[] reps) =>
        new(new DateOnly(2026, 10, 14).AddDays(-daysAgo), reps.Select(r => new SetResult(r, kg)).ToList());

    [Fact]
    public void Strength_Change_Uses_First_And_Last_Loaded_Day()
    {
        var days = new[] { Day(1, 70, 8, 8), Day(20, 0, 15), Day(30, 60, 10, 10), Day(10, 65, 8) };
        ProgressInsights.StrengthChange(days).Should().Be((60m, 70m));
    }

    [Fact]
    public void Strength_Change_Needs_Two_Loaded_Days() =>
        ProgressInsights.StrengthChange([Day(1, 60, 10), Day(3, 0, 20)]).Should().BeNull();

    [Fact]
    public void Detects_Last_Set_Drop_In_Two_Of_Three_Workouts()
    {
        var plan = new PlannedTarget(3, "10", 60, 90);
        var insights = ProgressInsights.Analyze(plan, [Day(1, 60, 10, 10, 8), Day(4, 60, 10, 10, 10), Day(8, 60, 10, 9, 7)]);
        insights.Should().ContainSingle(i => i.Kind == InsightKind.LastSetDrop)
            .Which.Recommendation.Should().Contain("z 90 s do 120 s");
    }

    [Fact]
    public void Single_Weaker_Day_Is_Not_A_Pattern()
    {
        var plan = new PlannedTarget(3, "10", 60, 90);
        ProgressInsights.Analyze(plan, [Day(1, 60, 10, 10, 8), Day(4, 60, 10, 10, 10), Day(8, 60, 10, 10, 10)])
            .Should().BeEmpty();
    }

    [Fact]
    public void Detects_Range_That_Never_Fits()
    {
        var plan = new PlannedTarget(3, "10-12", 16, null);
        var insights = ProgressInsights.Analyze(plan, [Day(1, 16, 9, 8, 7), Day(4, 16, 9, 8, 8)]);
        var item = insights.Should().ContainSingle(i => i.Kind == InsightKind.BelowRange).Subject;
        item.Finding.Should().Be("Zakres 10–12 nie wchodzi (ostatnio 9/8/7 na 16 kg).");
        item.Recommendation.Should().Contain("8–10");
        insights.Should().NotContain(i => i.Kind == InsightKind.LastSetDrop);
    }

    [Fact]
    public void Detects_Fewer_Sets_And_Below_Target()
    {
        var plan = new PlannedTarget(4, "8-12", 80, null);
        var insights = ProgressInsights.Analyze(plan, [Day(1, 70, 10, 10, 10), Day(4, 70, 10, 10, 10)]);
        insights.Select(i => i.Kind).Should().BeEquivalentTo([InsightKind.FewerSets, InsightKind.BelowTarget]);
        insights.Single(i => i.Kind == InsightKind.BelowTarget).Finding.Should().Contain("70 kg").And.Contain("80 kg");
    }

    [Fact]
    public void Detects_Stagnation_Over_Four_Workouts()
    {
        var plan = new PlannedTarget(3, "8-12", 50, null);
        var days = new[] { Day(1, 50, 9, 9, 8), Day(4, 50, 9, 8, 8), Day(8, 50, 9, 9, 8), Day(11, 50, 9, 8, 8) };
        ProgressInsights.Analyze(plan, days).Should().Contain(i => i.Kind == InsightKind.Stagnation);
    }

    [Fact]
    public void Rising_Reps_Are_Not_Stagnation()
    {
        var plan = new PlannedTarget(3, "8-12", 50, null);
        var days = new[] { Day(1, 50, 11, 10, 10), Day(4, 50, 10, 10, 9), Day(8, 50, 9, 9, 9), Day(11, 50, 8, 8, 8) };
        ProgressInsights.Analyze(plan, days).Should().NotContain(i => i.Kind == InsightKind.Stagnation);
    }
}

public class ProgressSummaryTextTests
{
    private static PTScheduler.Application.DTOs.ClientProgressDto Sample() => new()
    {
        CompletedSessions = 10,
        WorkoutDays = 9,
        Since = new DateOnly(2026, 9, 1),
        Strength = [new("Wyciskanie sztangi", 60m, 70m), new("Przysiad", 80m, 90m)],
        WeightFromKg = 82.5m,
        WeightToKg = 80.4m,
        Attended30 = 9,
        Missed30 = 1
    };

    [Fact]
    public void Client_Summary_Lists_Facts_With_Polish_Formatting()
    {
        var s = PTScheduler.Application.Progress.ProgressSummaryText.ForClient(Sample());
        s.Fallback.Should().Be("Masz za sobą już 10 treningów (od 1 września 2026). Siła rośnie — Wyciskanie sztangi: 60 → 70 kg, Przysiad: 80 → 90 kg. Masa ciała −2,1 kg.");
        s.Facts["cwiczenie_1_kg"].Should().Be("60 → 70 (+10)");
        s.Facts["masa_ciala_kg"].Should().Be("82,5 → 80,4 (−2,1)");
    }

    [Fact]
    public void Trainer_Summary_Mentions_Attention_Items()
    {
        var dto = Sample();
        var withInsight = new PTScheduler.Application.DTOs.ClientProgressDto
        {
            CompletedSessions = dto.CompletedSessions, Since = dto.Since, Strength = dto.Strength, Attended30 = 9, Missed30 = 1,
            Insights = [new("Wyciskanie sztangi", 1, "3 × 10", "10/10/8", InsightKind.LastSetDrop, "f", "r")]
        };
        var s = PTScheduler.Application.Progress.ProgressSummaryText.ForTrainer(withInsight);
        s.Fallback.Should().Contain("Największy postęp: Wyciskanie sztangi +10 kg.")
            .And.Contain("9 obecności i 1 nieobecność")
            .And.Contain("1 ćwiczenie wymaga uwagi");
    }
}
