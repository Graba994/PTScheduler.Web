using System.Text;
using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services.Studio;
using Xunit;
using R = PTScheduler.Portal.Services.Studio.StudioRules;

namespace PTScheduler.Tests;

public class StudioTests
{
    private static R.Ads GoodAds() => new()
    {
        Meta = [.. Enumerable.Range(1, 3).Select(i => new R.MetaAd { Angle = "Czas", PrimaryText = "Tekst " + i, Headline = "Grafik sam się układa", Description = "Zacznij za 0 zł" })],
        Google = new() { Headlines = [.. Enumerable.Range(1, 12).Select(i => $"Nagłówek {i}")], Descriptions = ["a", "b", "c", "d"], Keywords = ["aplikacja dla trenera"] },
        Audiences = ["trenerzy personalni"], Notes = "Test A/B"
    };

    [Fact]
    public void Ads_limits_are_checked()
    {
        R.Check(GoodAds()).Should().BeEmpty();
        var a = GoodAds();
        a.Meta[1].Headline = new string('x', 41);
        a.Meta[2].Description = new string('y', 31);
        a.Google.Headlines[0] = new string('z', 31);
        a.Google.Descriptions.RemoveAt(0);
        var issues = R.Check(a);
        issues.Should().Contain(i => i.Field == "Meta 2 — nagłówek" && i.Problem == "41/40 znaków");
        issues.Should().Contain(i => i.Field == "Meta 3 — opis");
        issues.Should().Contain(i => i.Field == "Google — nagłówki" && i.Problem.Contains("1 za długie"));
        issues.Should().Contain(i => i.Field == "Google — teksty" && i.Problem.Contains("3"));
    }

    [Fact]
    public void Post_limits_and_caption_tags()
    {
        var p = R.Clean(new R.Post
        {
            Headline = " Koniec z chaosem ", Sub = "Grafik, płatności i plany w jednym miejscu",
            Bullets = ["Rezerwacje 24/7", "", "Przypomnienia", "Plany w telefonie", "Czwarta"],
            Caption = "Haczyk.\n\nTreść #trenerpersonalny", Hashtags = ["#hantly", "hantly", "fit biz"]
        });
        p.Headline.Should().Be("Koniec z chaosem");
        p.Bullets.Should().Equal("Rezerwacje 24/7", "Przypomnienia", "Plany w telefonie");
        p.Hashtags.Should().Equal("hantly", "fitbiz", "trenerpersonalny");
        p.Caption.Should().Be("Haczyk.\n\nTreść");
        R.Check(p).Should().BeEmpty();
        R.CaptionWithTags(p).Should().Be("Haczyk.\n\nTreść\n\n#hantly #fitbiz #trenerpersonalny");
        p.Headline = new string('a', 41);
        R.Check(p).Should().Contain(i => i.Field == "Nagłówek");
    }

    [Fact]
    public void Csv_is_excel_friendly()
    {
        var a = GoodAds();
        a.Meta[0].PrimaryText = "Linia 1; z „cudzysłowem\"\nLinia 2";
        var bytes = R.Csv(a);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.Should().StartWith("Platforma;Typ;Tekst;Znaki");
        text.Should().Contain("Meta;Wariant 1 — tekst główny;\"Linia 1; z „cudzysłowem\"\"\nLinia 2\";");
        text.Should().Contain("Google;Nagłówek;Nagłówek 1;10\r\n");
        text.Should().Contain("Wszystkie;Grupa odbiorców;trenerzy personalni;19");
    }

    [Fact]
    public void Parses_ai_json_with_fences()
    {
        var ads = R.Parse<R.Ads>("Oto:\n```json\n{\"meta\":[{\"angle\":\"x\",\"primaryText\":\"t\",\"headline\":\"h\",\"description\":\"d\"}],\"google\":{\"headlines\":[\"a\"],\"descriptions\":[],\"keywords\":[]},\"audiences\":[],\"notes\":\"\"}\n```");
        ads.Should().NotBeNull();
        ads!.Meta.Single().Headline.Should().Be("h");
        R.Parse<R.Ads>("brak").Should().BeNull();
        R.Parse<R.Post>("{zly json").Should().BeNull();
    }

    [Fact]
    public void Html_and_razor_to_text()
    {
        var html = "<html><head><style>.a{}</style><script>var x=1</script></head><body><h1>Hantly</h1><p>Grafik &amp; klienci</p><svg><path/></svg><ul><li>Raz</li><li>Dwa</li></ul></body></html>";
        R.HtmlToText(html).Should().Be("Hantly\nGrafik & klienci\nRaz\nDwa");
        var razor = "@page \"/\"\n@inject X Y\n<h2>Witaj @Model.Name</h2>\n@* komentarz *@\n<p>Plany @(x ? \"a\" : \"b\") w telefonie</p>\n@code { private int x; }";
        var t = R.RazorToText(razor);
        t.Should().Contain("Witaj").And.Contain("Plany").And.Contain("w telefonie");
        t.Should().NotContain("@").And.NotContain("komentarz").And.NotContain("private");
        R.CommitLine(new DateTime(2026, 11, 12), "Instagram marki w Portalu\n\n- szczegóły").Should().Be("2026-11-12 | Instagram marki w Portalu");
        R.CommitLine(DateTime.Today, "Merge branch 'x'").Should().BeNull();
    }

    [Fact]
    public void Garmin_is_filtered_when_not_allowed()
    {
        var k = new R.Knowledge
        {
            Features = [new() { Name = "Plany treningowe" }, new() { Name = "Zegarki klientów", Benefit = "Dane z Garmin Connect" }],
            Novelties = [new() { Title = "Garmin: plan na zegarek" }, new() { Title = "Mapa mięśni" }],
            PainPoints = ["Chaos w grafiku"], ProofPoints = ["Integracja z Garminem", "Start za 0 zł"]
        };
        var off = StudioService.ForPrompt(k, garmin: false);
        off.Features.Select(f => f.Name).Should().Equal("Plany treningowe");
        off.Novelties.Select(n => n.Title).Should().Equal("Mapa mięśni");
        off.ProofPoints.Should().Equal("Start za 0 zł");
        StudioService.ForPrompt(k, garmin: true).Features.Should().HaveCount(2);
        k.Features.Should().HaveCount(2, "oryginał bez zmian");
    }

    [Fact]
    public void Manual_templates_parse_back()
    {
        var p = new StudioPromotion { Kind = StudioKind.Ads, ResultJson = StudioService.Empty(StudioKind.Ads) };
        StudioService.AdsOf(p)!.Meta.Should().HaveCount(3);
        StudioService.AdsOf(p)!.Google.Headlines.Should().HaveCount(10);
        var post = new StudioPromotion { Kind = StudioKind.Post, ResultJson = StudioService.Empty(StudioKind.Post) };
        StudioService.PostOf(post)!.Bullets.Should().HaveCount(3);
    }

    [Fact]
    public void Server_evaluation()
    {
        const long Gb = 1024L * 1024 * 1024;
        var ok = StudioService.Evaluate(8, 32 * Gb, 10 * Gb, 500 * Gb, 100 * Gb, 1.5, DateTime.UtcNow);
        ok.Tone.Should().Be("ok");
        ok.RenderMinutes.Should().Be(2.8, "8 rdzeni → render na 4");
        ok.Notes[0].Should().Contain("2,8").And.Contain("min");

        var small = StudioService.Evaluate(2, 4 * Gb, 3 * Gb, 40 * Gb, 38 * Gb, 1.8, null);
        small.Tone.Should().Be("danger");
        small.Notes.Should().Contain(n => n.Contains("pamięci")).And.Contain(n => n.Contains("dysku")).And.Contain(n => n.Contains("zajęty"));

        StudioService.Evaluate(4, 8 * Gb, (long)(5.5 * Gb), 200 * Gb, 50 * Gb, 0.5, null).Tone.Should().Be("warn", "2,5 GB wolne — jeden render naraz");
    }
}
