using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Statystyki i dziennik (Faza 5): objętość w czasie i per partia, rekordy,
/// dziennik oraz aktywność podopiecznych.
/// </summary>
public class WorkoutStatsTests
{
    private const string TRAINER = "trainer-1";

    private static WorkoutLogService Make(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 6, 10, 12, 0, 0)));

    private static async Task SeedAndLogAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
    {
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 1, ApplicationUserId = "cu", FirstName = "Anna", LastName = "Nowak", TrainerUserId = TRAINER });
            db.Exercises.AddRange(
                new Exercise { Id = 1, Visibility = ExerciseVisibility.Public, NamePl = "Wyciskanie", NameEn = "Bench", PrimaryMuscles = "chest" },
                new Exercise { Id = 2, Visibility = ExerciseVisibility.Public, NamePl = "Przysiad", NameEn = "Squat", PrimaryMuscles = "quadriceps" }
            );
            await db.SaveChangesAsync();
        }

        var svc = Make(f);
        await svc.LogWorkoutAsync(1, new LogWorkoutDto
        {
            Date = new DateOnly(2026, 6, 1),
            Exercises = [ new LogExerciseDto { ExerciseId = 1, Sets = [ new() { Reps = 10, WeightKg = 60m } ] } ]
        });
        await svc.LogWorkoutAsync(1, new LogWorkoutDto
        {
            Date = new DateOnly(2026, 6, 8),
            Exercises =
            [
                new LogExerciseDto { ExerciseId = 1, Sets = [ new() { Reps = 8, WeightKg = 70m } ] },
                new LogExerciseDto { ExerciseId = 2, Sets = [ new() { Reps = 12, WeightKg = 100m } ] }
            ]
        });
    }

    [Fact]
    public async Task VolumeOverTime_Points_Ordered_With_Totals()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAndLogAsync(f);

        var pts = await Make(f).GetVolumeOverTimeAsync(1);

        pts.Select(p => p.Date).Should().ContainInOrder(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 8));
        pts.Single(p => p.Date == new DateOnly(2026, 6, 1)).Volume.Should().Be(600m);
        pts.Single(p => p.Date == new DateOnly(2026, 6, 8)).Volume.Should().Be(8 * 70m + 12 * 100m); // 1760
    }

    [Fact]
    public async Task VolumeByMuscle_Aggregates_And_Sorts_Desc()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAndLogAsync(f);

        var byMuscle = await Make(f).GetVolumeByMuscleAsync(1);

        byMuscle.Single(m => m.Muscle == MuscleGroup.Chest).Volume.Should().Be(600m + 560m); // 1160
        byMuscle.Single(m => m.Muscle == MuscleGroup.Quadriceps).Volume.Should().Be(1200m);
        byMuscle.First().Muscle.Should().Be(MuscleGroup.Quadriceps); // największa objętość na górze
    }

    [Fact]
    public async Task PersonalRecords_Max_Weight_And_Best_Set()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAndLogAsync(f);

        var prs = await Make(f).GetPersonalRecordsAsync(1);

        var bench = prs.Single(r => r.ExerciseId == 1);
        bench.MaxWeight.Should().Be(70m);
        bench.RepsAtMax.Should().Be(8);
        bench.BestSetVolume.Should().Be(600m); // 10×60 > 8×70

        prs.First().ExerciseId.Should().Be(2); // przysiad 100 kg = najwyższy rekord
    }

    [Fact]
    public async Task Journal_Days_Newest_First_With_Sets()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAndLogAsync(f);

        var journal = await Make(f).GetJournalAsync(1);

        journal.Should().HaveCount(2);
        journal[0].Date.Should().Be(new DateOnly(2026, 6, 8));
        journal[0].Exercises.Should().HaveCount(2);
        journal[0].SetCount.Should().Be(2);
        journal[1].Date.Should().Be(new DateOnly(2026, 6, 1));
        journal[1].Exercises[0].Sets.Should().ContainSingle();
    }

    [Fact]
    public async Task ClientsActivity_Aggregates_For_Trainer()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAndLogAsync(f);

        var act = await Make(f).GetClientsActivityAsync(TRAINER);

        act.Should().ContainSingle();
        act[0].ClientName.Should().Be("Anna Nowak");
        act[0].LastWorkout.Should().Be(new DateOnly(2026, 6, 8));
        act[0].WorkoutsInWindow.Should().Be(2);
        act[0].VolumeInWindow.Should().Be(600m + 1760m); // 2360

        (await Make(f).GetClientsActivityAsync("inny-trener")).Should().BeEmpty();
    }
}
