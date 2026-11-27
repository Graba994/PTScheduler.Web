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
/// Katalog ćwiczeń: widoczność (baza + moje), filtry, ulubione, ostatnio
/// używane oraz CRUD własnych ćwiczeń z ochroną przed usunięciem używanego.
/// </summary>
public class ExerciseCatalogServiceTests
{
    private const string T1 = "trainer-1";
    private const string T2 = "trainer-2";

    private static ExerciseCatalogService Make(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 1, 1, 12, 0, 0)));

    private static async Task SeedAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
    {
        await using var db = f.CreateDbContext();
        db.Exercises.AddRange(
            new Exercise { Id = 1, Visibility = ExerciseVisibility.Public, OwnerTrainerUserId = null,
                NamePl = "Przysiad ze sztangą", NameEn = "Barbell Squat", PrimaryMuscles = "quadriceps,glutes",
                Category = ExerciseCategory.Strength, Level = ExerciseLevel.Intermediate, Equipment = "barbell",
                SourceKey = "Barbell_Squat", ImageUrls = "https://x/squat/0.jpg,https://x/squat/1.jpg" },
            new Exercise { Id = 2, Visibility = ExerciseVisibility.Public, OwnerTrainerUserId = null,
                NamePl = "Wyciskanie na ławce", NameEn = "Bench Press", PrimaryMuscles = "chest",
                Category = ExerciseCategory.Strength, Level = ExerciseLevel.Beginner, Equipment = "barbell",
                SourceKey = "Bench_Press" },
            new Exercise { Id = 3, Visibility = ExerciseVisibility.Mine, OwnerTrainerUserId = T1,
                NamePl = "Autorskie ćwiczenie T1", NameEn = "T1 Custom", PrimaryMuscles = "abdominals",
                Category = ExerciseCategory.Strength, Level = ExerciseLevel.Beginner },
            new Exercise { Id = 4, Visibility = ExerciseVisibility.Mine, OwnerTrainerUserId = T2,
                NamePl = "Autorskie ćwiczenie T2", NameEn = "T2 Custom", PrimaryMuscles = "biceps",
                Category = ExerciseCategory.Strength, Level = ExerciseLevel.Beginner }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_All_Returns_Public_Plus_Own_Only()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);

        var items = await Make(f).SearchAsync(T1, new ExerciseFilterDto { Scope = ExerciseCatalogScope.All });

        items.Select(i => i.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 }); // nie 4 (własne T2)
        items.Single(i => i.Id == 3).IsMine.Should().BeTrue();
        items.Single(i => i.Id == 1).IsMine.Should().BeFalse();
        items.Single(i => i.Id == 1).ThumbnailUrl.Should().Be("https://x/squat/0.jpg");
    }

    [Fact]
    public async Task Scope_Mine_And_Public_Filter_Correctly()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        (await svc.SearchAsync(T1, new ExerciseFilterDto { Scope = ExerciseCatalogScope.Mine }))
            .Select(i => i.Id).Should().BeEquivalentTo(new[] { 3 });
        (await svc.SearchAsync(T1, new ExerciseFilterDto { Scope = ExerciseCatalogScope.Public }))
            .Select(i => i.Id).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task Search_Text_And_Muscle_And_Category_Filters()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        (await svc.SearchAsync(T1, new ExerciseFilterDto { Search = "bench" }))
            .Select(i => i.Id).Should().BeEquivalentTo(new[] { 2 });
        (await svc.SearchAsync(T1, new ExerciseFilterDto { Muscle = MuscleGroup.Chest }))
            .Select(i => i.Id).Should().BeEquivalentTo(new[] { 2 });
        (await svc.SearchAsync(T1, new ExerciseFilterDto { Muscle = MuscleGroup.Glutes }))
            .Select(i => i.Id).Should().BeEquivalentTo(new[] { 1 });
    }

    [Fact]
    public async Task ToggleFavorite_Then_Favorites_Scope()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        (await svc.ToggleFavoriteAsync(T1, 1)).Should().BeTrue();
        var favs = await svc.SearchAsync(T1, new ExerciseFilterDto { Scope = ExerciseCatalogScope.Favorites });
        favs.Select(i => i.Id).Should().BeEquivalentTo(new[] { 1 });

        (await svc.ToggleFavoriteAsync(T1, 1)).Should().BeFalse();
        (await svc.SearchAsync(T1, new ExerciseFilterDto { Scope = ExerciseCatalogScope.Favorites })).Should().BeEmpty();
    }

    [Fact]
    public async Task MarkUsed_Then_Recent_Scope_Ordered()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        await svc.MarkUsedAsync(T1, 2);
        var recent = await svc.SearchAsync(T1, new ExerciseFilterDto { Scope = ExerciseCatalogScope.Recent });
        recent.Select(i => i.Id).Should().BeEquivalentTo(new[] { 2 });
    }

    [Fact]
    public async Task Create_Update_Delete_Own_Exercise()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.CreateAsync(T1, new SaveExerciseDto
        {
            NamePl = "Mój wyciąg", PrimaryMuscles = [MuscleGroup.Lats],
            Category = ExerciseCategory.Strength, Level = ExerciseLevel.Beginner, Equipment = "cable"
        });

        var detail = await svc.GetAsync(T1, id);
        detail!.IsMine.Should().BeTrue();
        detail.NameEn.Should().Be("Mój wyciąg"); // fallback z PL gdy EN puste

        await svc.UpdateAsync(T1, new SaveExerciseDto { Id = id, NamePl = "Mój wyciąg v2", PrimaryMuscles = [MuscleGroup.Lats] });
        (await svc.GetAsync(T1, id))!.NamePl.Should().Be("Mój wyciąg v2");

        await svc.DeleteAsync(T1, id);
        (await svc.GetAsync(T1, id)).Should().BeNull();
    }

    [Fact]
    public async Task Cannot_Edit_Or_Delete_Others_Exercise()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var act = async () => await svc.UpdateAsync(T1, new SaveExerciseDto { Id = 4, NamePl = "hack" });
        await act.Should().ThrowAsync<InvalidOperationException>();

        var del = async () => await svc.DeleteAsync(T1, 4);
        await del.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Cannot_Delete_Exercise_Used_In_Plan()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.CreateAsync(T1, new SaveExerciseDto { NamePl = "Używane", PrimaryMuscles = [MuscleGroup.Chest] });

        await using (var db = f.CreateDbContext())
        {
            db.TrainingPlans.Add(new TrainingPlan { Id = 100, TrainerUserId = T1, Name = "Plan" });
            db.PlanDays.Add(new PlanDay { Id = 100, PlanId = 100, Order = 1, Label = "A" });
            db.PlanExercises.Add(new PlanExercise { Id = 100, PlanDayId = 100, ExerciseId = id, Order = 1, Sets = 3 });
            await db.SaveChangesAsync();
        }

        var act = async () => await svc.DeleteAsync(T1, id);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*używane*");
    }
}
