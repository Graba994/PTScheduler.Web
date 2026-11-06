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
    [InlineData(null, null, "enter,look,tricks,hover,talk,doze,party,react")]
    [InlineData("look,party", null, "look,party,react")] // dawna lista włączonych: nowsze efekty (reakcje) są włączone
    [InlineData("", null, "react")]
    [InlineData("look,party", "react,doze", "enter,look,tricks,hover,talk,party")] // lista wyłączonych ma pierwszeństwo
    [InlineData(null, "", "enter,look,tricks,hover,talk,doze,party,react")]
    public void Effects_FromSavedSettings(string? legacyOn, string? off, string expected) =>
        AssistantService.EffectsFrom(legacyOn, off).Should().Be(expected);

    [Fact]
    public void EffectsOff_IsTheComplement() =>
        AssistantService.EffectsOffFor(["look", "react"]).Should().Be("enter,tricks,hover,talk,doze,party");

    [Theory]
    [InlineData("przychod_w_gore", "stonks")]
    [InlineData("rekord_tygodnia", "trophy")]
    [InlineData("niezaplacone", "coins")]
    [InlineData("okienko", "calendar")]
    [InlineData("nieaktywni", "magnifier")]
    [InlineData("dzis", "sun")]
    [InlineData("nieznany_rodzaj", null)]
    public void Reaction_MatchesTipKind(string kind, string? expected) =>
        PTScheduler.Web.Services.PlatformAssistantService.ReactionFor(kind).Should().Be(expected);

    [Fact]
    public void EveryReaction_HasStylesAndDuration_InScript()
    {
        var js = File.ReadAllText(Path.Combine(Root(), "PTScheduler.Web", "wwwroot", "js", "hantly-motion.js"));
        foreach (var (key, _, _) in AssistantService.Reactions)
        {
            js.Should().Contain($"{key}: ", $"skrypt musi znać czas trwania reakcji „{key}”");
            MascotArt.LiveCss.Should().Contain($".hm-fx.tr-{key} ", $"brak stylu reakcji „{key}”");
        }
        var kinds = new[] { "przychod_w_gore", "rekord_tygodnia", "postep", "kamien_milowy", "niezaplacone", "akceptacje", "okienko",
            "przeciazenie", "nieaktywni", "dzis", "tydzien", "cena", "przed_treningiem", "plan_stoi" };
        foreach (var kind in kinds)
        {
            var r = PTScheduler.Web.Services.PlatformAssistantService.ReactionFor(kind);
            r.Should().NotBeNull(kind);
            js.Should().Contain($"{r}: ", $"reakcja „{r}” dla „{kind}” musi istnieć w skrypcie");
        }
    }

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
