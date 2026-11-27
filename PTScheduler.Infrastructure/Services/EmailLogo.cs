using SkiaSharp;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Logo studia w e-mailach. Wysyłamy je jako obrazek w samej wiadomości (<c>cid:brand-logo</c>), a nie link:
/// skrzynki nie pobierają wtedy niczego z serwera trenera (adres względny albo za logowaniem kończył się
/// pustą ikonką), a Gmail i Outlook nie pokazują SVG ani często WebP — dlatego zawsze PNG, najwyżej 360×120.
/// Logo w SVG nie da się tu przerobić — wtedy nagłówek maila pokazuje samą nazwę studia.
/// </summary>
public static class EmailLogo
{
    public const string ContentId = "brand-logo";
    public const string Src = "cid:" + ContentId;
    private const int MaxWidth = 360, MaxHeight = 120;

    private static readonly object Gate = new();
    private static (string Key, byte[]? Png) _cache = ("", null);

    /// <summary>PNG logo do maila albo null (brak logo, SVG, uszkodzony plik).</summary>
    public static byte[]? Png(string webRootPath, string? logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath)) return null;
        var rel = logoPath.Split('?')[0].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(webRootPath, rel));
        if (!full.StartsWith(Path.GetFullPath(webRootPath), StringComparison.Ordinal) || !File.Exists(full)) return null;

        var key = full + "|" + File.GetLastWriteTimeUtc(full).Ticks;
        lock (Gate)
        {
            if (_cache.Key == key) return _cache.Png;
        }
        byte[]? png;
        try { png = Render(File.ReadAllBytes(full)); }
        catch (IOException) { png = null; }
        lock (Gate) _cache = (key, png);
        return png;
    }

    /// <summary>Obraz rastrowy (PNG, JPG, WebP, GIF…) → PNG zmniejszony do rozmiaru nagłówka maila; SVG → null.</summary>
    public static byte[]? Render(byte[] input)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(input));
        if (codec is null) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0) return null;

        var scale = Math.Min(1f, Math.Min((float)MaxWidth / bitmap.Width, (float)MaxHeight / bitmap.Height));
        var w = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var h = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return null;
        surface.Canvas.Clear(SKColors.Transparent);
        using (var image = SKImage.FromBitmap(bitmap))
            surface.Canvas.DrawImage(image, SKRect.Create(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        surface.Canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }
}
