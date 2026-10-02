namespace PTScheduler.Portal.Services;

/// <summary>
/// Dane kreatora „Zbuduj swoją aplikację”: specjalizacje (szablon strony, kolor, gotowa oferta),
/// kolory aplikacji i motywy stron. Klucze muszą zgadzać się z aplikacją trenera:
/// kolory — AppBranding.ThemeName (/admin/branding), szablony i motywy — SiteWidgets.
/// </summary>
public static class AppBuilderCatalog
{
    public sealed record AppColor(string Key, string Name, string Hex, string Hex2);

    public static readonly IReadOnlyList<AppColor> Colors =
    [
        new("ocean", "Ocean", "#0284C7", "#06B6D4"),
        new("forest", "Las", "#16A34A", "#10B981"),
        new("teal", "Morski", "#0D9488", "#06B6D4"),
        new("indigo", "Indygo", "#4F46E5", "#7C3AED"),
        new("lavender", "Lawenda", "#9333EA", "#EC4899"),
        new("rose", "Róż", "#E11D48", "#F43F5E"),
        new("crimson", "Karmin", "#DC2626", "#F97316"),
        new("sunset", "Zachód", "#EA580C", "#FBBF24"),
        new("amber", "Bursztyn", "#D97706", "#F59E0B"),
        new("slate", "Grafit", "#475569", "#64748B"),
    ];

    public static AppColor ColorOf(string? key) => Colors.FirstOrDefault(c => c.Key == key) ?? Colors[0];

    /// <summary>Motyw strony głównej (jak SiteWidgets.Themes). Pusty Accent = kolor aplikacji.</summary>
    public sealed record SiteTheme(string Key, string Bg, string Surface, string Ink, string Accent, bool Dark, bool Serif = false);

    private static readonly Dictionary<string, SiteTheme> Themes = new()
    {
        ["studio"] = new("studio", "#f6f7fb", "#ffffff", "#0f172a", "", false),
        ["energia"] = new("energia", "#0b0f17", "#131a27", "#eef2f7", "#c6f432", true),
        ["premium"] = new("premium", "#0d0d10", "#17171c", "#f5f1e8", "#d6b36a", true, true),
        ["natura"] = new("natura", "#f3f6f1", "#ffffff", "#1b2a20", "#2f855a", false),
        ["sport"] = new("sport", "#ffffff", "#f3f4f6", "#111111", "#ff5a1f", false),
        ["ocean"] = new("ocean", "#eff6fb", "#ffffff", "#0b2239", "#0ea5e9", false),
        ["roz"] = new("roz", "#fff6f8", "#ffffff", "#2b1320", "#db2f6f", false),
        ["minimal"] = new("minimal", "#ffffff", "#f4f4f2", "#111111", "#111111", false, true),
        ["ring"] = new("ring", "#101010", "#1b1b1b", "#f4f4f4", "#e5202e", true),
        ["noc"] = new("noc", "#0d1024", "#161a38", "#eef0ff", "#8b7bff", true),
    };

    public static SiteTheme ThemeOf(string key) => Themes.GetValueOrDefault(key) ?? Themes["studio"];

    /// <summary>Szablon strony głównej trenera (SiteWidgets.Templates) z nagłówkiem do podglądu.</summary>
    public sealed record SiteTemplate(string Key, string Name, string Theme, string Eyebrow, string Title, string Cta, int Sections);

    public static readonly IReadOnlyList<SiteTemplate> Templates =
    [
        new("personal", "Trener personalny", "studio", "Trener personalny", "Silniejsze ciało. Lepsze samopoczucie. Bez zgadywania.", "Umów pierwszy trening", 12),
        new("transform", "Metamorfozy", "energia", "Metamorfozy z trenerem", "Twoja najlepsza forma zaczyna się dziś", "Umów pierwszy trening", 8),
        new("premium", "Premium 1:1", "premium", "Trening personalny 1:1", "Indywidualnie. Dyskretnie. Skutecznie.", "Zarezerwuj konsultację", 7),
        new("health", "Zdrowie i ruch", "natura", "Zdrowy ruch w każdym wieku", "Ruszaj się bez bólu i z przyjemnością", "Umów pierwszy trening", 8),
        new("online", "Trening online", "ocean", "Trening i prowadzenie online", "Trener w Twoim telefonie — gdziekolwiek jesteś", "Zacznij współpracę", 7),
        new("sport", "Siła i motoryka", "sport", "Przygotowanie motoryczne", "Szybciej. Mocniej. Dalej.", "Umów test sprawności", 8),
        new("women", "Trening dla kobiet", "roz", "Trening dla kobiet", "Silna, pewna siebie, w swoim tempie", "Umów pierwszy trening", 8),
        new("mindful", "Pilates i mobilność", "minimal", "Pilates · mobilność · zdrowy kręgosłup", "Mniej napięcia. Więcej ruchu.", "Zarezerwuj zajęcia", 8),
        new("combat", "Sporty walki", "ring", "Boks · kickboxing · MMA", "Wejdź na matę. Wyjdź silniejszy.", "Pierwszy trening za darmo", 8),
        new("bootcamp", "Treningi grupowe", "noc", "Treningi w małych grupach", "Razem trenuje się łatwiej", "Zarezerwuj miejsce", 8),
        new("senior", "Aktywny senior", "natura", "Aktywny senior 60+", "Sprawność na co dzień. Bezpiecznie i spokojnie.", "Zadzwoń i umów się", 7),
        new("duo", "Treningi w parze", "ocean", "Treningi w parze", "We dwoje raźniej — i taniej", "Umów trening dla dwojga", 7),
    ];

    public static SiteTemplate TemplateOf(string? key) => Templates.FirstOrDefault(t => t.Key == key) ?? Templates[0];

    public sealed record OfferDraft(string Name, int Minutes, decimal Price, bool IsPair = false, bool IsGroup = false);

    /// <summary>Specjalizacja: od niej zależą szablon strony, kolor i gotowa oferta (trener tylko poprawia ceny).</summary>
    public sealed record Specialization(string Key, string Name, string Icon, string Template, string Color,
        string[] AltTemplates, OfferDraft[] Offers, int PackageCount, decimal PackagePrice);

    public static readonly IReadOnlyList<Specialization> Specializations =
    [
        new("personal", "Trening personalny", "bi-person-arms-up", "personal", "ocean", ["transform", "premium"],
            [new("Trening personalny", 60, 150), new("Konsultacja i pomiary", 30, 0)], 8, 1040),
        new("transform", "Redukcja i metamorfozy", "bi-fire", "transform", "crimson", ["personal", "women"],
            [new("Trening metamorfoza", 60, 160), new("Konsultacja i plan", 45, 0)], 12, 1680),
        new("women", "Trening dla kobiet", "bi-flower1", "women", "rose", ["personal", "mindful"],
            [new("Trening personalny", 60, 140), new("Trening z przyjaciółką", 60, 200, IsPair: true)], 8, 1000),
        new("mindful", "Pilates i mobilność", "bi-wind", "mindful", "slate", ["health", "women"],
            [new("Pilates 1:1", 55, 140), new("Mobilność i zdrowy kręgosłup", 45, 110)], 10, 1200),
        new("health", "Zdrowie i rehabilitacja", "bi-heart-pulse", "health", "forest", ["senior", "mindful"],
            [new("Trening zdrowotny", 60, 140), new("Terapia ruchem", 45, 120)], 8, 1000),
        new("sport", "Siła i motoryka", "bi-lightning-charge", "sport", "sunset", ["transform", "personal"],
            [new("Trening motoryczny", 60, 160), new("Test sprawności", 60, 200)], 8, 1160),
        new("combat", "Sporty walki", "bi-shield-shaded", "combat", "crimson", ["sport", "bootcamp"],
            [new("Trening techniczny 1:1", 60, 150), new("Tarcze i kondycja", 45, 120)], 8, 1040),
        new("bootcamp", "Treningi grupowe", "bi-people-fill", "bootcamp", "indigo", ["sport", "transform"],
            [new("Zajęcia w małej grupie", 45, 50, IsGroup: true), new("Trening personalny", 60, 150)], 10, 400),
        new("online", "Trening online", "bi-laptop", "online", "teal", ["personal", "transform"],
            [new("Trening online na żywo", 60, 120), new("Konsultacja online", 30, 0)], 4, 440),
        new("duo", "Treningi w parze", "bi-people", "duo", "lavender", ["personal", "women"],
            [new("Trening w parze", 60, 220, IsPair: true), new("Trening personalny", 60, 150)], 8, 1600),
        new("senior", "Aktywny senior", "bi-sun", "senior", "teal", ["health", "mindful"],
            [new("Trening 60+", 45, 110), new("Spacer z kijami (grupa)", 60, 40, IsGroup: true)], 8, 800),
        new("premium", "Premium 1:1", "bi-gem", "premium", "amber", ["personal", "transform"],
            [new("Trening premium 1:1", 60, 250), new("Konsultacja", 45, 0)], 10, 2300),
    ];

    public static Specialization SpecOf(string? key) => Specializations.FirstOrDefault(s => s.Key == key) ?? Specializations[0];

    /// <summary>Adres z nazwy studia: małe litery, bez polskich znaków, myślniki.</summary>
    public static string Slugify(string? name)
    {
        var slug = (name ?? "").Trim().ToLowerInvariant()
            .Replace("ą", "a").Replace("ć", "c").Replace("ę", "e").Replace("ł", "l").Replace("ń", "n")
            .Replace("ó", "o").Replace("ś", "s").Replace("ź", "z").Replace("ż", "z");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return slug;
    }

    /// <summary>Adresy zarezerwowane dla platformy.</summary>
    public static readonly HashSet<string> ReservedSlugs =
        ["www", "portal", "panel", "admin", "api", "app", "mail", "smtp", "ftp", "status", "docs", "blog", "pomoc", "help", "test", "demo", "npm", "guardian"];

    /// <summary>Inicjały do logo-monogramu (np. „Anna Fit Studio” → „AF”).</summary>
    public static string Monogram(string? name)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => char.IsLetterOrDigit(w[0])).ToList();
        return words.Count switch
        {
            0 => "PT",
            1 => words[0].Length > 1 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToLowerInvariant(words[0][1])}" : char.ToUpperInvariant(words[0][0]).ToString(),
            _ => string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])))
        };
    }
}
