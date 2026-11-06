using PTScheduler.Domain.Rules;
using Xunit;

namespace PTScheduler.Tests;

public class PlatformMailNoteTests
{
    [Fact]
    public void Footer_names_the_studio_and_reply_address()
    {
        var html = PlatformMailNote.Append("<html><body><p>Cześć</p></body></html>", "Anna Fit <Studio>", "anna@wp.pl");
        Assert.Contains("Anna Fit &lt;Studio&gt;", html);
        Assert.Contains("anna@wp.pl", html);
        Assert.Contains("przez Hantly", html);
        Assert.True(html.IndexOf(PlatformMailNote.Marker, StringComparison.Ordinal) < html.IndexOf("</body>", StringComparison.Ordinal));
    }

    [Fact]
    public void Footer_is_added_once_and_works_without_body_or_reply()
    {
        var once = PlatformMailNote.Append("<p>x</p>", "Studio", null);
        Assert.EndsWith("</div>", once);
        Assert.DoesNotContain("Odpowiedz", once);
        Assert.Equal(once, PlatformMailNote.Append(once, "Studio", null));
    }
}
