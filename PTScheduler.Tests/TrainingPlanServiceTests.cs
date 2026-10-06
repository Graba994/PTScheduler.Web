using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Kreator planów: zapis grafu plan→dni→ćwiczenia z dopasowaniem po Id
/// (dodanie/edycja/usunięcie), szablon vs przypisanie klienta, duplikacja,
/// usuwanie i pomijanie niewidocznych ćwiczeń.
/// </summary>
public class TrainingPlanServiceTests
{
    private const string T1 = "trainer-1";
    private const string T2 = "trainer-2";

    private static TrainingPlanService Make(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 1, 1, 12, 0, 0)));

    private static async Task SeedAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
    {
        await using var db = f.CreateDbContext();
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "Kowalski", TrainerUserId = T1 });
        db.Exercises.AddRange(
            new Exercise { Id = 1, Visibility = ExerciseVisibility.Public, NamePl = "Przysiad", NameEn = "Squat", PrimaryMuscles = "quadriceps", ImageUrls = "https://x/s0.jpg" },
            new Exercise { Id = 2, Visibility = ExerciseVisibility.Public, NamePl = "Wyciskanie", NameEn = "Bench", PrimaryMuscles = "chest" },
            new Exercise { Id = 3, Visibility = ExerciseVisibility.Public, NamePl = "Martwy ciąg", NameEn = "Deadlift", PrimaryMuscles = "hamstrings" }
        );
        await db.SaveChangesAsync();
    }

    private static PlanEditDto NewPlan() => new()
    {
        Name = "Plan A",
        IsTemplate = false,
        ClientId = 1,
        Days =
        [
            new PlanDayEditDto
            {
                Label = "Dzień A",
                Exercises =
                [
                    new PlanExerciseEditDto { ExerciseId = 1, Sets = 3, Reps = "8-12", TargetWeightKg = 60m },
                    new PlanExerciseEditDto { ExerciseId = 2, Sets = 4, Reps = "6" }
                ]
            }
        ]
    };

    [Fact]
    public async Task Create_Then_Load_Roundtrips_Structure()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        var loaded = await svc.GetForEditAsync(T1, id);

        loaded.Should().NotBeNull();
        loaded!.ClientId.Should().Be(1);
        loaded.Days.Should().ContainSingle();
        loaded.Days[0].Label.Should().Be("Dzień A");
        loaded.Days[0].Exercises.Select(e => e.ExerciseId).Should().ContainInOrder(1, 2);
        loaded.Days[0].Exercises[0].Order.Should().Be(0);
        loaded.Days[0].Exercises[1].Order.Should().Be(1);
        loaded.Days[0].Exercises[0].ExerciseNamePl.Should().Be("Przysiad");
        loaded.Days[0].Exercises[0].ThumbnailUrl.Should().Be("https://x/s0.jpg");
    }

    [Fact]
    public async Task Update_Diffs_Days_And_Exercises_Keeping_Ids()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        var dto = await svc.GetForEditAsync(T1, id);
        var keptId = dto!.Days[0].Exercises[0].Id;

        // Usuń drugie ćwiczenie, dodaj nowe (id 3), dołóż drugi dzień.
        dto.Days[0].Exercises.RemoveAt(1);
        dto.Days[0].Exercises.Add(new PlanExerciseEditDto { ExerciseId = 3, Sets = 5 });
        dto.Days.Add(new PlanDayEditDto { Label = "Dzień B", Exercises = [new PlanExerciseEditDto { ExerciseId = 2, Sets = 3 }] });

        await svc.SavePlanAsync(T1, dto);
        var reloaded = await svc.GetForEditAsync(T1, id);

        reloaded!.Days.Should().HaveCount(2);
        reloaded.Days[0].Exercises.Select(e => e.ExerciseId).Should().ContainInOrder(1, 3);
        reloaded.Days[0].Exercises[0].Id.Should().Be(keptId); // istniejący wiersz zachowany
        reloaded.Days[1].Label.Should().Be("Dzień B");
    }

    [Fact]
    public async Task Template_Forces_Null_Client()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var dto = NewPlan();
        dto.IsTemplate = true;
        dto.ClientId = 1;
        var id = await svc.SavePlanAsync(T1, dto);

        var loaded = await svc.GetForEditAsync(T1, id);
        loaded!.IsTemplate.Should().BeTrue();
        loaded.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_Exercise_Is_Skipped()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var dto = NewPlan();
        dto.Days[0].Exercises.Add(new PlanExerciseEditDto { ExerciseId = 999, Sets = 3 }); // nie istnieje

        var id = await svc.SavePlanAsync(T1, dto);
        var loaded = await svc.GetForEditAsync(T1, id);
        loaded!.Days[0].Exercises.Select(e => e.ExerciseId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task Duplicate_Copies_Structure_Without_Client()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        var copyId = await svc.DuplicateAsync(T1, id, asTemplate: true);

        copyId.Should().NotBe(id);
        var copy = await svc.GetForEditAsync(T1, copyId);
        copy!.Name.Should().Contain("kopia");
        copy.IsTemplate.Should().BeTrue();
        copy.ClientId.Should().BeNull();
        copy.Days[0].Exercises.Select(e => e.ExerciseId).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task Delete_Removes_Plan()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        await svc.DeletePlanAsync(T1, id);
        (await svc.GetForEditAsync(T1, id)).Should().BeNull();
    }

    [Fact]
    public async Task GetPlans_Returns_Counts_And_Respects_Ownership()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());

        var mine = await svc.GetPlansAsync(T1);
        mine.Should().ContainSingle();
        mine[0].DayCount.Should().Be(1);
        mine[0].ExerciseCount.Should().Be(2);
        mine[0].ClientName.Should().Be("Jan Kowalski");

        (await svc.GetPlansAsync(T2)).Should().BeEmpty();
        (await svc.GetForEditAsync(T2, id)).Should().BeNull(); // nie widać cudzego
    }

    [Fact]
    public async Task Save_Requires_Name()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var dto = NewPlan();
        dto.Name = "  ";
        var act = async () => await svc.SavePlanAsync(T1, dto);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
