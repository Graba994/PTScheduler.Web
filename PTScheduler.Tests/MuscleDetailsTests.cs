using FluentAssertions;
using PTScheduler.Domain.Enums;
using PTScheduler.Domain.Rules;
using PTScheduler.Web.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Szczegółowe partie mięśni: wnioskowanie z nazwy, zapis wyboru trenera, rysunek i kolory.</summary>
public class MuscleDetailsTests
{
    [Theory]
    [InlineData("Arnold Dumbbell Press", new[] { MuscleDetail.FrontDelts, MuscleDetail.SideDelts })]
    [InlineData("Side Lateral Raise", new[] { MuscleDetail.SideDelts })]
    [InlineData("Seated Bent-Over Rear Delt Raise", new[] { MuscleDetail.RearDelts })]
    [InlineData("Front Dumbbell Raise", new[] { MuscleDetail.FrontDelts })]
    public void Shoulder_heads_are_recognised_from_the_name(string name, MuscleDetail[] expected) =>
        MuscleDetails.Infer([MuscleGroup.Shoulders], [], name).Primary.Should().BeEquivalentTo(expected);

    [Fact]
    public void Chest_abs_and_secondary_shoulders_follow_the_exercise()
    {
        var incline = MuscleDetails.Infer([MuscleGroup.Chest], [MuscleGroup.Shoulders], "Barbell Incline Bench Press");
        incline.Primary.Should().Equal([MuscleDetail.UpperChest], "wyciskanie na skosie — górna klatka");
        incline.Secondary.Should().Equal([MuscleDetail.FrontDelts], "i przód barków");
        MuscleDetails.Infer([MuscleGroup.Abdominals], [], "Russian Twist").Primary.Should().Equal(MuscleDetail.Obliques);
        MuscleDetails.Infer([MuscleGroup.Abdominals], [], "Crunches").Primary.Should().Equal(MuscleDetail.Abs);
        MuscleDetails.Infer([MuscleGroup.Lats], [MuscleGroup.Shoulders], "Wide-Grip Lat Pulldown").Secondary.Should().Equal(MuscleDetail.RearDelts);
    }

    [Fact]
    public void Trainer_choice_wins_and_maps_back_to_groups()
    {
        var (p, s) = MuscleDetails.Resolve("rear delts", "upper traps,rear delts", "shoulders", "", "Some Press");
        p.Should().Equal(MuscleDetail.RearDelts);
        s.Should().Equal(MuscleDetail.UpperTraps);

        var (gp, gs) = MuscleDetails.ToGroups([MuscleDetail.FrontDelts, MuscleDetail.SideDelts], [MuscleDetail.UpperChest, MuscleDetail.RearDelts]);
        gp.Should().Equal(MuscleGroup.Shoulders);
        gs.Should().Equal(MuscleGroup.Chest);
        MuscleDetails.ToCsv([MuscleDetail.Obliques, MuscleDetail.Abs]).Should().Be("obliques,abs");
        MuscleDetails.Parse("obliques, abs, nonsense").Should().Equal(MuscleDetail.Obliques, MuscleDetail.Abs);
    }

    [Fact]
    public void Every_detail_is_drawn_labelled_and_coloured()
    {
        foreach (var female in new[] { false, true })
            foreach (var d in Enum.GetValues<MuscleDetail>())
            {
                MuscleMapGeometry.ForDetail(false, female).Concat(MuscleMapGeometry.ForDetail(true, female)).Should().Contain(r => r.Detail == d, d.ToString());
                MuscleDetails.Label(d).Should().NotBeNullOrWhiteSpace();
                MuscleColors.Color(d).Should().MatchRegex("^#[0-9a-f]{6}$");
            }
        Enum.GetValues<MuscleDetail>().Select(MuscleColors.Color).Should().OnlyHaveUniqueItems("każdy mięsień ma swój odcień");
    }
}
