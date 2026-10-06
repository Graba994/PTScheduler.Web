using FluentAssertions;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Objętość treningowa pod wykresy: seria = powt. × ciężar, wykonanie = suma
/// serii, agregacja „per partia" przypisuje pełną objętość każdej partii
/// pierwotnej ćwiczenia.
/// </summary>
public class VolumeCalculatorTests
{
    [Fact]
    public void SetVolume_Is_Reps_Times_Weight()
    {
        VolumeCalculator.SetVolume(10, 60m).Should().Be(600m);
    }

    [Fact]
    public void TotalVolume_Sums_Sets()
    {
        var sets = new[]
        {
            new WorkoutSetLog { SetNumber = 1, Reps = 10, WeightKg = 60m },
            new WorkoutSetLog { SetNumber = 2, Reps = 8,  WeightKg = 70m },
            new WorkoutSetLog { SetNumber = 3, Reps = 6,  WeightKg = 80m }
        };
        // 600 + 560 + 480
        VolumeCalculator.TotalVolume(sets).Should().Be(1_640m);
    }

    [Fact]
    public void TotalVolume_Empty_Is_Zero()
    {
        VolumeCalculator.TotalVolume([]).Should().Be(0m);
    }

    [Fact]
    public void VolumeByMuscle_Attributes_Full_Volume_To_Each_Primary_Muscle()
    {
        var workouts = new[]
        {
            ("chest, triceps", 1_000m),  // wyciskanie
            ("chest", 500m),             // rozpiętki
            ("biceps", 300m)             // uginania
        };

        var byMuscle = VolumeCalculator.VolumeByMuscle(workouts);

        byMuscle[MuscleGroup.Chest].Should().Be(1_500m);  // 1000 + 500
        byMuscle[MuscleGroup.Triceps].Should().Be(1_000m);
        byMuscle[MuscleGroup.Biceps].Should().Be(300m);
    }

    [Fact]
    public void VolumeByMuscle_Ignores_Workouts_Without_Recognized_Muscles()
    {
        var byMuscle = VolumeCalculator.VolumeByMuscle([("", 999m), ("banana", 999m)]);
        byMuscle.Should().BeEmpty();
    }
}
