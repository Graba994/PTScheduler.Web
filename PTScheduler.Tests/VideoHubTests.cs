using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Training;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class VideoHubTests
{
    private const long GB = 1024L * 1024 * 1024;

    [Fact]
    public void Quota_is_plan_plus_addons_plus_bought_space_and_zero_without_video_in_plan()
    {
        VideoHubService.Quota(20, 10, 2.5m).Should().Be(32 * GB + GB / 2);
        VideoHubService.Quota(20, 0, 0).Should().Be(20 * GB);
        VideoHubService.Quota(0, 50, 10).Should().Be(0); // dodatek bez pakietu z wideo nic nie daje
        VideoHubService.Quota(int.MaxValue, 0, 0).Should().Be(long.MaxValue);
    }

    [Fact]
    public void File_fits_up_to_the_last_byte_but_not_one_more()
    {
        var quota = 5 * GB;
        VideoHubService.Fits(4 * GB, GB, quota).Should().BeTrue();
        VideoHubService.Fits(4 * GB, GB + 1, quota).Should().BeFalse();
        VideoHubService.Fits(0, 1, 0).Should().BeFalse();
        VideoHubService.Fits(0, 0, quota).Should().BeFalse();
        VideoHubService.Fits(long.MaxValue / 2, 10 * GB, long.MaxValue).Should().BeTrue();
    }

    [Fact]
    public void Upload_signature_follows_bunny_tus_formula()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("123" + "key" + "1700000000" + "guid-1"))).ToLowerInvariant();
        VideoHubService.Sign("123", "key", 1700000000, "guid-1").Should().Be(expected).And.HaveLength(64);
    }

    [Fact]
    public void Video_ref_keeps_library_and_falls_back_for_old_guids()
    {
        var r = VideoRef.Make("555", "a1b2-c3");
        VideoRef.Parse(r).Should().Be(("555", "a1b2-c3"));
        VideoRef.Guid(r).Should().Be("a1b2-c3");
        VideoRef.Embed(r, "999").Should().Be("https://iframe.mediadelivery.net/embed/555/a1b2-c3");
        VideoRef.Embed("old-guid", "999").Should().Be("https://iframe.mediadelivery.net/embed/999/old-guid");
        VideoRef.Embed("old-guid", null).Should().BeNull();
        VideoRef.Embed("555/<script>", null).Should().BeNull();
    }

    [Fact]
    public void Lesson_text_becomes_safe_paragraphs_and_reads_back()
    {
        var html = LessonText.ToHtml("Rozgrzewka 5 min\nPotem <b>przysiad</b>\n\nMateriały: https://example.com/plik.pdf");
        html.Should().Be("<p>Rozgrzewka 5 min<br>Potem &lt;b&gt;przysiad&lt;/b&gt;</p>" +
                         "<p>Materiały: <a href=\"https://example.com/plik.pdf\" target=\"_blank\" rel=\"noopener\">https://example.com/plik.pdf</a></p>");
        LessonText.FromHtml(html).Should().Be("Rozgrzewka 5 min\nPotem <b>przysiad</b>\n\nMateriały: https://example.com/plik.pdf");
        LessonText.ToHtml("  ").Should().BeNull();
        LessonText.FromHtml("<div style=\"color:red\">Stara <i>lekcja</i></div><p>druga</p>").Should().Be("Stara lekcja\n\ndruga");
    }
}
