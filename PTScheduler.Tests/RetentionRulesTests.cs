using FluentAssertions;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Retencja klientów: ryzyko odejścia, trend samopoczucia, ankieta zdrowotna a plan.</summary>
public class RetentionRulesTests
{
    private static ClientChurnRules.Input Loyal() => new(
        DaysSinceLastVisit: 3, HasUpcoming: true, VisitsLast28: 8, VisitsPrev56: 16, CompletedTotal: 40,
        CancellationsLast30: 0, NoShowsLast30: 0, PackageRemaining: 6, PackageEndedRecently: false,
        WellbeingDropping: false, LowSatisfaction: false, RecentPain: false, AppWorkoutsLast14: 2, AppWorkoutsPrev28: 4);

    [Fact]
    public void Regular_client_has_no_churn_risk() =>
        ClientChurnRules.Score(Loyal()).Score.Should().Be(0);

    [Fact]
    public void Long_break_without_upcoming_suggests_comeback_message()
    {
        var r = ClientChurnRules.Score(Loyal() with { DaysSinceLastVisit = 25, HasUpcoming = false });
        r.Score.Should().Be(35);
        r.IsRisk.Should().BeTrue();
        r.Top!.Purpose.Should().Be(DraftPurpose.Comeback);
        r.Top.Text.Should().Be("Ostatni trening 25 dni temu, nic nie zaplanowane");
    }

    [Fact]
    public void Upcoming_session_cancels_the_break_signal() =>
        ClientChurnRules.Score(Loyal() with { DaysSinceLastVisit = 25, HasUpcoming = true }).Score.Should().Be(0);

    [Fact]
    public void Frequency_drop_counts_only_for_regular_clients()
    {
        var r = ClientChurnRules.Score(Loyal() with { VisitsLast28 = 3, VisitsPrev56 = 16 });
        r.Reasons.Should().ContainSingle(x => x.Text == "Rzadziej na treningach: 0,8/tydz. (wcześniej 2)");
        // Nowy klient — za mało historii.
        ClientChurnRules.Score(Loyal() with { VisitsLast28 = 0, VisitsPrev56 = 4, CompletedTotal = 4 }).Score.Should().Be(0);
    }

    [Fact]
    public void Ended_package_and_missed_sessions_add_up_to_high_risk()
    {
        var r = ClientChurnRules.Score(Loyal() with
        {
            HasUpcoming = false, DaysSinceLastVisit = 15, PackageRemaining = 0, PackageEndedRecently = true,
            CancellationsLast30 = 2, NoShowsLast30 = 1
        });
        r.Score.Should().Be(60);
        r.IsHigh.Should().BeTrue();
        r.Reasons.Select(x => x.Text).Should().Contain("3 odwołania lub nieobecności w 30 dni").And.Contain("Pakiet się skończył, brak nowego");
    }

    [Fact]
    public void Wellbeing_signals_point_to_a_caring_message()
    {
        var r = ClientChurnRules.Score(Loyal() with { LowSatisfaction = true, WellbeingDropping = true, RecentPain = true });
        r.Score.Should().Be(35);
        r.Top!.Purpose.Should().Be(DraftPurpose.Wellbeing);
    }

    private static SurveyResponseDto Survey(int day, decimal energy, decimal sleep, decimal soreness, decimal satisfaction, bool pain = false) => new()
    {
        Kind = SurveyKind.PostWorkout,
        WorkoutDate = new DateOnly(2026, 10, 1).AddDays(day),
        SubmittedAtUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(day),
        Answers =
        [
            new() { Key = SurveyKeys.Energy, Type = SurveyQuestionType.Scale, Number = energy, Max = 5 },
            new() { Key = SurveyKeys.Sleep, Type = SurveyQuestionType.Scale, Number = sleep, Max = 5 },
            new() { Key = SurveyKeys.Soreness, Type = SurveyQuestionType.Scale, Number = soreness, Max = 5 },
            new() { Key = SurveyKeys.Satisfaction, Type = SurveyQuestionType.Scale, Number = satisfaction, Max = 5 },
            new() { Key = SurveyKeys.Pain, Type = SurveyQuestionType.YesNo, Bool = pain },
        ]
    };

    [Fact]
    public void Wellbeing_index_averages_answers_with_inverted_soreness() =>
        WellbeingTrend.Score(Survey(0, 4, 4, 2, 4)).Should().Be(4m);

    [Fact]
    public void Wellbeing_trend_detects_drop_and_repeated_pain()
    {
        var list = new[]
        {
            Survey(0, 4, 4, 2, 5), Survey(2, 5, 4, 2, 4), Survey(4, 4, 5, 2, 4), Survey(6, 4, 4, 1, 4),
            Survey(8, 2, 2, 4, 3, pain: true), Survey(10, 2, 3, 4, 2), Survey(12, 2, 2, 5, 2, pain: true),
        };
        var t = WellbeingTrend.Compute(list);
        t.Direction.Should().Be(TrendDirection.Down);
        t.IsDropping.Should().BeTrue();
        t.PainInLast5.Should().Be(2);
        t.Text.Should().StartWith("Samopoczucie spada: 4,2 → 2,1").And.Contain("Ból zgłoszony w 2 z ostatnich 5 ankiet.");
    }

    [Fact]
    public void Wellbeing_trend_needs_four_surveys()
    {
        var t = WellbeingTrend.Compute([Survey(0, 4, 4, 2, 4), Survey(1, 2, 2, 4, 2)]);
        t.Direction.Should().Be(TrendDirection.Unknown);
        t.Text.Should().Contain("Za mało ankiet");
    }

    private static readonly HealthPlanRules.PlanExercise[] Plan =
    [
        new("Przysiad ze sztangą (na plecach)", "quadriceps", "glutes,hamstrings,lower back", ExerciseCategory.Strength),
        new("Wykroki z hantlami", "quadriceps", "glutes", ExerciseCategory.Strength),
        new("Wyciskanie sztangi nad głowę", "shoulders", "triceps", ExerciseCategory.Strength),
        new("Martwy ciąg klasyczny", "lower back", "hamstrings,glutes", ExerciseCategory.Powerlifting),
        new("Wiosłowanie hantlem", "middle back", "lats,biceps", ExerciseCategory.Strength),
    ];

    [Fact]
    public void Knee_pain_flags_squats_and_lunges()
    {
        var flags = HealthPlanRules.Check([new("Ankieta zdrowotna", "Ból kolana po bieganiu, 2025")], false, false, Plan);
        flags.Should().ContainSingle();
        flags[0].Area.Should().Be("Kolano");
        flags[0].Exercises.Should().BeEquivalentTo(["Przysiad ze sztangą (na plecach)", "Wykroki z hantlami"]);
        flags[0].Advice.Should().StartWith("Sprawdź");
    }

    [Fact]
    public void Lower_back_and_shoulder_notes_map_to_their_exercises()
    {
        var flags = HealthPlanRules.Check(
            [new("Ankieta zdrowotna", "przepuklina L5-S1"), new("Po treningu 12.10", "bark prawy")], false, false, Plan);
        flags.Select(f => f.Area).Should().BeEquivalentTo(["Kręgosłup / dolny odcinek pleców", "Bark"]);
        flags.First(f => f.Area == "Bark").Exercises.Should().Contain("Wyciskanie sztangi nad głowę");
        flags.First(f => f.Area == "Bark").Source.Should().Be("Po treningu 12.10");
    }

    [Fact]
    public void Unrelated_words_do_not_trigger_flags()
    {
        HealthPlanRules.Check([new("Ankieta zdrowotna", "Alergia na orzechy, stopniowo wracam do formy")], false, false, Plan)
            .Should().BeEmpty();
    }

    [Fact]
    public void Pregnancy_and_heart_answers_flag_relevant_exercises()
    {
        var flags = HealthPlanRules.Check([], pregnancy: false, heart: true, Plan);
        flags.Should().ContainSingle(f => f.Area == "Serce / ciśnienie (PAR-Q)")
            .Which.Exercises.Should().Contain("Martwy ciąg klasyczny");
    }

    [Fact]
    public void Empty_plan_gives_no_flags() =>
        HealthPlanRules.Check([new("Ankieta zdrowotna", "ból kolana")], true, true, []).Should().BeEmpty();
}
