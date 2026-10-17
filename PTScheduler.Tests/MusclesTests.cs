using FluentAssertions;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Partie mięśniowe trzymamy na ćwiczeniu jako CSV kluczy Free Exercise DB;
/// tu weryfikujemy parsowanie tam i z powrotem oraz odporność na śmieci.
/// </summary>
public class MusclesTests
{
    [Fact]
    public void Parse_Maps_Known_Keys_Including_Two_Word()
    {
        var result = Muscles.Parse("chest, triceps, lower back");
        result.Should().BeEquivalentTo(
            new[] { MuscleGroup.Chest, MuscleGroup.Triceps, MuscleGroup.LowerBack },
            o => o.WithoutStrictOrdering());
    }

    [Fact]
    public void Parse_Is_CaseInsensitive_And_Trims()
    {
        Muscles.Parse("  Chest ,BICEPS ").Should().BeEquivalentTo(
            new[] { MuscleGroup.Chest, MuscleGroup.Biceps }, o => o.WithoutStrictOrdering());
    }

    [Fact]
    public void Parse_Skips_Unknown_And_Empty()
    {
        Muscles.Parse("chest,,banana,triceps").Should().BeEquivalentTo(
            new[] { MuscleGroup.Chest, MuscleGroup.Triceps }, o => o.WithoutStrictOrdering());
    }

    [Fact]
    public void Parse_Deduplicates()
    {
        Muscles.Parse("chest,chest").Should().ContainSingle().Which.Should().Be(MuscleGroup.Chest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_Empty_Input_Yields_Empty(string? csv)
    {
        Muscles.Parse(csv).Should().BeEmpty();
    }

    [Fact]
    public void ToCsv_Then_Parse_Roundtrips()
    {
        var muscles = new[] { MuscleGroup.LowerBack, MuscleGroup.Glutes, MuscleGroup.Hamstrings };
        var csv = Muscles.ToCsv(muscles);
        Muscles.Parse(csv).Should().BeEquivalentTo(muscles, o => o.WithoutStrictOrdering());
    }

    [Fact]
    public void Label_Is_Provided_For_Every_Muscle()
    {
        foreach (MuscleGroup m in Enum.GetValues<MuscleGroup>())
            Muscles.Label(m).Should().NotBeNullOrWhiteSpace();
    }
}
