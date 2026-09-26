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
    public async Task Uses_Full_Polish_Translation_For_Name_And_Description()
    {
        var (_, db) = TestDb.CreateFresh();
        await DbInitializer.SeedExerciseCatalogAsync(db);

        // Pozycja spoza starej listy nazw — nazwa i opis z pełnego tłumaczenia (exercise-pl.json).
        var cocoons = await db.Exercises.SingleAsync(e => e.SourceKey == "Cocoons");
        cocoons.NamePl.Should().StartWith("Kokony");
        cocoons.NameEn.Should().Be("Cocoons");
        cocoons.DescriptionPl.Should().NotBeNullOrWhiteSpace();
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

    [Fact]
    public async Task Fills_Polish_Description_And_Name_For_Already_Seeded_Rows()
    {
        var (_, db) = TestDb.CreateFresh();
        await DbInitializer.SeedExerciseCatalogAsync(db);

        // Symulacja starej bazy: brak opisu PL i angielska nazwa.
        var sitUp = await db.Exercises.SingleAsync(e => e.SourceKey == "3_4_Sit-Up");
        sitUp.DescriptionPl = null;
        sitUp.NamePl = sitUp.NameEn;
        // Nazwa zmieniona ręcznie nie może zostać nadpisana.
        var roller = await db.Exercises.SingleAsync(e => e.SourceKey == "Ab_Roller");
        roller.NamePl = "Moja nazwa";
        await db.SaveChangesAsync();

        await DbInitializer.SeedExerciseCatalogAsync(db);

        sitUp = await db.Exercises.SingleAsync(e => e.SourceKey == "3_4_Sit-Up");
        sitUp.NamePl.Should().Be("Brzuszki 3/4");
        sitUp.DescriptionPl.Should().Contain("\n").And.Contain("kolana");
        (await db.Exercises.SingleAsync(e => e.SourceKey == "Ab_Roller")).NamePl.Should().Be("Moja nazwa");
    }

    [Fact]
    public async Task Every_Catalog_Exercise_Has_Polish_Name_And_Description()
    {
        var (_, db) = TestDb.CreateFresh();
        await DbInitializer.SeedExerciseCatalogAsync(db);

        var rows = await db.Exercises.Where(e => e.SourceKey != null).ToListAsync();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.NamePl)
                                       && !string.IsNullOrWhiteSpace(e.DescriptionPl));
        // Pojedyncze nazwy własne (np. „Superman”) mogą się pokrywać — reszta musi być po polsku.
        rows.Count(e => e.NamePl == e.NameEn).Should().BeLessThan(10);
    }
}
