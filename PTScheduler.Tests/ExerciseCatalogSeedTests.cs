using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Seed katalogu ćwiczeń z Free Exercise DB (zasób osadzony): kompletność,
/// idempotencja, nadpisania nazw PL i poprawność mapowania partii/obrazów.
/// </summary>
public class ExerciseCatalogSeedTests
{
    [Fact]
    public async Task Seeds_Full_Catalog_As_Public_Base_Exercises()
    {
        var (_, db) = TestDb.CreateFresh();

        await DbInitializer.SeedExerciseCatalogAsync(db);

        var all = await db.Exercises.ToListAsync();
        all.Count.Should().BeGreaterThan(800); // Free Exercise DB ma ~876 pozycji
        all.Should().OnlyContain(e =>
            e.Visibility == ExerciseVisibility.Public &&
            e.OwnerTrainerUserId == null &&
            e.SourceKey != null &&
            e.NameEn != "" &&
            e.NamePl != "");
    }

    [Fact]
    public async Task Is_Idempotent_On_Second_Run()
    {
        var (_, db) = TestDb.CreateFresh();

        await DbInitializer.SeedExerciseCatalogAsync(db);
        var afterFirst = await db.Exercises.CountAsync();

        await DbInitializer.SeedExerciseCatalogAsync(db);
        var afterSecond = await db.Exercises.CountAsync();

        afterSecond.Should().Be(afterFirst);
    }

    [Fact]
    public async Task Applies_Polish_Name_Override_When_Present()
    {
        var (_, db) = TestDb.CreateFresh();
        await DbInitializer.SeedExerciseCatalogAsync(db);

        var squat = await db.Exercises.SingleAsync(e => e.SourceKey == "Barbell_Squat");
        squat.NamePl.Should().Be("Przysiad ze sztangą (na plecach)");
        squat.NameEn.Should().Be("Barbell Squat");
    }

    [Fact]
    public async Task Falls_Back_To_English_Name_When_No_Override()
    {
        var (_, db) = TestDb.CreateFresh();
        await DbInitializer.SeedExerciseCatalogAsync(db);

        // Pozycja spoza kuratorowanej listy PL — NamePl == NameEn.
        var any = await db.Exercises.FirstAsync(e => e.SourceKey == "Cocoons");
        any.NamePl.Should().Be(any.NameEn);
    }

    [Fact]
    public async Task Maps_Muscles_And_Images_Usably()
    {
        var (_, db) = TestDb.CreateFresh();
        await DbInitializer.SeedExerciseCatalogAsync(db);

        var bench = await db.Exercises.SingleAsync(e => e.SourceKey == "Barbell_Bench_Press_-_Medium_Grip");

        Muscles.Parse(bench.PrimaryMuscles).Should().Contain(MuscleGroup.Chest);
        bench.ImageUrls.Should().Contain("Barbell_Bench_Press_-_Medium_Grip/0.jpg");
        bench.ImageUrls.Should().StartWith("http");
        bench.Category.Should().Be(ExerciseCategory.Strength);
    }
}
