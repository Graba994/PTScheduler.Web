using FluentAssertions;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using Xunit;
using G = PTScheduler.Domain.Rules.PlanGeneratorRules;

namespace PTScheduler.Tests;

public class PlanGeneratorRulesTests
{
    private static G.Candidate Ex(int id, string name, string muscles, string equipment = "barbell", string? force = "push",
        string mechanic = "compound", ExerciseLevel level = ExerciseLevel.Beginner) =>
        new(id, name, muscles, "", ExerciseCategory.Strength, equipment, force, mechanic, level, null);

    private static readonly List<G.Candidate> Catalog =
    [
        Ex(1, "Przysiad ze sztangą", "quadriceps"),
        Ex(2, "Przysiad goblet", "quadriceps", "dumbbell"),
        Ex(3, "Martwy ciąg rumuński", "hamstrings", force: "pull"),
        Ex(4, "Wyciskanie sztangi leżąc", "chest"),
        Ex(5, "Pompki", "chest", "body only"),
        Ex(6, "Wiosłowanie sztangą", "middle back", force: "pull"),
        Ex(7, "Podciąganie", "lats", "body only", "pull"),
        Ex(8, "Wyciskanie hantli nad głowę", "shoulders", "dumbbell"),
        Ex(9, "Uginanie ramion z hantlami", "biceps", "dumbbell", "pull", "isolation"),
        Ex(10, "Prostowanie ramion na wyciągu", "triceps", "cable", "push", "isolation"),
        Ex(11, "Plank", "abdominals", "body only", null, "isolation"),
        Ex(12, "Wykroki", "quadriceps", "dumbbell", mechanic: "compound"),
        Ex(13, "Hip thrust", "glutes"),
        Ex(14, "Wspięcia na palce", "calves", "machine", null, "isolation"),
        Ex(15, "Wyciskanie hantli leżąc", "chest", "dumbbell"),
        Ex(16, "Ściąganie drążka", "lats", "cable", "pull"),
        Ex(17, "Unoszenie hantli bokiem", "shoulders", "dumbbell", null, "isolation"),
        Ex(18, "Uginanie nóg na maszynie", "hamstrings", "machine", "pull", "isolation"),
    ];

    private static readonly Dictionary<int, G.History> NoHistory = [];

    [Theory]
    [InlineData("Chcę schudnąć 8 kg do wakacji", G.Focus.FatLoss)]
    [InlineData("Lepsza kondycja na rowerze", G.Focus.Conditioning)]
    [InlineData("Przebiec 10 km bez zatrzymywania", G.Focus.Conditioning)]
    [InlineData("Budowa masy mięśniowej", G.Focus.Hypertrophy)]
    [InlineData("Mocniejszy martwy ciąg, rekord 150", G.Focus.Strength)]
    [InlineData("Zdrowy kręgosłup", G.Focus.General)]
    [InlineData(null, G.Focus.General)]
    public void Focus_comes_from_goal_keywords(string? goal, G.Focus expected) =>
        G.FocusFromGoal(goal).Should().Be(expected);

    [Fact]
    public void Four_days_split_into_upper_and_lower()
    {
        var plan = G.Build(new(4, 60, G.Focus.General, G.Kit.Gym), Catalog, NoHistory, _ => null);

        plan.Days.Select(d => d.Label).Should().Equal("Góra A", "Dół A", "Góra B", "Dół B");
        plan.Days.Should().OnlyContain(d => d.Exercises.Count > 0 && d.Exercises.Count <= G.ExercisesFor(60));
        plan.Days[1].Exercises.Should().NotContain(e => e.Name.Contains("Wyciskanie sztangi"));
    }

    [Fact]
    public void Three_heavy_days_become_push_pull_legs_and_light_ones_full_body()
    {
        G.Build(new(3, 60, G.Focus.Strength, G.Kit.Gym), Catalog, NoHistory, _ => null).Days
            .Select(d => d.Label).Should().Equal("Push — klatka, barki, triceps", "Pull — plecy, biceps", "Nogi");
        G.Build(new(3, 45, G.Focus.FatLoss, G.Kit.Gym), Catalog, NoHistory, _ => null).Days
            .Select(d => d.Label).Should().Equal("Całe ciało A", "Całe ciało B", "Całe ciało C");
    }

    [Fact]
    public void Exercises_conflicting_with_health_are_skipped_and_named_in_notes()
    {
        var plan = G.Build(new(2, 60, G.Focus.General, G.Kit.Gym), Catalog, NoHistory,
            c => c.PrimaryMuscles.Contains("quadriceps") ? "Kolano" : null);

        plan.Days.SelectMany(d => d.Exercises).Should().NotContain(e => e.ExerciseId == 1 || e.ExerciseId == 2 || e.ExerciseId == 12);
        plan.Notes.Should().Contain(n => n.StartsWith("Kolano: pominięto"));
    }

    [Fact]
    public void Bodyweight_kit_keeps_out_barbells_and_machines()
    {
        var plan = G.Build(new(2, 45, G.Focus.General, G.Kit.Bodyweight), Catalog, NoHistory, _ => null);
        var ids = plan.Days.SelectMany(d => d.Exercises).Select(e => e.ExerciseId).ToList();

        ids.Should().NotBeEmpty();
        ids.Should().OnlyContain(id => Catalog.Single(c => c.Id == id).Equipment == "body only");
    }

    [Fact]
    public void Familiar_exercise_wins_and_gets_progressed_weight()
    {
        var history = new Dictionary<int, G.History>
        {
            [15] = new(15, 6, 24m, 10, 6.5m, new DateOnly(2026, 10, 10))
        };
        var plan = G.Build(new(1, 60, G.Focus.General, G.Kit.Gym), Catalog, history, _ => null);
        var press = plan.Days[0].Exercises.Single(e => e.ExerciseId is 4 or 15);

        press.ExerciseId.Should().Be(15);
        press.Familiar.Should().BeTrue();
        press.WeightKg.Should().Be(26m); // hantle: +2 kg przy RPE ≤ 7
        press.Why.Should().Contain("ostatnio 24 kg × 10");
        plan.Notes.Should().Contain(n => n.Contains("nowości"));
    }

    [Theory]
    [InlineData(6.5, 10, 62.5)]   // lekko i zakres domknięty → +2,5 kg
    [InlineData(8.0, 10, 60.0)]   // umiarkowanie → bez zmian
    [InlineData(9.5, 10, 52.5)]   // ciężko → ok. −10%
    [InlineData(6.0, 5, 60.0)]    // lekko, ale za mało powtórzeń → bez zmian
    public void Next_weight_follows_rpe(double rpe, int lastReps, double expected)
    {
        var h = new G.History(4, 5, 60m, lastReps, (decimal)rpe, new DateOnly(2026, 10, 1));
        G.NextWeight(h, Catalog.Single(c => c.Id == 4), "8-10", out var how).Should().Be((decimal)expected);
        how.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Short_sessions_have_fewer_exercises() =>
        G.ExercisesFor(30).Should().BeLessThan(G.ExercisesFor(75));
}
