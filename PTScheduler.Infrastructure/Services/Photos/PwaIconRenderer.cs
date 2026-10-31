using SkiaSharp;

namespace PTScheduler.Infrastructure.Services.Photos;

/// <summary>
/// Ikona aplikacji (PWA) w dokładnym, kwadratowym rozmiarze. Chrome sprawdza, czy ikona z manifestu
/// ma zadeklarowany rozmiar — ikona „udająca” 512×512 potrafi zablokować instalację jednym dotknięciem.
/// Obraz jest wpisywany w kwadrat (bez przycinania) na przezroczystym tle.
/// </summary>
public static class PwaIconRenderer
{
    /// <returns>PNG albo null, gdy plik nie jest obsługiwanym obrazem rastrowym (np. SVG).</returns>
    public static byte[]? RenderSquarePng(byte[] input, int size)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(input));
        if (codec is null) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var scale = Math.Min((float)size / bitmap.Width, (float)size / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        var dest = SKRect.Create((size - w) / 2f, (size - h) / 2f, w, h);
        using (var image = SKImage.FromBitmap(bitmap))
            canvas.DrawImage(image, dest, new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }
}
