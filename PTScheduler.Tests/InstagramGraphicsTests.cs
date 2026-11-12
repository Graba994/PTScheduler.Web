using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

public class InstagramGraphicsTests
{
    private static readonly string? Out = Environment.GetEnvironmentVariable("IG_SHOTS");

    public static TheoryData<string, string, string> Cases => new()
    {
        { "tip", "fiolet", "post" }, { "list", "grafit", "post" }, { "myth", "jasny", "square" },
        { "question", "energia", "post" }, { "cta", "fiolet", "story" }, { "screen", "grafit", "post" }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Renders_jpeg_in_instagram_size(string template, string theme, string size)
    {
        var shot = InstagramGraphics.Photo(Png(390, 844), story: false).Jpeg;
        var jpeg = InstagramGraphics.Render(new InstagramGraphics.Spec
        {
            Template = template, Theme = theme, Size = size, Eyebrow = "Porada dla trenera 💪",
            Headline = template == "myth" ? "Klienci odchodzą przez cenę" : "3 sposoby, żeby klient nie odwołał treningu",
            Body = "Przypomnienie dzień wcześniej, stały termin i krótka wiadomość po treningu robią różnicę. 🚀",
            Points = ["Przypomnienie dzień wcześniej", "Stały termin co tydzień", "Wiadomość po treningu"],
            Index = 2, Count = 5, Handle = "hantly.pl", Domain = "hantly.pl"
        }, template == "screen" ? shot : null);

        InstagramRules.IsJpeg(jpeg).Should().BeTrue();
        InstagramRules.ImageSize(jpeg).Should().Be(InstagramGraphics.Pixels(size));
        if (Out is not null) File.WriteAllBytes(Path.Combine(Out, $"{template}-{theme}-{size}.jpg"), jpeg);
    }

    [Fact]
    public void Tall_screenshot_is_framed_and_normal_photo_kept()
    {
        var tall = InstagramGraphics.Photo(Png(390, 844), story: false);
        (tall.W, tall.H).Should().Be((1080, 1350));
        InstagramRules.ImageSize(tall.Jpeg).Should().Be((1080, 1350));
        var square = InstagramGraphics.Photo(Png(800, 800), story: false);
        (square.W, square.H).Should().Be((1080, 1080));
        var story = InstagramGraphics.Photo(Png(800, 800), story: true);
        (story.W, story.H).Should().Be((1080, 1920));
        if (Out is not null) File.WriteAllBytes(Path.Combine(Out, "photo-tall.jpg"), tall.Jpeg);
    }

    [Fact]
    public void Clean_strips_emoji_and_trims()
    {
        InstagramGraphics.Clean("Super 💪 trening 🚀!", 100).Should().Be("Super trening !");
        InstagramGraphics.Clean("Zażółć gęślą jaźń", 100).Should().Be("Zażółć gęślą jaźń");
        InstagramGraphics.Clean(new string('a', 50), 10).Should().HaveLength(10).And.EndWith("…");
    }

    private static uint Crc(byte[] d)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in d) { c ^= b; for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; }
        return ~c;
    }

    /// <summary>Prosty PNG w kolorze (bez bibliotek) — do testów ramki.</summary>
    internal static byte[] Png(int w, int h)
    {
        using var ms = new MemoryStream();
        void Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes(data.Length); Array.Reverse(len); ms.Write(len);
            var t = System.Text.Encoding.ASCII.GetBytes(type); ms.Write(t); ms.Write(data);
            var crc = BitConverter.GetBytes(Crc([.. t, .. data])); Array.Reverse(crc); ms.Write(crc);
        }
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
        ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk("IHDR", ihdr);
        var raw = new byte[h * (1 + w * 3)];
        for (var y = 0; y < h; y++)
        {
            var o = y * (1 + w * 3);
            for (var x = 0; x < w; x++) { raw[o + 1 + x * 3] = (byte)(x * 255 / w); raw[o + 2 + x * 3] = 120; raw[o + 3 + x * 3] = (byte)(y * 255 / h); }
        }
        using var z = new MemoryStream();
        using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Fastest, true)) zs.Write(raw);
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", []);
        return ms.ToArray();
    }
}
