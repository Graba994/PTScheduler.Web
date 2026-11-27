using FluentAssertions;
using PTScheduler.Application.DTOs;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Przykładowe treści z szablonu strony trenera: goście nie widzą zmyślonych liczb, opinii ani kontaktu.</summary>
public class SiteSampleContentTests
{
    [Fact]
    public void Template_MarksEveryBlockAsSample()
    {
        var site = SiteWidgets.BuildTemplate("transform");
        site.Blocks.Should().NotBeEmpty().And.OnlyContain(b => b.Sample);
    }

    [Fact]
    public void Guests_DoNotSeeSampleStatsTransformationsOrFakeReviews()
    {
        var site = SiteWidgets.BuildTemplate("transform");
        SiteWidgets.ShownToGuests(site.Blocks.First(b => b.Type == "stats"), liveReviews: 0).Should().BeFalse();
        SiteWidgets.ShownToGuests(site.Blocks.First(b => b.Type == "transformations"), 0).Should().BeFalse();
        SiteWidgets.ShownToGuests(site.Blocks.First(b => b.Type == "testimonials"), 0).Should().BeFalse("bez prawdziwych opinii nie ma czego pokazać");
        SiteWidgets.ShownToGuests(site.Blocks.First(b => b.Type == "testimonials"), 3).Should().BeTrue("opinie klientów z aplikacji są prawdziwe");
        SiteWidgets.ShownToGuests(site.Blocks.First(b => b.Type == "hero"), 0).Should().BeTrue();
    }

    [Fact]
    public void EditedBlock_IsShownAsWritten()
    {
        var stats = SiteWidgets.Create("stats");
        stats.Sample = false;
        SiteWidgets.ShownToGuests(stats, 0).Should().BeTrue();
    }

    [Fact]
    public void Personalize_ReplacesPlaceholderContactWithRealData()
    {
        var site = SiteWidgets.BuildTemplate("transform");
        var contact = site.Blocks.First(b => b.Type == "contact");
        SiteWidgets.ShownToGuests(contact, 0).Should().BeFalse("przed uzupełnieniem są tylko przykładowe dane");

        SiteWidgets.Personalize(site, "marta@example.com", "+48 501 222 333");

        contact.Items.Should().Contain(i => i.Url == "mailto:marta@example.com");
        contact.Items.Should().Contain(i => i.Url == "tel:+48501222333");
        contact.Items.Should().NotContain(i => SiteWidgets.IsPlaceholderContact(i));
        SiteWidgets.ShownToGuests(contact, 0).Should().BeTrue();
    }

    [Fact]
    public void Personalize_WithoutPhone_SendsTelButtonsToBooking()
    {
        var site = SiteWidgets.BuildTemplate("senior");
        SiteWidgets.Personalize(site, "jan@example.com", null);
        site.Blocks.Should().NotContain(b => (b.CtaUrl ?? "").Contains("600") || (b.Cta2Url ?? "").Contains("600"));
        site.Blocks.First(b => b.Type == "hero").CtaUrl.Should().Be("/book");
    }

    [Fact]
    public void DefaultContent_HasNoInventedRatingOrScarcity()
    {
        SiteWidgets.Create("hero").Items.Should().NotContain(i => (i.Title ?? "").Contains("4,9"));
        SiteWidgets.Create("cta").Eyebrow.Should().NotContain("wolne miejsca");
    }
}
