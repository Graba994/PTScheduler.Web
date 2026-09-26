using SkiaSharp;

namespace PTScheduler.Infrastructure.Services.Photos;

/// <summary>
/// Zmniejsza i przekodowuje zdjęcia do WebP. Przekodowanie usuwa wszystkie
/// metadane (EXIF, w tym lokalizację GPS), a orientację z EXIF „wypala” w pikselach,
/// żeby zdjęcie z telefonu nie leżało bokiem.
/// </summary>
public static class PhotoCompressor
{
    public sealed record Result(byte[] Data, int Width, int Height);

    /// <exception cref="InvalidDataException">Plik nie jest obsługiwanym obrazem.</exception>
    public static Result Compress(byte[] input, int maxEdge, int quality)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(input))
            ?? throw new InvalidDataException("Nieobsługiwany format pliku.");
        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("Nie udało się odczytać obrazu.");

        using var oriented = ApplyOrigin(decoded, codec.EncodedOrigin);
        var src = oriented ?? decoded;

        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(src.Width, src.Height));
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));

        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidDataException("Obraz jest zbyt duży.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using (var img = SKImage.FromBitmap(src))
            canvas.DrawImage(img, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        canvas.Flush();

        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Webp, quality)
            ?? throw new InvalidDataException("Nie udało się zakodować obrazu.");
        return new Result(data.ToArray(), w, h);
    }

    private static SKBitmap? ApplyOrigin(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default) return null;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? bmp.Height : bmp.Width, swap ? bmp.Width : bmp.Height);
        using var c = new SKCanvas(result);
        var (w, h) = (bmp.Width, bmp.Height);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: c.Scale(-1, 1); c.Translate(-w, 0); break;
            case SKEncodedOrigin.BottomRight: c.RotateDegrees(180); c.Translate(-w, -h); break;
            case SKEncodedOrigin.BottomLeft: c.Scale(1, -1); c.Translate(0, -h); break;
            case SKEncodedOrigin.LeftTop: c.RotateDegrees(90); c.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: c.Translate(h, 0); c.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: c.Translate(h, w); c.RotateDegrees(90); c.Scale(-1, 1); c.Translate(-w, 0); break;
            case SKEncodedOrigin.LeftBottom: c.Translate(0, w); c.RotateDegrees(-90); break;
        }
        c.DrawBitmap(bmp, 0, 0);
        return result;
    }
}
