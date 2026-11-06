using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Scenariusz spotkania i prezentacja: klucze zapisywane w notatkach muszą być stałe i unikalne, a zasoby slajdów istnieć.</summary>
public class MeetingPlaybookTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "PTScheduler.Portal")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Nie znaleziono katalogu repozytorium.");
    }

    [Fact]
    public void Keys_AreUnique()
    {
        MeetingPlaybook.Phases.Select(p => p.Key).Should().OnlyHaveUniqueItems();
        MeetingPlaybook.Objections.Select(o => o.Key).Should().OnlyHaveUniqueItems();
        MeetingPlaybook.Slides.Select(s => s.Key).Should().OnlyHaveUniqueItems();
        MeetingPlaybook.Outcomes.Select(o => o.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Keys_DoNotContainCommas_BecauseNotesStoreThemAsCsv()
    {
        MeetingPlaybook.Objections.Select(o => o.Key).Concat(MeetingPlaybook.Slides.Select(s => s.Key))
            .Should().NotContain(k => k.Contains(','));
    }

    [Fact]
    public void Slides_BelongToKnownChapters_AndEveryChapterHasSlides()
    {
        var chapters = MeetingPlaybook.Chapters.Select(c => c.Key).ToList();
        MeetingPlaybook.Slides.Should().OnlyContain(s => chapters.Contains(s.Chapter));
        chapters.Should().OnlyContain(c => MeetingPlaybook.Slides.Any(s => s.Chapter == c));
        MeetingPlaybook.Slides[0].Key.Should().Be("intro");
    }

    [Fact]
    public void FeatureSlides_HideGarminWhenDisabled()
    {
        MeetingPlaybook.FeatureSlides(garmin: false).Should().NotContain(s => s.Key == "garmin");
        MeetingPlaybook.FeatureSlides(garmin: true).Should().Contain(s => s.Key == "garmin");
    }

    [Fact]
    public void SlideImages_ExistInPortal()
    {
        var lp = Path.Combine(Root(), "PTScheduler.Portal", "wwwroot", "img", "lp");
        foreach (var image in MeetingPlaybook.Slides.Select(s => s.Image).Where(i => i is not null and not "video"))
        {
            File.Exists(Path.Combine(lp, image + ".webp")).Should().BeTrue($"{image}.webp jest używany w prezentacji");
            File.Exists(Path.Combine(lp, image + "@3x.webp")).Should().BeTrue($"{image}@3x.webp jest w srcset prezentacji");
        }
    }

    [Fact]
    public void Cards_CoverPhasesAndObjections()
    {
        var cards = MeetingPlaybook.Cards();
        cards.Should().NotBeEmpty();
        cards.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Front) && !string.IsNullOrWhiteSpace(c.Back));
        cards.Count.Should().BeGreaterThanOrEqualTo(MeetingPlaybook.Objections.Length);
    }
}
