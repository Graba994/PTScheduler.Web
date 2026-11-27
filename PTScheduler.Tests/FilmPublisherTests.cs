using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services.Social;
using PTScheduler.Portal.Services.Studio;
using Xunit;

namespace PTScheduler.Tests;

public class FilmPublisherTests
{
    [Fact]
    public void Caption_adds_link_tags_and_garmin_note()
    {
        var v = new StudioVideo { Title = "Plan w telefonie", Description = "Klient widzi plan w aplikacji.", TagsJson = "[\"trener personalny\",\"#hantly\"]" };
        var c = FilmPublisher.Caption(v);
        c.Should().Be("Klient widzi plan w aplikacji.\n\nSprawdź Hantly: {link}\n\n#trenerpersonalny #hantly");
        LinkService.ForInstagram(c).Should().Contain("Sprawdź Hantly: link w bio");

        var g = new StudioVideo { Title = "Garmin w Hantly", Description = "Treningi z Garmin Connect. Załóż konto: {link}", Garmin = true };
        FilmPublisher.Caption(g).Should().Contain(StudioService.GarminAttribution).And.NotContain("Sprawdź Hantly");
    }

    [Fact]
    public void Signature_depends_on_key_id_and_slug()
    {
        var key = new byte[32];
        var a = FilmPublisher.Sign(key, 1, "reklama-1");
        a.Should().HaveLength(32).And.MatchRegex("^[0-9a-f]+$");
        FilmPublisher.Sign(key, 1, "reklama-1").Should().Be(a);
        FilmPublisher.Sign(key, 2, "reklama-1").Should().NotBe(a);
        key[0] = 1;
        FilmPublisher.Sign(key, 1, "reklama-1").Should().NotBe(a);
    }
}
