using FluentAssertions;
using PTScheduler.Infrastructure.Services;
using Xunit;

namespace PTScheduler.Tests;

public class SafeHtmlTests
{
    [Fact]
    public void Removes_Scripts_And_Event_Handlers_Keeps_Formatting()
    {
        var html = SafeHtml.Sanitize("<p onclick=\"steal()\"><b>Tekst</b><script>alert(1)</script></p>");
        html.Should().Contain("<b>Tekst</b>").And.NotContain("script").And.NotContain("onclick");
    }

    [Fact]
    public void Keeps_Trusted_Video_Embeds_Only()
    {
        SafeHtml.Sanitize("<iframe src=\"https://www.youtube.com/embed/abc\"></iframe>").Should().Contain("iframe");
        SafeHtml.Sanitize("<iframe src=\"https://evil.example/x\"></iframe>").Should().NotContain("iframe");
        SafeHtml.Sanitize("<a href=\"javascript:alert(1)\">x</a>").Should().NotContain("javascript");
    }
}
