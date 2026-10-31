using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using SkiaSharp;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Logo w mailach: PNG w samej wiadomości (cid), SVG zastępuje nazwa studia, Portal dokłada logo Hantly.</summary>
public class EmailLogoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "logo-" + Guid.NewGuid().ToString("N"));

    public EmailLogoTests() => Directory.CreateDirectory(Path.Combine(_root, "branding"));
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.OrangeRed);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private const string Svg = """<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><rect width="10" height="10"/></svg>""";

    [Fact]
    public void Raster_logo_becomes_small_png_and_svg_is_skipped()
    {
        var png = EmailLogo.Render(Png(1200, 300))!;
        using var bmp = SKBitmap.Decode(png);
        bmp.Width.Should().BeLessThanOrEqualTo(360);
        bmp.Height.Should().BeLessThanOrEqualTo(120);
        EmailLogo.Render(System.Text.Encoding.UTF8.GetBytes(Svg)).Should().BeNull();
    }

    [Fact]
    public void Reads_logo_from_web_root_but_not_outside_it()
    {
        File.WriteAllBytes(Path.Combine(_root, "branding", "logo.png"), Png(100, 40));
        EmailLogo.Png(_root, "/branding/logo.png?v=3").Should().NotBeNull();
        EmailLogo.Png(_root, "/../../etc/passwd").Should().BeNull();
        EmailLogo.Png(_root, null).Should().BeNull();
    }

    [Fact]
    public async Task Template_uses_inline_logo_or_studio_name()
    {
        File.WriteAllBytes(Path.Combine(_root, "branding", "logo.png"), Png(200, 80));
        File.WriteAllText(Path.Combine(_root, "branding", "logo.svg"), Svg);

        var withPng = await RenderAsync("/branding/logo.png");
        withPng.Should().Contain($"src=\"{EmailLogo.Src}\"").And.NotContain("/branding/logo.png\"");

        var withSvg = await RenderAsync("/branding/logo.svg");
        withSvg.Should().NotContain("<img").And.Contain("Studio &amp; Siła");
    }

    [Fact]
    public void Portal_marker_turns_into_hantly_logo_only_where_present()
    {
        var (html, images) = PTScheduler.Portal.Services.EmailService.WithLogo($"<body>{PTScheduler.Portal.Services.EmailService.LogoMarker}<p>Hej</p></body>", null);
        html.Should().NotContain(PTScheduler.Portal.Services.EmailService.LogoMarker);
        (html.Contains("cid:hantly-logo") ? images!.Count : 1).Should().Be(1, "logo jako obrazek w wiadomości albo napis Hantly, gdy brak pliku");

        var relay = "<p>Mail od trenera</p>";
        PTScheduler.Portal.Services.EmailService.WithLogo(relay, null).Html.Should().Be(relay);
    }

    private async Task<string> RenderAsync(string logoPath)
    {
        var name = Guid.NewGuid().ToString();
        var db = new Mock<IDbContextFactory<ApplicationDbContext>>();
        db.Setup(f => f.CreateDbContext()).Returns(() => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options));
        var branding = new Mock<IBrandingService>();
        branding.Setup(b => b.GetAsync()).ReturnsAsync(new AppBrandingDto { CompanyName = "Studio & Siła", LogoPath = logoPath });
        var root = new Mock<IWebRootPathProvider>();
        root.SetupGet(r => r.WebRootPath).Returns(_root);
        var svc = new EmailTemplateService(db.Object, branding.Object, root.Object);
        var (_, html) = await svc.RenderAsync("new-device-login", new Dictionary<string, string> { ["Name"] = "Ola" });
        return html;
    }
}
