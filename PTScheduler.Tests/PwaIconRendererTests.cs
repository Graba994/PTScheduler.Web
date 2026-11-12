using FluentAssertions;
using PTScheduler.Infrastructure.Services.Photos;
using SkiaSharp;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>Ikona PWA musi mieć dokładnie zadeklarowany rozmiar — inaczej Chrome nie proponuje instalacji.</summary>
public class PwaIconRendererTests
{
    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.OrangeRed);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Theory]
    [InlineData(300, 150, 512)]
    [InlineData(1024, 1024, 192)]
    [InlineData(64, 200, 512)]
    public void Renders_Exact_Square(int w, int h, int size)
    {
        var png = PwaIconRenderer.RenderSquarePng(Png(w, h), size);

        png.Should().NotBeNull();
        using var decoded = SKBitmap.Decode(png);
        decoded.Width.Should().Be(size);
        decoded.Height.Should().Be(size);
    }

    [Fact]
    public void Wide_Image_Is_Letterboxed_Not_Cropped()
    {
        using var decoded = SKBitmap.Decode(PwaIconRenderer.RenderSquarePng(Png(400, 200), 512));
        decoded.GetPixel(256, 5).Alpha.Should().Be(0);    // przezroczysty pas u góry
        decoded.GetPixel(256, 256).Alpha.Should().Be(255); // obraz w środku
        decoded.GetPixel(2, 256).Alpha.Should().Be(255);   // pełna szerokość zachowana
    }

    [Fact]
    public void Svg_Or_Garbage_Returns_Null() =>
        PwaIconRenderer.RenderSquarePng("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), 512).Should().BeNull();
}
