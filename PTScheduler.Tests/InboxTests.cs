using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Poczta w Panelu: odczyt webhooka Brevo, wątki, maskowanie przed AI i bezpieczny podgląd HTML.</summary>
public class InboxTests
{
    private const string Payload = """
        {"items":[{
          "Uuid":["a1"],
          "MessageId":"<m1@gmail.com>",
          "InReplyTo":null,
          "From":{"Name":"Ola Trenerka","Address":"Ola@Example.com"},
          "To":[{"Name":null,"Address":"kontakt@hantly.pl"}],
          "Subject":"Pytanie o plan",
          "RawHtmlBody":"<p>Cześć <b>zespole</b></p><script>alert(1)</script>",
          "RawTextBody":"Cześć zespole\n\n> stara wiadomość",
          "ExtractedMarkdownMessage":"Cześć zespole, jak zmienić plan? Tel. 600 100 200, mail ola@example.com",
          "SpamScore":0.4,
          "Attachments":[{"Name":"faktura.pdf","ContentType":"application/pdf","ContentLength":2048,"DownloadToken":"tok-1"}],
          "Headers":{"References":"<x1@a.pl> <x2@a.pl>"}
        }]}
        """;

    [Fact]
    public void Parses_brevo_inbound_payload()
    {
        var items = InboxParsing.ParseBrevo(JsonDocument.Parse(Payload).RootElement);
        items.Should().HaveCount(1);
        var m = items[0];
        m.FromAddress.Should().Be("ola@example.com");
        m.FromName.Should().Be("Ola Trenerka");
        m.ToAddress.Should().Be("kontakt@hantly.pl");
        m.Text.Should().StartWith("Cześć zespole, jak zmienić plan?");
        m.References.Should().Equal("<x1@a.pl>", "<x2@a.pl>");
        m.Attachments.Should().ContainSingle(a => a.Name == "faktura.pdf" && a.Token == "tok-1" && a.Size == 2048);
        m.SpamScore.Should().Be(0.4);
    }

    [Fact]
    public void Parses_string_addresses_and_falls_back_to_html_text()
    {
        var json = """[{"From":"Jan Kowalski <JAN@x.pl>","To":"kontakt@hantly.pl","Subject":"Hej","RawHtmlBody":"<div>Linia 1<br>Linia 2</div><style>p{}</style>"}]""";
        var m = InboxParsing.ParseBrevo(JsonDocument.Parse(json).RootElement).Single();
        m.FromAddress.Should().Be("jan@x.pl");
        m.FromName.Should().Be("Jan Kowalski");
        m.Text.Should().Be("Linia 1\nLinia 2");
    }

    [Fact]
    public void Ignores_items_without_sender()
    {
        InboxParsing.ParseBrevo(JsonDocument.Parse("""{"items":[{"Subject":"x"}]}""").RootElement).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Re: Odp: FWD: Pytanie o plan", "pytanie o plan")]
    [InlineData("RE[2]:  Pytanie   o plan ", "pytanie o plan")]
    [InlineData("PD: AW: Pytanie o plan", "pytanie o plan")]
    public void Subject_key_strips_reply_prefixes(string subject, string key) =>
        InboxParsing.SubjectKey(subject).Should().Be(key);

    [Fact]
    public void Reply_subject_is_not_doubled()
    {
        InboxParsing.ReplySubject("Pytanie").Should().Be("Re: Pytanie");
        InboxParsing.ReplySubject("Odp: Pytanie").Should().Be("Odp: Pytanie");
    }

    [Fact]
    public void Masks_emails_and_phones_but_keeps_dates()
    {
        var masked = InboxParsing.MaskForAi("Tel. +48 600 100 200, mail ola@example.com, termin 2026-10-19, 3 treningi");
        masked.Should().Contain("[telefon]").And.Contain("[e-mail]").And.Contain("2026-10-19").And.Contain("3 treningi");
        masked.Should().NotContain("600").And.NotContain("ola@");
    }

    [Fact]
    public void Safe_srcdoc_blocks_scripts_and_remote_images_until_allowed()
    {
        var doc = InboxParsing.SafeSrcDoc("<img src='https://t.example/p.gif'>", allowRemoteImages: false);
        doc.Should().StartWith("<meta http-equiv=\"Content-Security-Policy\"");
        doc.Should().Contain("default-src 'none'").And.Contain("img-src data:;");
        InboxParsing.SafeSrcDoc("x", allowRemoteImages: true).Should().Contain("img-src data: https:;");
    }

    [Fact]
    public void Reply_html_encodes_body_and_quotes_previous_message()
    {
        var html = InboxParsing.ReplyHtml("Dzień dobry,\n\n<b>dzięki</b>", "19.10.2026, 08:00 · Ola:", "pytanie");
        html.Should().Contain("&lt;b&gt;dzięki&lt;/b&gt;").And.Contain("<blockquote").And.Contain("pytanie");
    }

    private static (InboxService Inbox, IDbContextFactory<PortalDbContext> Db) Build()
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
        services.AddScoped<CreditService>();
        services.AddScoped<AiService>();
        services.AddScoped<AssistantService>();
        services.AddScoped<InboxService>();
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<InboxService>(), sp.GetRequiredService<IDbContextFactory<PortalDbContext>>());
    }

    private static JsonElement Msg(string id, string from, string subject, string text, string? inReplyTo = null, double spam = 0) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { items = new[] { new {
            MessageId = id, InReplyTo = inReplyTo, From = new { Name = (string?)null, Address = from },
            To = new[] { new { Address = "kontakt@hantly.pl" } }, Subject = subject, ExtractedMarkdownMessage = text, SpamScore = spam } } })).RootElement;

    [Fact]
    public async Task Receive_groups_replies_into_one_thread_and_skips_duplicates()
    {
        var (inbox, dbf) = Build();
        (await inbox.ReceiveAsync(Msg("<a@x>", "ola@example.com", "Pytanie o plan", "pierwsza"))).Should().Be(1);
        (await inbox.ReceiveAsync(Msg("<b@x>", "ola@example.com", "Re: Pytanie o plan", "druga"))).Should().Be(1);
        (await inbox.ReceiveAsync(Msg("<b@x>", "ola@example.com", "Re: Pytanie o plan", "druga"))).Should().Be(0); // Brevo ponowiło
        (await inbox.ReceiveAsync(Msg("<c@x>", "jan@example.com", "Pytanie o plan", "inna osoba"))).Should().Be(1);

        await using var db = await dbf.CreateDbContextAsync();
        var threads = await db.InboxThreads.Include(t => t.Messages).OrderBy(t => t.Id).ToListAsync();
        threads.Should().HaveCount(2);
        threads[0].Messages.Should().HaveCount(2);
        threads[0].Unread.Should().Be(2);
        threads[0].Snippet.Should().Be("druga");
        threads[1].Counterpart.Should().Be("jan@example.com");
    }

    [Fact]
    public async Task Receive_follows_in_reply_to_even_with_changed_subject_and_files_spam()
    {
        var (inbox, dbf) = Build();
        await inbox.ReceiveAsync(Msg("<a@x>", "ola@example.com", "Pytanie", "pierwsza"));
        await inbox.ReceiveAsync(Msg("<b@x>", "ola@example.com", "Zupełnie inny temat", "dalej", inReplyTo: "<a@x>"));
        await inbox.ReceiveAsync(Msg("<s@x>", "spam@bad.example", "Wygrałeś", "kliknij", spam: 9));

        await using var db = await dbf.CreateDbContextAsync();
        var threads = await db.InboxThreads.Include(t => t.Messages).ToListAsync();
        threads.Should().HaveCount(2);
        threads.Single(t => t.Counterpart == "ola@example.com").Messages.Should().HaveCount(2);
        threads.Single(t => t.Counterpart == "spam@bad.example").Folder.Should().Be(InboxFolder.Spam);
    }

    [Fact]
    public async Task Webhook_secret_is_generated_and_checked()
    {
        var (inbox, _) = Build();
        (await inbox.SecretMatchesAsync("cokolwiek")).Should().BeFalse();
        var url = await inbox.WebhookUrlAsync("https://hantly.pl/");
        var secret = url.Split('/').Last();
        url.Should().StartWith("https://hantly.pl/api/inbound/brevo/");
        secret.Length.Should().BeGreaterThanOrEqualTo(32);
        (await inbox.SecretMatchesAsync(secret)).Should().BeTrue();
        (await inbox.SecretMatchesAsync(secret + "x")).Should().BeFalse();
    }
}
