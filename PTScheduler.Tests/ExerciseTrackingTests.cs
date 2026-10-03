using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Rodzaj pomiaru ćwiczenia (ciężar×powtórzenia, czas, dystans…) — automatyczne
/// przypisanie dla katalogu i zapis serii z czasem/dystansem.
/// </summary>
public class ExerciseTrackingTests
{
    [Theory]
    [InlineData(ExerciseCategory.Cardio, "machine", "Running, Treadmill", ExerciseTracking.DistanceTime)]
    [InlineData(ExerciseCategory.Cardio, "other", "Rope Jumping", ExerciseTracking.Time)]
    [InlineData(ExerciseCategory.Cardio, "machine", "Elliptical Trainer", ExerciseTracking.Time)]
    [InlineData(ExerciseCategory.Stretching, "body only", "Hamstring Stretch", ExerciseTracking.Time)]
    [InlineData(ExerciseCategory.Strongman, "other", "Farmer's Walk", ExerciseTracking.WeightDistance)]
    [InlineData(ExerciseCategory.Strongman, "other", "Atlas Stones", ExerciseTracking.WeightReps)]
    [InlineData(ExerciseCategory.Strength, "body only", "Plank", ExerciseTracking.Time)]
    [InlineData(ExerciseCategory.Strength, "body only", "Pushups", ExerciseTracking.Reps)]
    [InlineData(ExerciseCategory.Strength, "barbell", "Barbell Squat", ExerciseTracking.WeightReps)]
    [InlineData(ExerciseCategory.Plyometrics, "body only", "Box Jump", ExerciseTracking.Reps)]
    [InlineData(ExerciseCategory.Plyometrics, "medicine ball", "Medicine Ball Slam", ExerciseTracking.WeightReps)]
    public void Guess_Picks_Sensible_Tracking(ExerciseCategory category, string equipment, string nameEn, ExerciseTracking expected) =>
        ExerciseTrackingRules.Guess(category, equipment, nameEn).Should().Be(expected);

    [Fact]
    public void Guess_Recognises_Polish_Names() =>
        ExerciseTrackingRules.Guess(ExerciseCategory.Other, "", null, "Deska bokiem").Should().Be(ExerciseTracking.Time);

    [Fact]
    public void Running_Has_No_Weight_Or_Reps()
    {
        ExerciseTrackingRules.UsesReps(ExerciseTracking.DistanceTime).Should().BeFalse();
        ExerciseTrackingRules.UsesWeight(ExerciseTracking.DistanceTime).Should().BeFalse();
        ExerciseTrackingRules.UsesTime(ExerciseTracking.DistanceTime).Should().BeTrue();
        ExerciseTrackingRules.UsesDistance(ExerciseTracking.DistanceTime).Should().BeTrue();
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(90, "1:30")]
    [InlineData(1805, "30:05")]
    public void FormatDuration(int seconds, string expected) =>
        ExerciseTrackingRules.FormatDuration(seconds).Should().Be(expected);

    [Theory]
    [InlineData(800, "800 m")]
    [InlineData(5200, "5,2 km")]
    public void FormatDistance(decimal meters, string expected) =>
        ExerciseTrackingRules.FormatDistance(meters).Should().Be(expected);

    [Fact]
    public async Task Time_And_Distance_Sets_Are_Saved_And_Shown_In_Journal()
    {
        var (f, _) = TestDb.CreateFresh();
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", LastName = "Nowak", TrainerUserId = "t" });
            db.Exercises.AddRange(
                new Exercise { Id = 1, Visibility = ExerciseVisibility.Public, NamePl = "Bieg", NameEn = "Running", Tracking = ExerciseTracking.DistanceTime },
                new Exercise { Id = 2, Visibility = ExerciseVisibility.Public, NamePl = "Deska", NameEn = "Plank", Tracking = ExerciseTracking.Time });
            await db.SaveChangesAsync();
        }

        var log = new WorkoutLogService(f, TestClock.AtWallClock(new DateTime(2026, 6, 1, 18, 0, 0)));
        var saved = await log.LogWorkoutAsync(1, new LogWorkoutDto
        {
            Date = new DateOnly(2026, 6, 1),
            Exercises =
            [
                new LogExerciseDto { ExerciseId = 1, Sets = [new() { SetNumber = 1, DurationSeconds = 1800, DistanceMeters = 5000m }] },
                new LogExerciseDto { ExerciseId = 2, Sets = [new() { SetNumber = 1, DurationSeconds = 60 }, new() { SetNumber = 2 }] }
            ]
        });

        saved.Should().Be(2); // pusta seria deski pominięta
        await using var check = f.CreateDbContext();
        var run = await check.WorkoutSetLogs.SingleAsync(s => s.WorkoutLog!.ExerciseId == 1);
        run.DurationSeconds.Should().Be(1800);
        run.DistanceMeters.Should().Be(5000m);
        run.Reps.Should().Be(0);
    }
}
