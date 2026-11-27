using System.Net;
using System.Text;
using FluentAssertions;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class WatchTests
{
    private sealed class Fake(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(WebUtility.UrlDecode(request.RequestUri!.ToString()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    [Fact]
    public void Normalize_accounts_and_hashtags()
    {
        WatchService.Normalize("account", "@Studio.Trenera").Should().Be("studio.trenera");
        WatchService.Normalize("account", "https://www.instagram.com/hantly.pl/").Should().Be("hantly.pl");
        WatchService.Normalize("account", "zły login!").Should().Be("zylogin", "tylko litery ASCII, cyfry, kropka i podkreślnik");
        WatchService.Normalize("hashtag", "#TrenerPersonalny").Should().Be("trenerpersonalny");
        WatchService.Normalize("hashtag", "#").Should().BeNull();
    }

    [Fact]
    public void Summary_engagement_frequency_and_top()
    {
        var t = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var media = new List<WatchClient.Media>
        {
            new("1", "a", 100, 10, "IMAGE", null, t), new("2", "b", 300, 20, "REELS", null, t.AddDays(7)), new("3", "c", 50, 0, "IMAGE", null, t.AddDays(14))
        };
        var s = WatchService.Summarize(media, 1000, 950);
        s.Engagement.Should().Be(16.0);
        s.PostsPerWeek.Should().Be(1.5);
        s.FollowersDelta7.Should().Be(50);
        s.Top.Select(m => m.Id).Should().Equal("2", "1", "3");
    }

    [Fact]
    public async Task Business_discovery_and_hashtag_calls()
    {
        var fake = new Fake(req => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/ig_hashtag_search") => "{\"data\":[{\"id\":\"178\"}]}",
            var p when p.EndsWith("/top_media") => "{\"data\":[{\"id\":\"m1\",\"caption\":\"Trening\",\"like_count\":12,\"comments_count\":3,\"media_type\":\"IMAGE\",\"timestamp\":\"2026-11-20T10:00:00+0000\"}]}",
            _ => "{\"business_discovery\":{\"username\":\"studio.x\",\"followers_count\":2400,\"media_count\":310,\"media\":{\"data\":[{\"id\":\"p1\",\"like_count\":80,\"comments_count\":4,\"media_type\":\"VIDEO\",\"media_product_type\":\"REELS\",\"timestamp\":\"2026-11-25T18:00:00+0000\"}]}}}"
        });
        var c = new WatchClient(new Factory(fake));
        var p = await c.ProfileAsync("1784", "T", "studio.x");
        p.Followers.Should().Be(2400);
        p.Media.Single().Type.Should().Be("REELS");
        fake.Urls[0].Should().Contain("/1784?fields=business_discovery.username(studio.x)");
        (await c.HashtagIdAsync("1784", "T", "trenerpersonalny")).Should().Be("178");
        (await c.HashtagMediaAsync("178", "1784", "T", recent: false)).Single().Likes.Should().Be(12);
        fake.Urls[^1].Should().Contain("/178/top_media?user_id=1784");
    }
}
