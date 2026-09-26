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
/// Dziennik treningowy klienta: zapis wykonania (WorkoutLog + serie),
/// pomijanie pustych/niepoprawnych, podsumowania historii oraz dostęp klienta
/// do przypisanych planów.
/// </summary>
public class WorkoutLoggingTests
{
    private const string TRAINER = "trainer-1";

    private static WorkoutLogService MakeLog(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 6, 1, 18, 0, 0)));

    private static TrainingPlanService MakePlan(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 6, 1, 18, 0, 0)));

    private static async Task SeedAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
    {
        await using var db = f.CreateDbContext();
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", LastName = "Nowak", TrainerUserId = TRAINER });
        db.Exercises.AddRange(
            new Exercise { Id = 1, Visibility = ExerciseVisibility.Public, NamePl = "Przysiad", NameEn = "Squat", PrimaryMuscles = "quadriceps" },
            new Exercise { Id = 2, Visibility = ExerciseVisibility.Public, NamePl = "Wyciskanie", NameEn = "Bench", PrimaryMuscles = "chest" }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task LogWorkout_Persists_Logs_And_Sets()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);

        var dto = new LogWorkoutDto
        {
            Date = new DateOnly(2026, 6, 1),
            Exercises =
            [
                new LogExerciseDto { ExerciseId = 1, Sets = [ new() { SetNumber = 1, Reps = 10, WeightKg = 60m }, new() { SetNumber = 2, Reps = 8, WeightKg = 65m } ] },
                new LogExerciseDto { ExerciseId = 2, Sets = [ new() { SetNumber = 1, Reps = 12, WeightKg = 40m } ] }
            ]
        };

        var saved = await MakeLog(f).LogWorkoutAsync(1, dto);
        saved.Should().Be(3);

        await using var db = f.CreateDbContext();
        (await db.WorkoutLogs.CountAsync()).Should().Be(2);
        (await db.WorkoutSetLogs.CountAsync()).Should().Be(3);
        var log1 = await db.WorkoutLogs.Include(w => w.Sets).FirstAsync(w => w.ExerciseId == 1);
        log1.WorkoutDate.Should().Be(new DateOnly(2026, 6, 1));
        log1.Sets.Should().HaveCount(2);
    }

    [Fact]
    public async Task LogWorkout_Skips_Empty_Sets_And_Unknown_Exercises()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);

        var dto = new LogWorkoutDto
        {
            Date = new DateOnly(2026, 6, 1),
            Exercises =
            [
                new LogExerciseDto { ExerciseId = 1, Sets = [ new() { SetNumber = 1, Reps = 0, WeightKg = 0m } ] }, // pusta
                new LogExerciseDto { ExerciseId = 999, Sets = [ new() { SetNumber = 1, Reps = 10, WeightKg = 50m } ] } // nie istnieje
            ]
        };

        var saved = await MakeLog(f).LogWorkoutAsync(1, dto);
        saved.Should().Be(0);

        await using var db = f.CreateDbContext();
        (await db.WorkoutLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task GetRecent_Summarizes_By_Date_With_Volume()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = MakeLog(f);

        await svc.LogWorkoutAsync(1, new LogWorkoutDto
        {
            Date = new DateOnly(2026, 6, 1),
            Exercises =
            [
                new LogExerciseDto { ExerciseId = 1, Sets = [ new() { Reps = 10, WeightKg = 60m }, new() { Reps = 8, WeightKg = 65m } ] },
                new LogExerciseDto { ExerciseId = 2, Sets = [ new() { Reps = 12, WeightKg = 40m } ] }
            ]
        });

        var recent = await svc.GetRecentForClientAsync(1);
        recent.Should().ContainSingle();
        recent[0].Date.Should().Be(new DateOnly(2026, 6, 1));
        recent[0].ExerciseCount.Should().Be(2);
        recent[0].SetCount.Should().Be(3);
        recent[0].TotalVolume.Should().Be(10 * 60m + 8 * 65m + 12 * 40m); // 1600
    }

    [Fact]
    public async Task Client_Sees_Only_Assigned_Plans_For_Workout()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var plans = MakePlan(f);

        var id = await plans.SavePlanAsync(TRAINER, new PlanEditDto
        {
            Name = "Plan Anny",
            IsTemplate = false,
            ClientId = 1,
            Days = [ new PlanDayEditDto { Label = "Dzień A", Exercises = [ new PlanExerciseEditDto { ExerciseId = 1, Sets = 3, Reps = "10" } ] } ]
        });

        (await plans.GetClientPlansAsync(1)).Select(p => p.Id).Should().Contain(id);

        var forWorkout = await plans.GetForWorkoutAsync(1, id);
        forWorkout.Should().NotBeNull();
        forWorkout!.Days.Should().ContainSingle();
        forWorkout.Days[0].Exercises[0].ExerciseNamePl.Should().Be("Przysiad");

        (await plans.GetForWorkoutAsync(2, id)).Should().BeNull(); // inny klient
        (await plans.GetClientPlansAsync(2)).Should().BeEmpty();
    }
}
