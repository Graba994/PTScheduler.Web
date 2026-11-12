using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Grafiki na Instagram w stylu marki, renderowane na serwerze (QuestPDF/Skia → JPEG 1080 px):
/// szablony (porada, lista, mit/fakt, pytanie, zachęta, zrzut ekranu) w kilku motywach kolorów, z Hantelkiem.
/// Działa bez AI i bez przeglądarki — dlatego autopilot może sam przygotować post.
/// </summary>
public static class InstagramGraphics
{
    static InstagramGraphics() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Px → pt przy 144 dpi (1080 px = 540 pt).</summary>
    private const float Scale = 72f / 144f;
    private const int Dpi = 144;

    public sealed record Theme(string Key, string Label, string From, string To, string Ink, string Muted, string Accent, string AccentInk);

    public static readonly Theme[] Themes =
    [
        new("fiolet", "Fiolet", "#3B0764", "#7C3AED", "#FFFFFF", "#DDD6FE", "#FDE68A", "#3B0764"),
        new("grafit", "Grafit", "#0B1120", "#1E293B", "#F8FAFC", "#94A3B8", "#A78BFA", "#0B1120"),
        new("jasny", "Jasny", "#FAF7FF", "#EDE9FE", "#1E1B4B", "#5B5487", "#7C3AED", "#FFFFFF"),
        new("energia", "Energia", "#F7FEE7", "#D9F99D", "#1A2E05", "#4D6620", "#4C1D95", "#FFFFFF"),
    ];

    public sealed record Template(string Key, string Label, string Hint);

    public static readonly Template[] Templates =
    [
        new("tip", "Porada", "nagłówek i krótki tekst"),
        new("list", "Lista", "nagłówek i 2–5 punktów"),
        new("myth", "Mit i fakt", "mit w nagłówku, fakt w tekście"),
        new("question", "Pytanie", "pytanie do obserwujących"),
        new("cta", "Zachęta", "na koniec karuzeli: co zrobić dalej"),
        new("screen", "Zrzut ekranu", "nagłówek i zrzut z aplikacji"),
    ];

    public static Theme FindTheme(string? key) => Themes.FirstOrDefault(t => t.Key == key) ?? Themes[0];

    /// <summary>Treść grafiki. Size: post (4:5), square (1:1), story (9:16).</summary>
    public sealed record Spec
    {
        public string Template { get; init; } = "tip";
        public string Theme { get; init; } = "fiolet";
        public string Size { get; init; } = "post";
        public string? Eyebrow { get; init; }
        public string Headline { get; init; } = "";
        public string? Body { get; init; }
        public List<string>? Points { get; init; }
        public string Mood { get; init; } = "happy";
        public bool Mascot { get; init; } = true;
        public int? Index { get; init; }
        public int? Count { get; init; }
        public string? Handle { get; init; }
        public string? Domain { get; init; }
    }

    public static (int W, int H) Pixels(string size) => size switch
    {
        "story" => (1080, 1920),
        "square" => (1080, 1080),
        _ => (1080, 1350)
    };

    /// <summary>Grafika z szablonu. <paramref name="screenshot"/> — obrazek do szablonu „Zrzut ekranu”.</summary>
    public static byte[] Render(Spec s, byte[]? screenshot = null)
    {
        var t = FindTheme(s.Theme);
        var (w, h) = Pixels(s.Size);
        var story = s.Size == "story";
        var doc = Document.Create(c => c.Page(page =>
        {
            page.Size(w * Scale, h * Scale, Unit.Point);
            page.Margin(0);
            page.DefaultTextStyle(x => x.FontFamily("Lato").FontColor(t.Ink));
            page.Background().Svg(Background(t, w, h));
            var pad = story ? 48f : 40f;
            // Góra: etykieta i numer slajdu.
            page.Header().PaddingHorizontal(pad).PaddingTop(story ? 96 : 40).Row(r =>
            {
                var eyebrow = Clean(s.Eyebrow, 40);
                if (s.Template == "myth" && string.IsNullOrEmpty(eyebrow)) eyebrow = "Mit czy fakt?";
                if (!string.IsNullOrEmpty(eyebrow))
                    r.AutoItem().Background(t.Accent).CornerRadius(16).PaddingVertical(6).PaddingHorizontal(14)
                        .Text(eyebrow.ToUpper(new CultureInfo("pl-PL"))).FontSize(13).Bold().LetterSpacing(0.08f).FontColor(t.AccentInk);
                r.RelativeItem();
                if (s.Index is int i && s.Count is int n && n > 1)
                    r.AutoItem().AlignMiddle().Text($"{i}/{n}").FontSize(15).SemiBold().FontColor(t.Muted);
            });
            // Środek: treść (zmniejsza się sama, gdy jest jej za dużo).
            var top = s.Template is "list" or "screen";
            var content = page.Content().PaddingHorizontal(pad).PaddingVertical(story ? 40 : 22);
            (top ? content.AlignTop() : content.AlignMiddle()).ScaleToFit().Element(body => Body(body, s, t, screenshot));
            // Dół: adres i Hantelek.
            page.Footer().PaddingHorizontal(pad).PaddingBottom(story ? 120 : 34).Row(r =>
            {
                r.RelativeItem().AlignBottom().Column(f =>
                {
                    var handle = Clean(s.Handle, 40);
                    if (!string.IsNullOrEmpty(handle)) f.Item().Text("@" + handle.TrimStart('@')).FontSize(15).Bold();
                    var domain = Clean(s.Domain, 60);
                    if (!string.IsNullOrEmpty(domain)) f.Item().Text(domain).FontSize(13).FontColor(t.Muted);
                });
                if (s.Mascot && s.Template != "screen")
                    r.ConstantItem(story ? 140 : 104).AlignBottom().Svg(MascotArt.Svg(s.Template == "question" ? "think" : s.Mood));
            });
        }));
        return doc.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Jpeg, RasterDpi = Dpi, ImageCompressionQuality = ImageCompressionQuality.High }).First();
    }

    private static void Body(IContainer box, Spec s, Theme t, byte[]? screenshot)
    {
        var headline = Clean(s.Headline, 160);
        var body = Clean(s.Body, 420);
        var hSize = headline.Length switch { <= 30 => 58f, <= 50 => 50f, <= 80 => 44f, <= 120 => 38f, _ => 32f };
        box.Column(col =>
        {
            switch (s.Template)
            {
                case "myth":
                    col.Item().Text("MIT").FontSize(16).Black().LetterSpacing(0.12f).FontColor("#F87171");
                    col.Item().PaddingTop(4).Text(headline).FontSize(hSize - 4).ExtraBold().LineHeight(1.1f).Strikethrough().DecorationColor("#F87171");
                    col.Item().PaddingVertical(16).LineHorizontal(1.5f).LineColor(t.Muted);
                    col.Item().Text("FAKT").FontSize(16).Black().LetterSpacing(0.12f).FontColor(t.Accent == "#FFFFFF" ? "#22C55E" : t.Accent);
                    if (!string.IsNullOrEmpty(body)) col.Item().PaddingTop(4).Text(body).FontSize(23).LineHeight(1.35f);
                    break;

                case "list":
                    col.Item().Text(headline).FontSize(hSize - 2).Black().LineHeight(1.08f);
                    var n = 0;
                    foreach (var p in (s.Points ?? []).Select(x => Clean(x, 120)).Where(x => x.Length > 0).Take(5))
                    {
                        n++;
                        col.Item().PaddingTop(n == 1 ? 18 : 10).Row(r =>
                        {
                            r.ConstantItem(38).Height(38).Background(t.Accent).CornerRadius(19).AlignCenter().AlignMiddle()
                                .Text(n.ToString()).FontSize(18).Black().FontColor(t.AccentInk);
                            r.RelativeItem().PaddingLeft(12).AlignMiddle().Text(p).FontSize(22).LineHeight(1.3f);
                        });
                    }
                    if (!string.IsNullOrEmpty(body)) col.Item().PaddingTop(16).Text(body).FontSize(19).FontColor(t.Muted).LineHeight(1.35f);
                    break;

                case "question":
                    col.Item().PaddingTop(20).Text(headline).FontSize(hSize + 2).Black().LineHeight(1.08f);
                    if (!string.IsNullOrEmpty(body)) col.Item().PaddingTop(14).Text(body).FontSize(23).FontColor(t.Muted).LineHeight(1.35f);
                    col.Item().PaddingTop(22).Row(r => r.AutoItem().Border(1.5f).BorderColor(t.Ink).CornerRadius(18).PaddingVertical(7).PaddingHorizontal(16)
                        .Text("Odpowiedz w komentarzu").FontSize(16).Bold());
                    break;

                case "cta":
                    col.Item().PaddingTop(20).Text(headline).FontSize(hSize + 2).Black().LineHeight(1.08f);
                    if (!string.IsNullOrEmpty(body)) col.Item().PaddingTop(14).Text(body).FontSize(23).LineHeight(1.35f);
                    var domain = Clean(s.Domain, 60);
                    if (!string.IsNullOrEmpty(domain))
                        col.Item().PaddingTop(24).Row(r => r.AutoItem().Background(t.Accent).CornerRadius(22).PaddingVertical(10).PaddingHorizontal(22)
                            .Text(domain).FontSize(22).Black().FontColor(t.AccentInk));
                    break;

                case "screen":
                    col.Item().Text(headline).FontSize(hSize - 6).Black().LineHeight(1.08f);
                    if (!string.IsNullOrEmpty(body)) col.Item().PaddingTop(8).Text(body).FontSize(19).FontColor(t.Muted).LineHeight(1.3f);
                    if (screenshot is { Length: > 0 })
                        col.Item().PaddingTop(18).AlignCenter().Height(s.Size == "story" ? 900 : s.Size == "square" ? 300 : 420).Image(screenshot).FitArea();
                    break;

                default:
                    col.Item().Text(headline).FontSize(hSize).Black().LineHeight(1.08f);
                    if (!string.IsNullOrEmpty(body)) col.Item().PaddingTop(16).Text(body).FontSize(24).LineHeight(1.38f);
                    break;
            }
        });
    }

    /// <summary>
    /// Zdjęcie użytkownika jako JPEG gotowy dla Instagrama: w dopuszczalnych proporcjach — na całą grafikę,
    /// inaczej (np. pionowy zrzut z telefonu) — wyśrodkowane na tle marki.
    /// </summary>
    public static (byte[] Jpeg, int W, int H) Photo(byte[] image, bool story, string theme = "grafit")
    {
        var (iw, ih) = InstagramRules.ImageSize(image);
        var (w, h, framed) = InstagramRules.TargetSize(iw, ih, story);
        var t = FindTheme(theme);
        var doc = Document.Create(c => c.Page(page =>
        {
            page.Size(w * Scale, h * Scale, Unit.Point);
            page.Margin(0);
            if (framed)
            {
                page.Background().Svg(Background(t, w, h));
                page.Content().Padding(story ? 60 : 36).AlignCenter().AlignMiddle().Image(image).FitArea();
            }
            else page.Content().Image(image).FitUnproportionally();
        }));
        var jpeg = doc.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Jpeg, RasterDpi = Dpi, ImageCompressionQuality = ImageCompressionQuality.VeryHigh }).First();
        return (jpeg, w, h);
    }

    private static string Background(Theme t, int w, int h)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" preserveAspectRatio="none">""");
        sb.Append($"""<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{t.From}"/><stop offset="1" stop-color="{t.To}"/></linearGradient></defs>""");
        sb.Append(CultureInfo.InvariantCulture, $"""<rect width="{w}" height="{h}" fill="url(#g)"/>""");
        // Talerz hantla w rogu — delikatny znak marki.
        sb.Append(CultureInfo.InvariantCulture, $"""<circle cx="{w - 40}" cy="80" r="190" fill="{t.Accent}" opacity=".10"/><circle cx="{w - 40}" cy="80" r="120" fill="{t.Accent}" opacity=".08"/>""");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Bez emotek i znaków spoza czcionki (na grafice wyszłyby kwadraty), przycięte do długości.</summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var r in text.EnumerateRunes())
        {
            if (r.Value > 0x2FFF && r.Value is not (>= 0xFB00 and <= 0xFB06)) continue;
            if (Rune.GetUnicodeCategory(r) is UnicodeCategory.OtherSymbol or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse) continue;
            sb.Append(r.ToString());
        }
        var s = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        return s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
    }
}
