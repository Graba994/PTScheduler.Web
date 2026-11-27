using System.Net;
using System.Text;
using FluentAssertions;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class SocialTests
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

    private static InstagramPost Post(params InstagramAsset[] assets) => new() { Caption = "Opis", Assets = [.. assets] };
    private static InstagramAsset Img(int order = 0) => new() { Kind = "image", Order = order };
    private static InstagramAsset Vid() => new() { Kind = "video", ExternalUrl = "https://cdn/x.mp4" };

    [Fact]
    public void Platform_rules()
    {
        SocialPlatforms.Problems(SocialPlatform.Facebook, Post(), "Sam tekst").Should().BeEmpty("Facebook przyjmie wpis bez grafiki");
        SocialPlatforms.Problems(SocialPlatform.Facebook, Post(Img(), Vid()), "x").Should().ContainSingle().Which.Should().Contain("jeden film");
        SocialPlatforms.Problems(SocialPlatform.Threads, Post(), new string('a', 501)).Should().ContainSingle().Which.Should().Contain("500");
        SocialPlatforms.Problems(SocialPlatform.TikTok, Post(Img()), "x").Should().Contain(p => p.Contains("film"));
        SocialPlatforms.Problems(SocialPlatform.YouTube, Post(Vid()), "x").Should().BeEmpty();
        SocialPlatforms.Problems(SocialPlatform.X, Post(), new string('a', 281)).Should().ContainSingle();
        SocialPlatforms.Problems(SocialPlatform.LinkedIn, Post(), "").Should().ContainSingle().Which.Should().Contain("pusty");
        SocialPlatforms.All.Where(p => !p.Api).Should().OnlyContain(p => p.WhyManual != null);
        SocialPlatforms.All.Where(p => p.Api).Select(p => p.Platform).Should().BeEquivalentTo([SocialPlatform.Facebook, SocialPlatform.Threads]);
    }

    [Fact]
    public void Instagram_rules_apply_only_when_selected()
    {
        var p = new InstagramPost { Kind = InstagramPostKind.Image, Caption = "", Assets = [], ToInstagram = true };
        InstagramService.Problems(p).Should().NotBeEmpty();
        p.ToInstagram = false;
        InstagramService.Problems(p).Should().BeEmpty("bez Instagrama sprawdzamy tylko wybrane kanały");
    }

    [Fact]
    public async Task Facebook_publishes_album_and_lists_pages()
    {
        var n = 0;
        var fake = new Fake((req, body) => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/photos") => (HttpStatusCode.OK, $"{{\"id\":\"ph{++n}\"}}"),
            var p when p.EndsWith("/feed") => (HttpStatusCode.OK, "{\"id\":\"123_456\"}"),
            var p when p.EndsWith("/me/accounts") => (HttpStatusCode.OK, "{\"data\":[{\"id\":\"123\",\"name\":\"Hantly\",\"access_token\":\"PAGE\",\"followers_count\":42,\"instagram_business_account\":{\"id\":\"1784\",\"username\":\"hantly.pl\"}}]}"),
            _ => (HttpStatusCode.OK, "{}")
        });
        var fb = new FacebookClient(new Factory(fake));
        var id = await fb.PublishAsync("123", "PAGE", "Nowy post", ["https://p/1.jpg", "https://p/2.jpg"], null);
        id.Should().Be("123_456");
        fake.Calls.Count(c => c.Url.EndsWith("/123/photos")).Should().Be(2);
        fake.Calls[0].Body.Should().Contain("published=false");
        var feed = fake.Calls.Single(c => c.Url.EndsWith("/feed"));
        WebUtility.UrlDecode(feed.Body).Should().Contain("attached_media[0]={\"media_fbid\":\"ph1\"}").And.Contain("attached_media[1]={\"media_fbid\":\"ph2\"}").And.Contain("message=Nowy post");

        var pages = await fb.PagesAsync("USER");
        pages.Single().Should().Be(new FacebookClient.Page("123", "Hantly", "PAGE", null, null, 42, "1784", "hantly.pl", null));
    }

    [Fact]
    public async Task Facebook_single_photo_text_and_video()
    {
        var fake = new Fake((req, _) => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/photos") => (HttpStatusCode.OK, "{\"id\":\"ph\",\"post_id\":\"1_2\"}"),
            var p when p.EndsWith("/videos") => (HttpStatusCode.OK, "{\"id\":\"v1\"}"),
            var p when p.EndsWith("/feed") => (HttpStatusCode.OK, "{\"id\":\"1_3\"}"),
            _ => (HttpStatusCode.OK, "{}")
        });
        var fb = new FacebookClient(new Factory(fake));
        (await fb.PublishAsync("1", "T", "a", ["https://p/1.jpg"], null)).Should().Be("1_2");
        (await fb.PublishAsync("1", "T", "sam tekst", [], null)).Should().Be("1_3");
        (await fb.PublishAsync("1", "T", "film", [], "https://cdn/v.mp4")).Should().Be("v1");
        fake.Calls.Last().Url.Should().StartWith("https://graph-video.facebook.com/");
    }

    [Fact]
    public async Task Threads_carousel_and_publish()
    {
        var n = 0;
        var fake = new Fake((req, body) =>
        {
            var p = req.RequestUri!.AbsolutePath;
            if (p.EndsWith("/threads")) return (HttpStatusCode.OK, $"{{\"id\":\"c{++n}\"}}");
            if (p.EndsWith("/threads_publish")) return (HttpStatusCode.OK, "{\"id\":\"t1\"}");
            if (req.RequestUri.Query.Contains("fields=status")) return (HttpStatusCode.OK, "{\"status\":\"FINISHED\"}");
            return (HttpStatusCode.OK, "{}");
        });
        var th = new ThreadsClient(new Factory(fake));
        var container = await th.CreateAsync("99", "T", "Krótko", ["https://p/1.jpg", "https://p/2.jpg"], null);
        container.Should().Be("c3");
        fake.Calls[2].Body.Should().Contain("media_type=CAROUSEL").And.Contain("children=c1%2Cc2");
        (await th.StatusAsync(container, "T")).Status.Should().Be("FINISHED");
        (await th.PublishAsync("99", "T", container)).Should().Be("t1");
        fake.Calls.Last().Body.Should().Contain("creation_id=c3");
        fake.Calls[0].Url.Should().StartWith("https://graph.threads.net/v1.0/99/threads");
    }

    [Fact]
    public async Task Instagram_through_facebook_uses_graph_facebook()
    {
        var fake = new Fake((req, _) => (HttpStatusCode.OK, "{\"id\":\"1784\",\"username\":\"hantly.pl\",\"followers_count\":10}"));
        var client = new InstagramClient(new Factory(fake)) { Provider = "facebook" };
        var p = await client.ProfileAsync("PAGE", "1784");
        p.UserId.Should().Be("1784");
        p.Followers.Should().Be(10);
        fake.Calls.Single().Url.Should().StartWith("https://graph.facebook.com/v24.0/1784?");
        client.Provider = "instagram";
        await client.MeAsync("T");
        fake.Calls.Last().Url.Should().StartWith("https://graph.instagram.com/");
    }
}
