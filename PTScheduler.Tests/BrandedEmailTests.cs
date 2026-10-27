using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Firmowy szablon wiadomości od zespołu platformy (szkice Leo).</summary>
public class BrandedEmailTests
{
    private const string Body = "Dzień dobry,\n\nZauważyliśmy, że kalendarz na najbliższy tydzień jest pusty.\n\n- serie wizyt\n- szablon tygodnia\n\nWięcej: https://hantly.pl/pomoc.\n\nPozdrawiamy,\nZespół Hantly";

    [Fact]
    public void Signature_from_text_is_replaced_by_template_signature()
    {
        EmailService.StripSignature(Body).Should().EndWith("https://hantly.pl/pomoc.");
        var html = EmailService.BrandedMessage(Body);
        html.Should().Contain(EmailService.LogoMarker);
        html.Should().NotContain("Pozdrawiamy");
        html.Split("Zespół Hantly").Length.Should().Be(2, "podpis zespołu pojawia się dokładnie raz — z szablonu");
    }

    [Fact]
    public void Lists_links_and_html_are_rendered_safely()
    {
        var html = EmailService.BrandedMessage(Body + "\n\n<script>x</script>");
        html.Should().Contain("<li style=\"margin:0 0 6px\">serie wizyt</li>");
        html.Should().Contain("href=\"https://hantly.pl/pomoc\"").And.Contain("</a>.");
        html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void Thanks_without_team_signature_stays_in_text()
    {
        EmailService.StripSignature("Dzień dobry,\n\nDziękujemy!").Should().EndWith("Dziękujemy!");
        EmailService.BrandedMessage("Treść", "Studio X").Should().Contain("Zespół Studio X");
    }
}
