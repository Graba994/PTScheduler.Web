using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

public class ProgressionRulesTests
{
    private static SetResult[] Sets(decimal kg, params int[] reps) => reps.Select(r => new SetResult(r, kg)).ToArray();

    [Theory]
    [InlineData("10", 10, 10)]
    [InlineData("8-12", 8, 12)]
    [InlineData("8 – 12", 8, 12)]
    [InlineData("12-8", 8, 12)]
    public void Parses_Rep_Ranges(string text, int min, int max) =>
        ProgressionRules.ParseReps(text).Should().Be((min, max));

    [Theory]
    [InlineData(null)]
    [InlineData("max")]
    [InlineData("AMRAP")]
    [InlineData("30 s")]
    public void Ignores_Non_Numeric_Reps(string? text) => ProgressionRules.ParseReps(text).Should().BeNull();

    [Fact]
    public void Top_Of_Range_With_Low_Rpe_Suggests_Increase()
    {
        var d = ProgressionRules.Decide(3, "8-12", Sets(40, 12, 12, 13), null, 6, "chest", "compound", "barbell");
        d!.Action.Should().Be(ProgressionAction.Increase);
        (d.FromKg, d.ToKg, d.DeltaKg).Should().Be((40m, 42.5m, 2.5m));
        d.Reason.Should().Contain("RPE 6");
    }

    [Fact]
    public void Heavy_Lower_Body_Compound_Gets_Bigger_Step_And_Light_Weights_Smaller()
    {
        ProgressionRules.Decide(3, "5", Sets(100, 5, 5, 5), null, 7, "quadriceps,glutes", "compound", "barbell")!.DeltaKg.Should().Be(5m);
        ProgressionRules.Decide(3, "12", Sets(10, 12, 12, 12), null, 5, "biceps", "isolation", "cable")!.DeltaKg.Should().Be(1m);
        ProgressionRules.Decide(3, "12", Sets(24, 12, 12, 12), null, 5, "shoulders", "isolation", "dumbbell")!.DeltaKg.Should().Be(2m);
    }

    [Fact]
    public void Hard_Session_Or_Missing_Sets_Does_Not_Increase()
    {
        ProgressionRules.Decide(3, "8-12", Sets(40, 12, 12, 12), null, 8, "chest", null, null).Should().BeNull("RPE 8 — jeszcze za ciężko na podbicie");
        ProgressionRules.Decide(3, "8-12", Sets(40, 12, 12), null, 5, "chest", null, null).Should().BeNull("zrobione 2 z 3 serii");
        ProgressionRules.Decide(3, "8-12", Sets(40, 12, 11, 12), null, 5, "chest", null, null).Should().BeNull("jedna seria poniżej góry zakresu");
    }

    [Fact]
    public void Only_Top_Weight_Sets_Count_As_Working_Sets()
    {
        // Rozgrzewka 20 kg × 5 nie blokuje progresji.
        var sets = Sets(20, 5).Concat(Sets(40, 10, 10, 10)).ToArray();
        ProgressionRules.Decide(3, "10", sets, null, 6, "chest", null, null)!.ToKg.Should().Be(42.5m);
    }

    [Fact]
    public void Missing_Range_At_Very_High_Rpe_Or_Twice_In_A_Row_Suggests_Deload()
    {
        var hard = ProgressionRules.Decide(3, "8-12", Sets(50, 7, 6, 6), null, 9.5m, "chest", "compound", "barbell");
        hard!.Action.Should().Be(ProgressionAction.Deload);
        hard.ToKg.Should().Be(45m);

        ProgressionRules.Decide(3, "8-12", Sets(50, 7, 6, 6), null, 8, "chest", null, null).Should().BeNull("jeden słabszy dzień to jeszcze nie powód");
        var twice = ProgressionRules.Decide(3, "8-12", Sets(50, 7, 6, 6), Sets(50, 7, 7, 6), 8, "chest", null, null);
        twice!.Action.Should().Be(ProgressionAction.Deload);
        twice.Reason.Should().Contain("Drugi trening");
    }

    [Fact]
    public void Bodyweight_And_Unparsable_Reps_Are_Skipped()
    {
        ProgressionRules.Decide(3, "10", Sets(0, 15, 15, 15), null, 4, "chest", null, null).Should().BeNull();
        ProgressionRules.Decide(3, "max", Sets(40, 15, 15, 15), null, 4, "chest", null, null).Should().BeNull();
    }
}

public class ProgressionServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 20);

    private static async Task<IDbContextFactory<ApplicationDbContext>> SeedAsync(decimal? rpe = 6)
    {
        var (f, _) = TestDb.CreateFresh();
        await using var db = f.CreateDbContext();
        db.Clients.Add(new Client { Id = 1, FirstName = "Ola", LastName = "K", TrainerUserId = "t1" });
        db.Exercises.Add(new Exercise { Id = 1, NamePl = "Wyciskanie leżąc", PrimaryMuscles = "chest", Mechanic = "compound", Equipment = "barbell" });
        db.TrainingPlans.Add(new TrainingPlan
        {
            Id = 1, TrainerUserId = "t1", ClientId = 1, Name = "Siła A",
            Days = [new PlanDay { Id = 1, Label = "Dzień 1", Exercises = [new PlanExercise { Id = 1, ExerciseId = 1, Sets = 3, Reps = "8-10", TargetWeightKg = 40 }] }]
        });
        var date = Today.AddDays(-2);
        db.WorkoutLogs.Add(new WorkoutLog
        {
            ClientId = 1, ExerciseId = 1, PlanExerciseId = 1, WorkoutDate = date,
            Sets = [new() { SetNumber = 1, Reps = 10, WeightKg = 40 }, new() { SetNumber = 2, Reps = 10, WeightKg = 40 }, new() { SetNumber = 3, Reps = 11, WeightKg = 40 }]
        });
        if (rpe is decimal r)
            db.SurveyResponses.Add(new SurveyResponse
            {
                Kind = SurveyKind.PostWorkout, ClientId = 1, WorkoutDate = date,
                AnswersJson = SurveyJson.Serialize(new List<SurveyAnswer> { new() { Key = SurveyKeys.Rpe, Number = r } })
            });
        await db.SaveChangesAsync();
        return f;
    }

    private static ProgressionService Make(IDbContextFactory<ApplicationDbContext> f) => new(f, TestClock.AtDate(2026, 9, 20));

    [Fact]
    public async Task Suggests_Increase_From_Log_And_Survey_Rpe()
    {
        var svc = Make(await SeedAsync());
        var s = (await svc.GetSuggestionsAsync("t1", false, 1)).Should().ContainSingle().Subject;
        (s.ExerciseName, s.Action, s.FromKg, s.SuggestedKg, s.Rpe, s.LastRepsText).Should()
            .Be(("Wyciskanie leżąc", ProgressionAction.Increase, 40m, 42.5m, 6m, "10 / 10 / 11"));
        (await svc.GetCountsAsync("t1", false)).Should().Equal(new Dictionary<int, int> { [1] = 1 });
    }

    [Fact]
    public async Task High_Rpe_Gives_No_Suggestion_And_Other_Trainer_Sees_Nothing()
    {
        (await Make(await SeedAsync(rpe: 8)).GetSuggestionsAsync("t1", false, 1)).Should().BeEmpty();

        var svc = Make(await SeedAsync());
        (await svc.GetSuggestionsAsync("t2", false, 1)).Should().BeEmpty();
        (await svc.ApplyAsync("t2", false, 1, 50)).Should().BeFalse();
        (await svc.GetSuggestionsAsync("admin", true, 1)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Apply_Updates_Plan_And_Hides_Suggestion_Until_Next_Workout()
    {
        var f = await SeedAsync();
        var svc = Make(f);
        (await svc.ApplyAsync("t1", false, 1, 42.5m)).Should().BeTrue();

        await using (var db = f.CreateDbContext())
            (await db.PlanExercises.SingleAsync()).TargetWeightKg.Should().Be(42.5m);
        (await svc.GetSuggestionsAsync("t1", false, 1)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dismissed_Suggestion_Returns_After_A_New_Easy_Workout()
    {
        var f = await SeedAsync(rpe: null);
        var svc = Make(f);
        (await svc.GetSuggestionsAsync("t1", false, 1)).Single().Reason.Should().Contain("brak ankiety");
        (await svc.DismissAsync("t1", false, 1)).Should().BeTrue();
        (await svc.GetSuggestionsAsync("t1", false, 1)).Should().BeEmpty();

        await using (var db = f.CreateDbContext())
        {
            db.WorkoutLogs.Add(new WorkoutLog
            {
                ClientId = 1, ExerciseId = 1, PlanExerciseId = 1, WorkoutDate = Today,
                Sets = [new() { SetNumber = 1, Reps = 10, WeightKg = 40 }, new() { SetNumber = 2, Reps = 10, WeightKg = 40 }, new() { SetNumber = 3, Reps = 10, WeightKg = 40 }]
            });
            await db.SaveChangesAsync();
        }
        (await svc.GetSuggestionsAsync("t1", false, 1)).Should().ContainSingle();
    }
}
