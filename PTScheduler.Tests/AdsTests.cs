using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class AdsTests
{
    private sealed class Fake(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.RequestUri!.ToString(), WebUtility.UrlDecode(body)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request, body), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    [Fact]
    public void Budget_limits_and_ids()
    {
        AdsClient.ActId("123456").Should().Be("act_123456");
        AdsClient.ActId("act_987").Should().Be("act_987");
        AdsClient.ActId("").Should().Be("");
        AdsClient.Minor(19.99m).Should().Be(1999);
        AdsClient.StatusText(3).Should().Contain("płatność");

        AdsService.Form F(decimal daily = 20, int days = 7, int ageMin = 21, int ageMax = 55, bool ig = true, bool fb = true, string mode = "traffic") =>
            new(1, mode, daily, days, ageMin, ageMax, [], ig, fb);
        AdsService.Validate(F()).Should().BeNull();
        AdsService.Validate(F(daily: 2)).Should().Contain("Budżet dzienny");
        AdsService.Validate(F(daily: 500, days: 30)).Should().Contain("Łączny budżet");
        AdsService.Validate(F(days: 0)).Should().Contain("Czas trwania");
        AdsService.Validate(F(ageMin: 40, ageMax: 30)).Should().Contain("Wiek");
        AdsService.Validate(F(ig: false, fb: false)).Should().Contain("gdzie wyświetlać");
        AdsService.Validate(F(mode: "x")).Should().NotBeNull();
    }

    [Fact]
    public void Targeting_is_poland_with_interests_and_platforms()
    {
        var json = JsonDocument.Parse(AdsClient.Targeting(21, 55, [("6003", "Trening personalny")], instagram: true, facebook: false)).RootElement;
        json.GetProperty("geo_locations").GetProperty("countries")[0].GetString().Should().Be("PL");
        json.GetProperty("publisher_platforms").EnumerateArray().Select(x => x.GetString()).Should().Equal("instagram");
        json.GetProperty("flexible_spec")[0].GetProperty("interests")[0].GetProperty("id").GetString().Should().Be("6003");
        json.GetProperty("age_min").GetInt32().Should().Be(21);
    }

    [Fact]
    public async Task Everything_is_created_paused_and_link_ad_uses_signup_button()
    {
        var fake = new Fake((req, _) => req.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/campaigns") => "{\"id\":\"c1\"}",
            var p when p.EndsWith("/adsets") => "{\"id\":\"s1\"}",
            var p when p.EndsWith("/adimages") => "{\"images\":{\"bytes\":{\"hash\":\"h1\"}}}",
            var p when p.EndsWith("/adcreatives") => "{\"id\":\"cr1\"}",
            var p when p.EndsWith("/ads") => "{\"id\":\"a1\"}",
            _ => "{\"success\":true}"
        });
        var ads = new AdsClient(new Factory(fake));
        (await ads.CampaignAsync("act_1", "T", "Hantly", traffic: true)).Should().Be("c1");
        (await ads.AdSetAsync("act_1", "T", "c1", "Hantly", true, 20m, new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 8, 9, 0, 0, DateTimeKind.Utc), "{}")).Should().Be("s1");
        (await ads.ImageHashAsync("act_1", "T", [1, 2, 3])).Should().Be("h1");
        (await ads.LinkCreativeAsync("act_1", "T", "Hantly", "555", "1784", "Opis", "Tytuł", "https://hantly.pl/go/abc", "h1")).Should().Be("cr1");
        (await ads.AdAsync("act_1", "T", "s1", "cr1", "Hantly")).Should().Be("a1");

        fake.Calls[0].Body.Should().Contain("objective=OUTCOME_TRAFFIC").And.Contain("status=PAUSED").And.Contain("special_ad_categories=[]");
        fake.Calls[1].Body.Should().Contain("daily_budget=2000").And.Contain("optimization_goal=LINK_CLICKS").And.Contain("status=PAUSED").And.Contain("destination_type=WEBSITE")
            .And.Contain("end_time=2026-12-08T09:00:00Z");
        fake.Calls[3].Body.Should().Contain("\"type\":\"SIGN_UP\"").And.Contain("\"link\":\"https://hantly.pl/go/abc\"").And.Contain("\"instagram_user_id\":\"1784\"");
        fake.Calls[4].Body.Should().Contain("status=PAUSED").And.Contain("{\"creative_id\":\"cr1\"}");
        fake.Calls.Should().OnlyContain(c => !c.Body.Contains("status=ACTIVE"), "nic nie startuje bez decyzji człowieka");
    }
}
