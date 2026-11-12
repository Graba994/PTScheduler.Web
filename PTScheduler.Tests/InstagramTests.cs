using System.Net;
using System.Text;
using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class InstagramRulesTests
{
    [Fact]
    public void Caption_limits_are_checked()
    {
        InstagramRules.CaptionProblems("Krótki opis #hantly").Should().BeEmpty();
        InstagramRules.CaptionProblems(new string('a', 2201)).Should().ContainSingle().Which.Should().Contain("2200");
        InstagramRules.CaptionProblems(string.Join(" ", Enumerable.Range(1, 31).Select(i => $"#tag{i}"))).Should().ContainSingle().Which.Should().Contain("hashtag");
        InstagramRules.CaptionProblems(string.Join(" ", Enumerable.Range(1, 21).Select(i => $"@konto{i}"))).Should().ContainSingle().Which.Should().Contain("@");
    }

    [Fact]
    public void Hashtags_are_added_without_duplicates()
    {
        var c = InstagramRules.AddHashtags("Opis #Hantly", ["hantly", "#trenerpersonalny", "trening personalny", "#trenerpersonalny"]);
        c.Should().Be("Opis #Hantly\n\n#trenerpersonalny #treningpersonalny");
        InstagramRules.AddHashtags("", ["#a"]).Should().Be("#a");
        InstagramRules.Hashtags("Zażółć #gęśląJaźń i #fit_biz").Should().Equal("#gęśląJaźń", "#fit_biz");
        InstagramRules.ParseTags("#a, b;#a\n c").Should().Equal("#a", "#b", "#c");
    }

    [Fact]
    public void Free_slots_skip_taken_days_and_past_times()
    {
        // Poniedziałek 10.11.2025, 12:00 w Polsce (UTC+1).
        var now = new DateTime(2025, 11, 10, 11, 0, 0, DateTimeKind.Utc);
        var taken = new[] { InstagramRules.ToUtc(new DateTime(2025, 11, 12, 18, 0, 0)) };
        var slots = InstagramRules.FreeSlots(now, new HashSet<int> { 1, 3, 5 }, new TimeOnly(18, 0), taken, 3);
        slots.Select(InstagramRules.ToWarsaw).Should().Equal(
            new DateTime(2025, 11, 10, 18, 0, 0), new DateTime(2025, 11, 14, 18, 0, 0), new DateTime(2025, 11, 17, 18, 0, 0));
        // Po 17:30 dzisiejszy termin już przepada.
        InstagramRules.FreeSlots(now.AddHours(5.6), new HashSet<int> { 1 }, new TimeOnly(18, 0), [], 1).Single()
            .Should().Be(InstagramRules.ToUtc(new DateTime(2025, 11, 17, 18, 0, 0)));
        InstagramRules.ParseDays("").Should().BeEquivalentTo([1, 3, 5]);
        InstagramRules.ParseDays("2,9,x,7").Should().BeEquivalentTo([2, 7]);
    }

    [Fact]
    public void Engagement_and_best_slots()
    {
        var m1 = new InstagramMedia { Id = "1", PostedAt = InstagramRules.ToUtc(new DateTime(2025, 11, 11, 19, 5, 0)), Likes = 40, Comments = 5, Saved = 10, Shares = 5, Reach = 600 };
        var m2 = new InstagramMedia { Id = "2", PostedAt = InstagramRules.ToUtc(new DateTime(2025, 11, 13, 8, 0, 0)), Likes = 10, Comments = 0, Reach = 500 };
        InstagramRules.EngagementRate(m1, 1000).Should().Be(10);
        InstagramRules.EngagementRate(new InstagramMedia { Likes = 5 }, 100).Should().Be(5, "bez zasięgu liczymy od obserwujących");
        InstagramRules.EngagementRate(new InstagramMedia(), 0).Should().BeNull();
        var best = InstagramRules.BestSlots([m1, m2], 1000);
        best[0].Should().Be(new InstagramRules.Slot(2, 19, 10, 1));
    }

    [Fact]
    public void Token_refresh_window()
    {
        var now = DateTime.UtcNow;
        InstagramRules.TokenNeedsRefresh(now.AddDays(50), now.AddDays(-10), now).Should().BeFalse();
        InstagramRules.TokenNeedsRefresh(now.AddDays(10), now.AddDays(-50), now).Should().BeTrue();
        InstagramRules.TokenNeedsRefresh(now.AddDays(10), now.AddHours(-2), now).Should().BeFalse("świeżo odnowionego nie odnawiamy");
        InstagramRules.TokenNeedsRefresh(now.AddDays(-1), now.AddDays(-61), now).Should().BeFalse("wygasłego nie da się odnowić");
    }

    [Theory]
    [InlineData(1080, 1350, false, 1080, 1350, false)]
    [InlineData(1000, 1000, false, 1080, 1080, false)]
    [InlineData(390, 844, false, 1080, 1350, true)]
    [InlineData(4000, 1000, false, 1080, 566, true)]
    [InlineData(1080, 1920, true, 1080, 1920, false)]
    [InlineData(1000, 1000, true, 1080, 1920, true)]
    public void Target_sizes_follow_instagram_ratios(int w, int h, bool story, int tw, int th, bool framed) =>
        InstagramRules.TargetSize(w, h, story).Should().Be((tw, th, framed));

    [Fact]
    public void Image_size_from_headers()
    {
        InstagramRules.ImageSize(InstagramGraphicsTests.Png(321, 123)).Should().Be((321, 123));
        var jpeg = InstagramGraphics.Photo(InstagramGraphicsTests.Png(800, 800), story: false).Jpeg;
        InstagramRules.IsJpeg(jpeg).Should().BeTrue();
        InstagramRules.ImageSize(jpeg).Should().Be((1080, 1080));
        InstagramRules.ImageSize("nie obrazek"u8).Should().Be((0, 0));
    }

    [Theory]
    [InlineData("🔥🔥🔥", true)]
    [InlineData("Super! 💪", true)]
    [InlineData("Dzięki, przyda się", true)]
    [InlineData("Ile to kosztuje?", false)]
    [InlineData("super, a jak to działa z kalendarzem google", false)]
    [InlineData("http://spam.example", false)]
    [InlineData("Nie podoba mi się ta zmiana i nie wiem czemu to zrobiliście tak", false)]
    public void Simple_praise_detection(string text, bool simple) => InstagramRules.IsSimplePraise(text).Should().Be(simple);

    [Fact]
    public void Json_part_and_idea_parsing()
    {
        InstagramRules.JsonPart("Proszę:\n```json\n{\"a\":1}\n```").Should().Be("{\"a\":1}");
        InstagramRules.JsonPart("brak").Should().BeNull();
        var idea = InstagramAi.Parse("""
            ```json
            {"pillar":"porada","format":"carousel","title":"","slides":[
              {"template":"tip","headline":"Haczyk","body":"Tekst"},
              {"template":"wymyslony","headline":"Środek","points":["a","b"]},
              {"template":"cta","headline":""}],
             "caption":"Opis posta #zostaw","hashtags":["#trenerpersonalny"]}
            ```
            """);
        idea.Should().NotBeNull();
        idea!.Slides.Should().HaveCount(2, "slajd bez nagłówka odpada");
        idea.Slides[1].Template.Should().Be("tip", "nieznany szablon → porada");
        idea.Title.Should().Be("Haczyk");
        idea.Caption.Should().Be("Opis posta", "hashtagi z opisu przechodzą do listy na końcu");
        InstagramAi.Parse("{\"slides\":[]}").Should().BeNull();
        InstagramAi.Parse("to nie json").Should().BeNull();
    }

    [Fact]
    public void Bank_has_ideas_for_every_pillar_and_valid_templates()
    {
        foreach (var p in InstagramRules.Pillars)
            InstagramAi.Bank.Should().Contain(i => i.Pillar == p.Key, $"filar {p.Key}");
        foreach (var i in InstagramAi.Bank)
        {
            i.Slides.Should().NotBeEmpty();
            i.Slides.Should().OnlyContain(s => InstagramGraphics.Templates.Any(t => t.Key == s.Template) && s.Headline.Length <= 70);
            InstagramRules.CaptionProblems(InstagramRules.AddHashtags(i.Caption, i.Hashtags)).Should().BeEmpty();
            System.Text.RegularExpressions.Regex.IsMatch(i.Caption, @"\w+(łeś|łaś)\b|eś/aś").Should().BeFalse("teksty bez form płciowych");
        }
        InstagramAi.FromBank("porada", [InstagramAi.Bank[0].Title], 5).Should().NotContain(i => i.Title == InstagramAi.Bank[0].Title);
    }

    [Fact]
    public void Post_problems_by_kind()
    {
        var img = new InstagramAsset { Kind = "image" };
        InstagramService.Problems(new InstagramPost { Kind = InstagramPostKind.Image, Caption = "x", Assets = [img] }).Should().BeEmpty();
        InstagramService.Problems(new InstagramPost { Kind = InstagramPostKind.Image, Caption = "", Assets = [img] }).Should().Contain("Dodaj opis.");
        InstagramService.Problems(new InstagramPost { Kind = InstagramPostKind.Carousel, Caption = "x", Assets = [img] }).Should().ContainSingle();
        InstagramService.Problems(new InstagramPost { Kind = InstagramPostKind.Reel, Caption = "x", Assets = [img] }).Should().ContainSingle();
        InstagramService.Problems(new InstagramPost { Kind = InstagramPostKind.Story, Caption = "", Assets = [img] }).Should().BeEmpty("relacja nie ma opisu");
    }
}

public class InstagramClientTests
{
    private sealed class Fake(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<(string Method, string Url, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, request.RequestUri!.ToString(), body));
            var (code, json) = respond(request, body);
            return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    [Fact]
    public async Task Publishes_carousel_with_children()
    {
        var n = 0;
        var fake = new Fake((req, body) =>
        {
            var url = req.RequestUri!.AbsolutePath;
            if (url.EndsWith("/media")) return (HttpStatusCode.OK, $"{{\"id\":\"c{++n}\"}}");
            if (url.EndsWith("/media_publish")) return (HttpStatusCode.OK, "{\"id\":\"m1\"}");
            return (HttpStatusCode.OK, "{\"status_code\":\"FINISHED\"}");
        });
        var client = new InstagramClient(new Factory(fake));
        var c1 = await client.CreateContainerAsync("T", "17841", new() { ["image_url"] = "https://x/1.jpg", ["is_carousel_item"] = "true" });
        var c2 = await client.CreateContainerAsync("T", "17841", new() { ["image_url"] = "https://x/2.jpg", ["is_carousel_item"] = "true" });
        var carousel = await client.CreateContainerAsync("T", "17841", new() { ["media_type"] = "CAROUSEL", ["children"] = $"{c1},{c2}", ["caption"] = "Zażółć" });
        (await client.ContainerStatusAsync("T", carousel)).Status.Should().Be("FINISHED");
        (await client.PublishAsync("T", "17841", carousel)).Should().Be("m1");

        fake.Calls[0].Url.Should().Be("https://graph.instagram.com/v24.0/17841/media");
        fake.Calls[0].Body.Should().Contain("image_url=https%3A%2F%2Fx%2F1.jpg").And.Contain("access_token=T");
        fake.Calls[2].Body.Should().Contain("children=c1%2Cc2").And.Contain("media_type=CAROUSEL");
        fake.Calls[3].Url.Should().Contain("/c3?fields=status_code");
        fake.Calls[4].Body.Should().Be("creation_id=c3&access_token=T");
    }

    [Fact]
    public async Task Errors_are_translated()
    {
        var fake = new Fake((_, _) => (HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid OAuth access token\",\"code\":190}}"));
        var client = new InstagramClient(new Factory(fake));
        var ex = await Assert.ThrowsAsync<InstagramApiException>(() => client.MeAsync("zly"));
        ex.TokenInvalid.Should().BeTrue();
        ex.Message.Should().Contain("połącz konto ponownie");

        var limit = new Fake((_, _) => (HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"x\",\"code\":9,\"error_subcode\":2207042}}"));
        var ex2 = await Assert.ThrowsAsync<InstagramApiException>(() => new InstagramClient(new Factory(limit)).PublishAsync("T", "1", "c"));
        ex2.PublishLimit.Should().BeTrue();
        ex2.Message.Should().Contain("limit");
    }

    [Fact]
    public async Task Reads_profile_media_insights_and_comments()
    {
        var fake = new Fake((req, _) => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/me") => (HttpStatusCode.OK, "{\"user_id\":\"17841\",\"username\":\"hantly.pl\",\"followers_count\":120,\"follows_count\":5,\"media_count\":9}"),
            var p when p.EndsWith("/17841/media") => (HttpStatusCode.OK, "{\"data\":[{\"id\":\"m1\",\"media_type\":\"CAROUSEL_ALBUM\",\"media_product_type\":\"FEED\",\"timestamp\":\"2025-11-10T17:00:00+0000\",\"like_count\":12,\"comments_count\":3}]}"),
            var p when p.EndsWith("/m1/insights") => (HttpStatusCode.OK, "{\"data\":[{\"name\":\"reach\",\"values\":[{\"value\":300}]},{\"name\":\"views\",\"total_value\":{\"value\":450}}]}"),
            var p when p.EndsWith("/m1/comments") => (HttpStatusCode.OK, "{\"data\":[{\"id\":\"k1\",\"text\":\"Super!\",\"username\":\"ania\",\"timestamp\":\"2025-11-10T18:00:00+0000\",\"replies\":{\"data\":[{\"id\":\"k2\",\"text\":\"Dzięki!\",\"username\":\"hantly.pl\",\"timestamp\":\"2025-11-10T18:05:00+0000\"}]}}]}"),
            _ => (HttpStatusCode.NotFound, "{}")
        });
        var client = new InstagramClient(new Factory(fake));
        var me = await client.MeAsync("T");
        me.Should().Be(new InstagramClient.Profile("17841", "hantly.pl", null, null, 120, 5, 9));
        var media = await client.MediaAsync("T", "17841");
        media.Single().PostedAt().Should().Be(new DateTime(2025, 11, 10, 17, 0, 0, DateTimeKind.Utc));
        var ins = await client.MediaInsightsAsync("T", "m1", "FEED");
        ins.Should().Contain(new KeyValuePair<string, int>("reach", 300)).And.Contain(new KeyValuePair<string, int>("views", 450));
        var comments = await client.CommentsAsync("T", "m1");
        comments.Should().HaveCount(2);
        comments[1].ParentId.Should().Be("k1");
    }
}

file static class MediaExt
{
    public static DateTime PostedAt(this InstagramClient.Media m) => m.Timestamp;
}
