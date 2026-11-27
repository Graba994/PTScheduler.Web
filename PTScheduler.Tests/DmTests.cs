using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class DmTests
{
    private sealed class Fake(Func<HttpRequestMessage, string, string> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request, body), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    [Fact]
    public void Keyword_matches_whole_word_any_case()
    {
        DmService.Matches("plan poproszę!", "PLAN").Should().BeTrue();
        DmService.Matches("PLAN", "PLAN").Should().BeTrue();
        DmService.Matches("Chcę #plan", "PLAN").Should().BeTrue();
        DmService.Matches("planowanie", "PLAN").Should().BeFalse();
        DmService.Matches("zaplan", "PLAN").Should().BeFalse();
        DmService.Matches("Łódź łódź", "ŁÓDŹ").Should().BeTrue();
        DmService.NormalizeKeyword("  plan  treningowy ").Should().Be("PLAN TRENINGOWY");
    }

    [Fact]
    public void Scrub_removes_emails_and_phones()
    {
        DmService.Scrub("Pisz na jan.kowalski@gmail.com albo +48 600 100 200, dzięki")
            .Should().Be("Pisz na [e-mail] albo [telefon], dzięki");
        DmService.Scrub("Mam 3 klientów i 2 sesje").Should().Be("Mam 3 klientów i 2 sesje");
    }

    [Fact]
    public async Task Conversations_parse_participant_and_messages()
    {
        var fake = new Fake((req, _) => """
            {"data":[{"id":"t_1","updated_time":"2026-11-27T10:00:00+0000",
              "participants":{"data":[{"id":"1784","username":"hantly.pl"},{"id":"555","username":"kasia.fit"}]},
              "messages":{"data":[
                {"id":"m2","message":"Ile kosztuje?","created_time":"2026-11-27T10:00:00+0000","from":{"id":"555","username":"kasia.fit"}},
                {"id":"m1","message":"Cześć","created_time":"2026-11-27T09:00:00+0000","from":{"id":"555","username":"kasia.fit"}}]}}]}
            """);
        var client = new MessagesClient(new Factory(fake));
        var box = MessagesClient.Box.InstagramViaPage("https://graph.facebook.com", "999", "1784", "T");
        var list = await client.ConversationsAsync(box);
        list.Should().ContainSingle();
        list[0].ParticipantId.Should().Be("555");
        list[0].ParticipantName.Should().Be("kasia.fit");
        list[0].Messages.Select(m => m.Text).Should().Equal("Cześć", "Ile kosztuje?");
        fake.Calls[0].Url.Should().StartWith("https://graph.facebook.com/v24.0/999/conversations?platform=instagram");

        await client.PrivateReplyAsync(box, "c77", "Link: x");
        WebUtility.UrlDecode(fake.Calls[^1].Body).Should().Contain("recipient={\"comment_id\":\"c77\"}").And.Contain("message={\"text\":\"Link: x\"}");
        await client.SendAsync(MessagesClient.Box.Messenger("https://graph.facebook.com", "999", "T"), "555", "Hej");
        WebUtility.UrlDecode(fake.Calls[^1].Body).Should().Contain("recipient={\"id\":\"555\"}").And.Contain("messaging_type=RESPONSE");
        fake.Calls[^1].Url.Should().Be("https://graph.facebook.com/v24.0/999/messages");
    }

    [Fact]
    public async Task Store_marks_new_inbound_as_open_and_own_last_message_as_done()
    {
        var services = new ServiceCollection();
        var name = Guid.NewGuid().ToString();
        services.AddDbContextFactory<PortalDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<SiteSettingsService>();
        services.AddScoped<EmailService>();
        services.AddScoped<AiService>();
        services.AddScoped<InstagramAi>();
        services.AddScoped<LinkService>();
        services.AddScoped<MessagesClient>();
        services.AddScoped(_ => (InstagramService)null!);
        services.AddScoped<DmService>();
        await using var sp = services.BuildServiceProvider();
        var dm = sp.GetRequiredService<DmService>();
        var box = MessagesClient.Box.InstagramViaPage("h", "999", "1784", "T");
        var now = DateTime.UtcNow;

        await dm.StoreAsync(box, [new("t1", "555", "kasia.fit", now, [new("m1", "555", "Ile kosztuje?", now.AddMinutes(-5), false)])]);
        var t = (await dm.ThreadsAsync(openOnly: true)).Single();
        t.Unread.Should().BeTrue();
        DmService.CanReply(t).Should().BeTrue();
        (await dm.OpenCountAsync()).Should().Be(1);

        // Odpowiedź z telefonu (nasza ostatnia) — rozmowa obsłużona; powtórne pobranie nie dubluje wiadomości.
        await dm.StoreAsync(box, [new("t1", "555", "kasia.fit", now, [
            new("m1", "555", "Ile kosztuje?", now.AddMinutes(-5), false), new("m2", "1784", "Od 49 zł", now.AddMinutes(-1), false)])]);
        (await dm.ThreadsAsync(openOnly: true)).Should().BeEmpty();
        var all = await dm.ThreadsAsync(openOnly: false);
        (await dm.MessagesAsync(all[0].Id)).Select(m => (m.Text, m.Inbound)).Should().Equal(("Ile kosztuje?", true), ("Od 49 zł", false));

        var old = new SocialThread { LastInboundAt = now.AddHours(-30) };
        DmService.CanReply(old).Should().BeFalse("Meta pozwala odpowiedzieć 24 h od ostatniej wiadomości osoby");
    }
}
