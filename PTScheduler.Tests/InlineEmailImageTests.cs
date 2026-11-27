using FluentAssertions;
using MimeKit;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class InlineEmailImageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    [Fact]
    public void Html_with_inline_image_becomes_multipart_related_with_content_id()
    {
        var body = SmtpEmailService.Body("<img src=\"cid:hantly\">", [new EmailInlineImage("hantly", Png, "image/png")]);
        var related = body.Should().BeOfType<MultipartRelated>().Subject;
        var image = related.OfType<MimePart>().Single(p => p.ContentType.MimeType == "image/png");
        image.ContentId.Should().Be("hantly");
        related.OfType<TextPart>().Single().Text.Should().Contain("cid:hantly");
    }

    [Fact]
    public void Html_without_images_stays_a_plain_html_part() =>
        SmtpEmailService.Body("<p>Hej</p>", null).Should().BeOfType<TextPart>();

    [Fact]
    public void Portal_accepts_small_images_and_rejects_bad_ones()
    {
        var ok = new MailRelayImage("hantly", "image/png", Convert.ToBase64String(Png));
        TenantMailRelay.TryImages([ok], out var images, out var error).Should().BeTrue();
        images!.Single().Data.Should().Equal(Png);
        error.Should().BeNull();

        TenantMailRelay.TryImages(null, out images, out _).Should().BeTrue();
        images.Should().BeNull();

        TenantMailRelay.TryImages([ok with { ContentType = "image/svg+xml" }], out _, out error).Should().BeFalse();
        TenantMailRelay.TryImages([ok with { ContentId = "a b<script>" }], out _, out error).Should().BeFalse();
        TenantMailRelay.TryImages([ok with { Data = "nie-base64!" }], out _, out error).Should().BeFalse();
        TenantMailRelay.TryImages([ok, ok, ok, ok], out _, out error).Should().BeFalse();
        var huge = Convert.ToBase64String(new byte[TenantMailRelay.MaxImageBytes + 1]);
        TenantMailRelay.TryImages([ok with { Data = huge }], out _, out error).Should().BeFalse();
        error.Should().Be("Obrazek jest za duży.");
    }
}
