using FluentAssertions;
using PTScheduler.Domain.Enums;
using PTScheduler.Web.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Mapa mięśni: każda partia z katalogu ma kształt na postaci, a odbicie i sylwetka kobiety liczą się poprawnie.</summary>
public class MuscleMapGeometryTests
{
    [Fact]
    public void Every_muscle_group_is_drawn_on_front_or_back_for_both_bodies()
    {
        foreach (var female in new[] { false, true })
            foreach (var m in Enum.GetValues<MuscleGroup>())
                MuscleMapGeometry.For(false, female).Concat(MuscleMapGeometry.For(true, female)).Should().Contain(r => r.Muscle == m, $"{m} ({(female ? "kobieta" : "mężczyzna")})");
    }

    [Fact]
    public void Mirror_and_offset_move_points_and_female_body_narrows_shoulders()
    {
        MuscleMapGeometry.Path("M150,100 L120,300", MuscleMapGeometry.Part.Torso, false, mirror: true, dx: 0).Should().Be("M50,100 L80,300");
        MuscleMapGeometry.Path("M150,100", MuscleMapGeometry.Part.Torso, false, mirror: false, dx: 224).Should().Be("M374,100");

        var shoulder = MuscleMapGeometry.Path("M150,120", MuscleMapGeometry.Part.Torso, true, false, 0);
        var hip = MuscleMapGeometry.Path("M140,240", MuscleMapGeometry.Part.Torso, true, false, 0);
        double X(string p) => double.Parse(p[1..].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
        X(shoulder).Should().BeLessThan(150, "węższe barki");
        X(hip).Should().BeGreaterThan(140, "szersze biodra");
        MuscleMapGeometry.Path("M120,30", MuscleMapGeometry.Part.Head, true, false, 0).Should().Be("M120,30");
    }
}
