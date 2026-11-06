using System.Globalization;
using System.Text;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.Medals;

/// <summary>Kolory i motyw do rysowania medali: kolory marki (motyw aplikacji) i motyw strony głównej trenera.</summary>
public sealed record MedalPalette(string Primary, string Secondary, string Bg, string Surface, string Ink, string Accent, bool Dark, string SiteTheme);

/// <summary>
/// Grafiki medali (SVG 120×120) generowane z kolorów trenera — bez plików. Style: klasyczny medal na wstążce,
/// tarcza, odznaka (heksagon), minimal oraz „w moim stylu strony” (kształt, kolory i krój z motywu strony głównej).
/// Metal (brąz, srebro, złoto) mówi o randze medalu.
/// </summary>
public static class MedalArt
{
    public sealed record StyleInfo(string Key, string Name, string Description);

    public const string SiteStyle = "strona";

    public static readonly IReadOnlyList<StyleInfo> Styles =
    [
        new("klasyczny", "Klasyczny", "Krążek z metalu na wstążce w kolorach marki"),
        new("tarcza", "Tarcza", "Herb w kolorach marki z metalową obwódką"),
        new("odznaka", "Odznaka", "Heksagon jak naszywka z banerem"),
        new("minimal", "Minimal", "Cienki pierścień i dużo bieli"),
        new(SiteStyle, "W moim stylu strony", "Kształt, kolory i krój z motywu Twojej strony głównej"),
    ];

    public static bool IsStyle(string? key) => Styles.Any(s => s.Key == key);

    /// <summary>Kolory marki z motywu aplikacji (te same co w „Wygląd aplikacji”).</summary>
    public static (string Primary, string Secondary) BrandColors(string? appTheme)
    {
        var t = appTheme ?? "ocean";
        if (t.EndsWith("-dark", StringComparison.Ordinal)) t = t[..^5];
        return t switch
        {
            "forest" => ("#16A34A", "#10B981"),
            "slate" => ("#475569", "#64748B"),
            "teal" => ("#0D9488", "#06B6D4"),
            "sunset" => ("#EA580C", "#FBBF24"),
            "crimson" => ("#DC2626", "#F97316"),
            "lavender" => ("#9333EA", "#EC4899"),
            "amber" => ("#D97706", "#F59E0B"),
            "indigo" => ("#4F46E5", "#7C3AED"),
            "rose" => ("#E11D48", "#F43F5E"),
            _ => ("#0284C7", "#06B6D4"),
        };
    }

    public static MedalPalette Palette(string? appTheme, string? siteTheme, string? accentOverride)
    {
        var (primary, secondary) = BrandColors(appTheme);
        var th = SiteWidgets.ThemeOf(siteTheme);
        var accent = !string.IsNullOrWhiteSpace(accentOverride) ? accentOverride! : string.IsNullOrEmpty(th.Accent) ? primary : th.Accent;
        return new(primary, secondary, th.Bg, th.Surface, th.Ink, accent, th.Dark, th.Key);
    }

    private sealed record Metal(string Light, string Mid, string Dark, string Name);

    private static Metal MetalOf(MedalTier tier) => tier switch
    {
        MedalTier.Gold => new("#FCE7A2", "#E2B33F", "#8A6514", "ZŁOTO"),
        MedalTier.Silver => new("#F8FAFC", "#B6C0CB", "#56616E", "SREBRO"),
        _ => new("#F6C79C", "#C47A3A", "#6E3B12", "BRĄZ"),
    };

    public static string DataUri(MedalRules.Medal medal, string style, MedalPalette p) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Svg(medal, style, p)));

    public static string Svg(MedalRules.Medal medal, string style, MedalPalette p)
    {
        var m = MetalOf(medal.Tier);
        var body = style switch
        {
            "tarcza" => Shield(medal, m, p),
            "odznaka" => Hexagon(medal, m, p),
            "minimal" => Minimal(medal, m, p),
            SiteStyle => Site(medal, m, p),
            _ => Classic(medal, m, p),
        };
        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">{body}</svg>""";
    }

    private static string MetalGradient(string id, Metal m) =>
        $"""<linearGradient id="{id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{m.Light}"/><stop offset=".55" stop-color="{m.Mid}"/><stop offset="1" stop-color="{m.Dark}"/></linearGradient>""";

    private static string Classic(MedalRules.Medal medal, Metal m, MedalPalette p) => $"""
        <defs>{MetalGradient("mt", m)}<radialGradient id="mi" cx=".35" cy=".3" r=".8"><stop offset="0" stop-color="{m.Light}"/><stop offset="1" stop-color="{m.Mid}"/></radialGradient></defs>
        <polygon points="36,0 58,0 66,50 44,50" fill="{p.Primary}"/>
        <polygon points="62,0 84,0 76,50 54,50" fill="{p.Secondary}"/>
        <polygon points="58,0 62,0 60,40" fill="#000" opacity=".12"/>
        <ellipse cx="60" cy="112" rx="26" ry="3.5" fill="#0F172A" opacity=".12"/>
        <circle cx="60" cy="74" r="36" fill="url(#mt)"/>
        <circle cx="60" cy="74" r="29" fill="url(#mi)" stroke="{m.Dark}" stroke-opacity=".35" stroke-width="1.5"/>
        <path d="M38 62 A26 26 0 0 1 70 50" stroke="#fff" stroke-opacity=".55" stroke-width="3" fill="none" stroke-linecap="round"/>
        {Glyph(medal, 60, 75, 30, m.Dark, "Arial Black, Arial, sans-serif", 900)}
        """;

    private static string Shield(MedalRules.Medal medal, Metal m, MedalPalette p) => $"""
        <defs>{MetalGradient("mt", m)}<linearGradient id="br" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{p.Primary}"/><stop offset="1" stop-color="{p.Secondary}"/></linearGradient></defs>
        <ellipse cx="60" cy="114" rx="28" ry="3.5" fill="#0F172A" opacity=".12"/>
        <path d="M60 6 L102 20 V56 C102 84 84 102 60 112 C36 102 18 84 18 56 V20 Z" fill="url(#mt)"/>
        <path d="M60 14 L94 25.5 V56 C94 79 79 94 60 103 C41 94 26 79 26 56 V25.5 Z" fill="url(#br)"/>
        <path d="M60 14 L94 25.5 V40 C78 34 44 36 26 46 V25.5 Z" fill="#fff" opacity=".14"/>
        {Stars(medal.Tier, 60, 30, 5, m.Light)}
        {Glyph(medal, 60, 66, 30, "#fff", "Arial Black, Arial, sans-serif", 900)}
        """;

    private static string Hexagon(MedalRules.Medal medal, Metal m, MedalPalette p)
    {
        static string Hex(double r, double cy) => string.Join(" ", Enumerable.Range(0, 6).Select(i =>
        {
            var a = Math.PI / 180 * (60 * i - 90);
            return $"{F(60 + r * Math.Cos(a))},{F(cy + r * Math.Sin(a))}";
        }));
        return $"""
            <defs>{MetalGradient("mt", m)}</defs>
            <ellipse cx="60" cy="113" rx="28" ry="3.5" fill="#0F172A" opacity=".12"/>
            <polygon points="{Hex(52, 54)}" fill="url(#mt)"/>
            <polygon points="{Hex(43, 54)}" fill="{p.Primary}"/>
            <polygon points="{Hex(43, 54)}" fill="none" stroke="#fff" stroke-opacity=".25" stroke-width="2" stroke-dasharray="3 3"/>
            {Glyph(medal, 60, 52, 30, "#fff", "Arial Black, Arial, sans-serif", 900)}
            <path d="M14 88 H106 L100 97 L106 106 H14 L20 97 Z" fill="{p.Secondary}"/>
            <path d="M14 88 H106 L100 97 L106 106 H14 L20 97 Z" fill="#000" opacity=".08"/>
            <text x="60" y="100.5" text-anchor="middle" font-family="Arial, sans-serif" font-weight="800" font-size="9" letter-spacing="1.5" fill="#fff">{m.Name}</text>
            """;
    }

    private static string Minimal(MedalRules.Medal medal, Metal m, MedalPalette p) => $"""
        <defs>{MetalGradient("mt", m)}</defs>
        <circle cx="60" cy="60" r="48" fill="#fff" stroke="{p.Primary}" stroke-width="4"/>
        <circle cx="60" cy="60" r="40" fill="none" stroke="{p.Primary}" stroke-opacity=".25" stroke-width="1.5"/>
        <circle cx="60" cy="12" r="7" fill="url(#mt)" stroke="#fff" stroke-width="2.5"/>
        {Glyph(medal, 60, 62, 30, p.Primary, "Helvetica, Arial, sans-serif", 700)}
        """;

    /// <summary>„W moim stylu strony”: kształt i krój z charakteru motywu strony, kolory z motywu.</summary>
    private static string Site(MedalRules.Medal medal, Metal m, MedalPalette p)
    {
        var accentInk = Contrast(p.Accent);
        switch (p.SiteTheme)
        {
            case "premium" or "minimal":
                return $"""
                    <defs>{MetalGradient("mt", m)}</defs>
                    <circle cx="60" cy="60" r="50" fill="{p.Surface}"/>
                    <circle cx="60" cy="60" r="46" fill="none" stroke="{p.Accent}" stroke-width="1.5"/>
                    <circle cx="60" cy="60" r="41" fill="none" stroke="{p.Accent}" stroke-width=".8"/>
                    <circle cx="60" cy="60" r="36" fill="none" stroke="url(#mt)" stroke-width="3"/>
                    {Glyph(medal, 60, 61, 28, p.Accent, "Georgia, 'Times New Roman', serif", 700)}
                    <text x="60" y="101" text-anchor="middle" font-family="Georgia, serif" font-size="7" letter-spacing="2" fill="{p.Ink}" opacity=".7">{m.Name}</text>
                    """;
            case "sport" or "ring" or "beton" or "stal" or "energia":
                return $"""
                    <defs>{MetalGradient("mt", m)}</defs>
                    <polygon points="30,8 90,8 112,30 112,90 90,112 30,112 8,90 8,30" fill="{p.Ink}"/>
                    <polygon points="34,16 86,16 104,34 104,86 86,104 34,104 16,86 16,34" fill="{p.Accent}"/>
                    <polygon points="16,34 34,16 50,16 16,50" fill="url(#mt)"/>
                    {Glyph(medal, 60, 62, 34, accentInk, "Impact, 'Arial Black', sans-serif", 900)}
                    """;
            case "noc":
                return $"""
                    <defs>{MetalGradient("mt", m)}<filter id="gl" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="4"/></filter></defs>
                    <circle cx="60" cy="60" r="44" fill="none" stroke="{p.Accent}" stroke-width="8" filter="url(#gl)" opacity=".8"/>
                    <circle cx="60" cy="60" r="44" fill="{p.Surface}" stroke="{p.Accent}" stroke-width="3"/>
                    <circle cx="60" cy="16" r="6" fill="url(#mt)"/>
                    {Glyph(medal, 60, 62, 30, p.Accent, "Arial, sans-serif", 800)}
                    """;
            default:
                // Jasne, miękkie motywy: zaokrąglony kwadrat w kolorze akcentu i metalowy znaczek rangi.
                return $"""
                    <defs>{MetalGradient("mt", m)}</defs>
                    <rect x="10" y="10" width="100" height="100" rx="30" fill="{p.Surface}" stroke="{p.Accent}" stroke-width="4"/>
                    <circle cx="60" cy="56" r="32" fill="{p.Accent}" opacity=".14"/>
                    {Glyph(medal, 60, 57, 30, p.Accent, "Arial, sans-serif", 800)}
                    <rect x="38" y="90" width="44" height="12" rx="6" fill="url(#mt)"/>
                    <text x="60" y="99" text-anchor="middle" font-family="Arial, sans-serif" font-weight="800" font-size="7" letter-spacing="1" fill="#fff">{m.Name}</text>
                    """;
        }
    }

    private static string Stars(MedalTier tier, double cx, double cy, double r, string color)
    {
        var n = tier switch { MedalTier.Gold => 3, MedalTier.Silver => 2, _ => 1 };
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            var x = cx + (i - (n - 1) / 2.0) * (r * 2.6);
            sb.Append($"""<path transform="translate({F(x - r)},{F(cy - r)}) scale({F(r / 12)})" d="{Paths.Star}" fill="{color}"/>""");
        }
        return sb.ToString();
    }

    private static string Glyph(MedalRules.Medal medal, double cx, double cy, double size, string color, string font, int weight)
    {
        if (medal.Glyph == MedalGlyph.Number && medal.Label is { } label)
        {
            var fs = label.Length >= 3 ? size * 0.8 : size * 1.05;
            return $"""<text x="{F(cx)}" y="{F(cy + fs * 0.35)}" text-anchor="middle" font-family="{font}" font-weight="{weight}" font-size="{F(fs)}" fill="{color}">{label}</text>""";
        }
        var path = medal.Glyph switch
        {
            MedalGlyph.Bolt => Paths.Bolt,
            MedalGlyph.Flame => Paths.Flame,
            MedalGlyph.Calendar => Paths.Calendar,
            MedalGlyph.Heart => Paths.Heart,
            MedalGlyph.Dumbbell => Paths.Dumbbell,
            _ => Paths.Star,
        };
        return $"""<path transform="translate({F(cx - size / 2)},{F(cy - size / 2)}) scale({F(size / 24)})" d="{path}" fill="{color}" fill-rule="evenodd"/>""";
    }

    private static class Paths
    {
        public const string Bolt = "M13.5 2 L4 14 h6.5 l-1.5 8 L19 10 h-6.5 z";
        public const string Flame = "M12 2c1.2 3.2 4.8 5 4.8 9.6a4.8 4.8 0 0 1-9.6 0c0-2.4 1.2-3.8 2.4-4.8 0 2 .9 3.3 2.2 3.6C11.6 8 10.8 5 12 2zM12 15.5c-.9.8-1.4 1.6-1.4 2.6a1.4 1.4 0 0 0 2.8 0c0-1-.5-1.8-1.4-2.6z";
        public const string Star = "M12 2.5l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 17.4 6.1 20.5l1.2-6.5L2.5 9.4l6.6-.9z";
        public const string Calendar = "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM5 10v9h14v-9zM7 2h2v5H7zM15 2h2v5h-2zM10.5 17.2l-2.7-2.7 1.2-1.2 1.5 1.5 3.9-3.9 1.2 1.2z";
        public const string Heart = "M12 21s-7.5-4.6-9.6-9.1C.9 8.6 3 5 6.5 5c2.1 0 3.6 1.2 5.5 3.2C13.9 6.2 15.4 5 17.5 5 21 5 23.1 8.6 21.6 11.9 19.5 16.4 12 21 12 21z";
        public const string Dumbbell = "M1.5 10h2V8.5h2v7h-2V14h-2zM22.5 10h-2V8.5h-2v7h2V14h2zM6.5 6h2.5v12H6.5zM15 6h2.5v12H15zM9 11h6v2H9z";
    }

    /// <summary>Biały albo prawie czarny tekst na kolorze tła (czytelność).</summary>
    public static string Contrast(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return "#fff";
        var r = (rgb >> 16) & 255; var g = (rgb >> 8) & 255; var b = rgb & 255;
        return (0.299 * r + 0.587 * g + 0.114 * b) > 160 ? "#111111" : "#ffffff";
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
