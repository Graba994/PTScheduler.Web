using FluentAssertions;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>„Pierwsze ustawienia”: rozpoznanie poczty po adresie i kontakt na stronie głównej.</summary>
public class FirstSetupHelpersTests
{
    [Theory]
    [InlineData("anna@gmail.com", "Gmail", "smtp.gmail.com", 587)]
    [InlineData("Anna@WP.pl", "Wp", "smtp.wp.pl", 465)]
    [InlineData("studio@op.pl", "Onet", "smtp.poczta.onet.pl", 465)]
    [InlineData("studio@interia.pl", "Interia", "poczta.interia.pl", 465)]
    [InlineData("studio@o2.pl", "O2", "poczta.o2.pl", 465)]
    [InlineData("kontakt@annafit.pl", "hosting", "mail.annafit.pl", 465)]
    public void Detects_provider_from_address(string email, string key, string host, int port)
    {
        var p = MailProviderRules.Detect(email)!;
        p.Key.Should().Be(key);
        p.Host.Should().Be(host);
        p.Port.Should().Be(port);
        p.Steps.Should().NotBeEmpty();
    }

    [Fact]
    public void Own_domain_can_be_google_workspace()
    {
        MailProviderRules.Detect("kontakt@annafit.pl", workspace: true)!.Key.Should().Be("GoogleWorkspace");
        MailProviderRules.Detect("kontakt@annafit.pl")!.CustomHost.Should().BeTrue();
        MailProviderRules.Detect("anna@gmail.com", workspace: true)!.Key.Should().Be("Gmail", "znana domena ma pierwszeństwo");
    }

    [Theory]
    [InlineData("anna@outlook.com")]
    [InlineData("anna@icloud.com")]
    public void Mailboxes_without_password_smtp_are_not_offered(string email)
    {
        MailProviderRules.Detect(email).Should().BeNull();
        MailProviderRules.IsUnsupported(email).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("anna")]
    [InlineData("anna@")]
    public void Incomplete_address_gives_no_provider(string email) => MailProviderRules.Detect(email).Should().BeNull();

    [Theory]
    [InlineData("@anna.fit", "anna.fit")]
    [InlineData("https://instagram.com/anna.fit/", "anna.fit")]
    [InlineData("instagram.com/anna_fit?igsh=1", "anna_fit")]
    [InlineData("anna fit", null)]
    [InlineData("", null)]
    public void Instagram_handle_is_normalised(string value, string? expected) => SiteWidgets.InstagramHandle(value).Should().Be(expected);

    [Fact]
    public void Contact_replaces_template_placeholders_and_keeps_real_items()
    {
        var site = SiteWidgets.BuildTemplate("personal");
        var contact = site.Blocks.First(b => b.Type == "contact");
        contact.Items.Add(new SiteBlockItem { Icon = "bi-facebook", Title = "Facebook", Value = "Anna Fit", Url = "https://facebook.com/annafit" });

        SiteWidgets.ApplyContact(site, "600 100 200", "anna@annafit.pl", "@anna.fit");
        SiteWidgets.ApplyContact(site, "600 100 200", "anna@annafit.pl", "@anna.fit"); // powtórka nie dubluje

        contact.Items.Select(i => i.Icon).Should().Equal("bi-telephone", "bi-envelope", "bi-instagram", "bi-facebook");
        contact.Items.Should().NotContain(i => SiteWidgets.IsPlaceholderContact(i));
        contact.Items[0].Url.Should().Be("tel:600100200");
        contact.Items[2].Url.Should().Be("https://instagram.com/anna.fit");
        site.Blocks.Where(b => b.CtaUrl?.StartsWith("tel:") == true).Should().OnlyContain(b => b.CtaUrl == "tel:600100200");
    }

    [Fact]
    public void Contact_without_phone_sends_call_buttons_to_booking()
    {
        var site = SiteWidgets.BuildTemplate("senior");
        SiteWidgets.ApplyContact(site, null, "anna@annafit.pl", null);
        site.Blocks.Should().NotContain(b => b.CtaUrl != null && b.CtaUrl.StartsWith("tel:"));
        site.Blocks.First(b => b.Type == "contact").Items.Should().ContainSingle(i => i.Icon == "bi-envelope");
    }
}
