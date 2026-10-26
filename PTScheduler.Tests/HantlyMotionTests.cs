using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Ruch maskotki Hantly: wspólny skrypt, efekty z Panelu i elementy sztuczek w animowanym SVG.</summary>
public class HantlyMotionTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PTScheduler.sln")) && !Directory.Exists(Path.Combine(dir.FullName, "PTScheduler.Portal")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Nie znaleziono katalogu repozytorium.");
    }

    [Fact]
    public void MotionScript_IsTheSameInPortalAndTrainerApp()
    {
        var root = Root();
        var portal = File.ReadAllText(Path.Combine(root, "PTScheduler.Portal", "wwwroot", "hantly-motion.js"));
        var app = File.ReadAllText(Path.Combine(root, "PTScheduler.Web", "wwwroot", "js", "hantly-motion.js"));
        app.Should().Be(portal, "skrypt ruchu maskotki musi być taki sam w Portalu i w aplikacji trenera");
    }

    [Theory]
    [InlineData(null, "enter,look,tricks,hover,talk,doze,party")]
    [InlineData("", "")]
    [InlineData("party, look ,nieznany,look", "look,party")]
    public void Effects_KnownOnly_InFixedOrder(string? saved, string expected) =>
        AssistantService.EffectsOrDefault(saved).Should().Be(expected);

    [Theory]
    [InlineData("calm", "calm")]
    [InlineData("lively", "lively")]
    [InlineData("szalony", "normal")]
    [InlineData(null, "normal")]
    public void Motion_FallsBackToNormal(string? saved, string expected) =>
        AssistantService.MotionOrDefault(saved).Should().Be(expected);

    [Fact]
    public void AnimatedSvg_HasTrickParts_AndStylesForEveryTrick()
    {
        var svg = MascotArt.AnimatedSvg();
        foreach (var part in new[] { "hma-hover", "hma-mouth", "hma-eyes", "hma-yawn", "hma-heart", "hma-plus", "hma-party" })
            svg.Should().Contain($"class=\"{part}\"");
        foreach (var trick in new[] { "wiggle", "hop", "spin", "curl", "look", "stretch", "heart", "talk", "party", "yawn" })
            MascotArt.LiveCss.Should().Contain($".hm-fx.tr-{trick} ");
        MascotArt.LiveCss.Should().Contain(".hm-fx.is-dozing").And.Contain("data-hm-motion=\"off\"").And.Contain("prefers-reduced-motion");
    }
}
