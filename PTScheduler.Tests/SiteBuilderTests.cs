using FluentAssertions;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Kreator strony trenera: szablony, przeniesienie starej treści, szkic i publikacja.</summary>
public class SiteBuilderTests
{
    [Fact]
    public void EveryTemplate_BuildsWidgetPage_WithKnownWidgets()
    {
        foreach (var t in SiteWidgets.Templates)
        {
            var page = SiteWidgets.BuildTemplate(t.Key);
            page.LayoutVersion.Should().Be(2);
            page.Theme.Should().Be(t.Theme);
            page.Blocks.Should().NotBeEmpty();
            page.Blocks[0].Type.Should().Be("hero");
            page.Blocks.Select(b => b.Id).Should().OnlyHaveUniqueItems();
            page.Blocks.Should().OnlyContain(b => SiteWidgets.Find(b.Type) != null);
        }
    }

    [Fact]
    public void EveryWidget_HasDefaultContent()
    {
        foreach (var w in SiteWidgets.Catalog)
        {
            var b = SiteWidgets.Create(w.Type);
            b.Type.Should().Be(w.Type);
            (b.Title ?? b.Items.FirstOrDefault()?.Title ?? b.Subtitle).Should().NotBeNullOrWhiteSpace($"widget {w.Type} powinien mieć przykładową treść");
        }
    }

    [Fact]
    public void Legacy_IsConvertedKeepingTrainerTexts()
    {
        var old = new SiteContentDto
        {
            HeroTitle = "Mój tytuł",
            ShowStats = true, Stats = [new() { Value = "200+", Label = "klientów" }],
            ShowFaq = true, Faqs = [new() { Question = "Pytanie?", Answer = "Odpowiedź." }],
            ShowAbout = true, AboutHtml = "<p>Akapit 1</p><p>Akapit 2</p>"
        };
        var page = SiteWidgets.FromLegacy(old);
        page.LayoutVersion.Should().Be(2);
        page.Blocks.Single(b => b.Type == "hero").Title.Should().Be("Mój tytuł");
        page.Blocks.Single(b => b.Type == "stats").Items.Single().Value.Should().Be("200+");
        page.Blocks.Single(b => b.Type == "faq").Items.Single().Text.Should().Be("Odpowiedź.");
        page.Blocks.Single(b => b.Type == "about").Text.Should().Be("Akapit 1\n\nAkapit 2");
    }

    [Fact]
    public async Task Draft_IsSeparateUntilPublished()
    {
        var dir = Path.Combine(Path.GetTempPath(), "site-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var web = new Mock<IWebRootPathProvider>();
            web.Setup(w => w.WebRootPath).Returns(dir);
            var svc = new SiteContentService(web.Object);

            // Nowa instalacja: od razu strona z widgetów.
            (await svc.GetAsync()).LayoutVersion.Should().Be(2);

            var live = SiteWidgets.BuildTemplate("personal");
            await svc.SaveAsync(live);
            var draft = SiteWidgets.BuildTemplate("premium");
            await svc.SaveDraftAsync(draft);

            (await svc.GetAsync()).Theme.Should().Be("studio");
            (await svc.GetDraftAsync()).Theme.Should().Be("premium");
            (await svc.HasUnpublishedDraftAsync()).Should().BeTrue();

            await svc.PublishDraftAsync();
            (await svc.GetAsync()).Theme.Should().Be("premium");
            (await svc.HasUnpublishedDraftAsync()).Should().BeFalse();

            await svc.SaveDraftAsync(SiteWidgets.BuildTemplate("sport"));
            await svc.DiscardDraftAsync();
            (await svc.GetDraftAsync()).Theme.Should().Be("premium");
        }
        finally { Directory.Delete(dir, true); }
    }
}
