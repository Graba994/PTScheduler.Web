using System.Text.RegularExpressions;
using FluentAssertions;
using PTScheduler.Portal.Services;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Hantelek w filmach (studio/src/ad/Hantelek.tsx, React dla Remotion) ma wyglądać tak samo jak w aplikacji.
/// Jedno źródło prawdy to MascotArt.cs: każdy kształt i kolor z wbudowanych SVG musi być w komponencie wideo.
/// Gdy test nie przechodzi po zmianie maskotki – przenieś zmianę do Hantelek.tsx.
/// </summary>
public partial class HantelekMatchesMascotArtTests
{
    /// <summary>
    /// Ozdobniki, które w wideo są animowane inną geometrią (konfetti spada, dzwonek się kołysze),
    /// więc nie muszą mieć identycznych współrzędnych. Korpus, talerze, oczy, policzki i usta – muszą.
    /// </summary>
    private static readonly HashSet<string> AnimatedDifferently =
    [
        // celebrate: konfetti i gwiazdki (w wideo spadające prostokąty)
        "cx=\"30\" cy=\"20\"", "cx=\"92\" cy=\"22\"",
        "d=\"M76 12 l1.8 3.8 3.8 1.8 -3.8 1.8 -1.8 3.8 -1.8 -3.8 -3.8 -1.8 3.8 -1.8z\"",
        "d=\"M40 8 l1.4 3 3 1.4 -3 1.4 -1.4 3 -1.4 -3 -3 -1.4 3 -1.4z\"",
        // remind: dzwonek (w wideo ta sama ikona, ale przesunięta w ścieżce i kołysana)
        "d=\"M8 2 a6 6 0 0 1 6 6 v5 l2 3 h-16 l2 -3 v-5 a6 6 0 0 1 6 -6z\"", "cx=\"8\" cy=\"18.5\"",
        // think: błyski w oczach przesunięte o (1.6, -1.6) – w wideo jako dx/dy, sprawdza to ThinkingEyes_LookTheSameWay
        "cx=\"54.2\" cy=\"59.4\"", "cx=\"72.2\" cy=\"59.4\"",
    ];

    [Fact]
    public void ThinkingEyes_LookTheSameWay()
    {
        var think = ShapeKeys(MascotArt.Svg("think")).ToList();
        var hello = ShapeKeys(MascotArt.Svg("hello")).ToList();
        think.Should().Contain("cx=\"54.2\" cy=\"59.4\"").And.Contain("cx=\"72.2\" cy=\"59.4\"");
        hello.Should().Contain("cx=\"52.6\" cy=\"61\"");
        VideoComponent().Should().Contain("mood === \"think\" ? [1.6, -1.6]", "w wideo błysk w oczach przesuwa się tak samo jak w MascotArt");
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "PTScheduler.Portal")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Nie znaleziono katalogu repozytorium.");
    }

    private static string VideoComponent() =>
        File.ReadAllText(Path.Combine(Root(), "studio", "src", "ad", "Hantelek.tsx"));

    /// <summary>Odciski kształtów: ścieżki (d) i położenia prostokątów, elips i kół bez transformacji.</summary>
    internal static IEnumerable<string> ShapeKeys(string svg)
    {
        foreach (Match m in Element().Matches(svg))
        {
            var (tag, attrs) = (m.Groups[1].Value, m.Groups[2].Value);
            if (attrs.Contains("transform=") || attrs.Contains("class=")) continue;
            // liczby zaokrąglone – MascotArt potrafi wypisać 72.19999999999999 zamiast 72.2
            string? Attr(string name) => Regex.Match(attrs, $@"(?:^|\s){name}=""([^""]*)""") is { Success: true } a
                ? (name != "d" && double.TryParse(a.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)
                    ? Math.Round(n, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) : a.Groups[1].Value)
                : null;
            var key = tag switch
            {
                "path" => Attr("d") is { } d ? $"d=\"{d}\"" : null,
                "rect" => Attr("x") is { } x && Attr("y") is { } y && Attr("width") is { } w && Attr("height") is { } h
                    ? $"x=\"{x}\" y=\"{y}\" width=\"{w}\" height=\"{h}\"" : null,
                "ellipse" or "circle" => Attr("cx") is { } cx && Attr("cy") is { } cy ? $"cx=\"{cx}\" cy=\"{cy}\"" : null,
                _ => null,
            };
            if (key is not null) yield return key;
        }
    }

    [Fact]
    public void EveryMood_ExistsInVideo()
    {
        var tsx = VideoComponent();
        foreach (var mood in MascotArt.Moods)
            tsx.Should().Contain($"\"{mood.Key}\"", $"nastrój „{mood.Key}” z MascotArt musi być w Hantelek.tsx");
    }

    [Fact]
    public void Shapes_AreTheSameAsInMascotArt()
    {
        var tsx = VideoComponent();
        var missing = MascotArt.Moods
            .SelectMany(m => ShapeKeys(MascotArt.Svg(m.Key)).Select(k => (Mood: m.Key, Key: k)))
            .Where(x => !AnimatedDifferently.Contains(x.Key) && !InVideo(tsx, x.Key))
            .Select(x => $"{x.Mood}: {x.Key}")
            .Distinct()
            .ToList();
        missing.Should().BeEmpty("kształty Hantelka w wideo muszą być takie jak w MascotArt.cs");
    }

    [Fact]
    public void Colors_AreTheSameAsInMascotArt()
    {
        var tsx = VideoComponent();
        var missing = MascotArt.Moods
            .SelectMany(m => HexColor().Matches(MascotArt.Svg(m.Key)).Select(c => c.Value.ToUpperInvariant()))
            .Distinct()
            .Where(c => !tsx.Contains(c, StringComparison.OrdinalIgnoreCase))
            .ToList();
        missing.Should().BeEmpty("kolory Hantelka w wideo muszą być takie jak w MascotArt.cs");
    }

    /// <summary>
    /// Kształt jest w komponencie dosłownie albo z położeniem liczonym od tej samej wartości
    /// (np. błysk w oku: <c>cx={52.6 + dx}</c> – przesuwa się, gdy Hantelek myśli).
    /// </summary>
    internal static bool InVideo(string tsx, string key)
    {
        if (tsx.Contains(key, StringComparison.Ordinal)) return true;
        var m = Regex.Match(key, @"^cx=""([^""]+)"" cy=""([^""]+)""$");
        return m.Success
            && Regex.IsMatch(tsx, $@"cx=\{{{Regex.Escape(m.Groups[1].Value)}(?![\d.])[^}}]*\}}\s+cy=\{{{Regex.Escape(m.Groups[2].Value)}(?![\d.])");
    }

    [Fact]
    public void InVideo_AcceptsComputedPosition()
    {
        const string tsx = """<circle cx={52.6 + dx} cy={61 + dy} r="1.8" />""";
        InVideo(tsx, "cx=\"52.6\" cy=\"61\"").Should().BeTrue();
        InVideo(tsx, "cx=\"52\" cy=\"61\"").Should().BeFalse();
    }

    [Fact]
    public void ShapeKeys_ReadsSvgElements()
    {
        const string svg = """<g><rect x="1" y="2" width="3" height="4" rx="1"/><circle cx="5" cy="6" r="1" transform="rotate(3)"/><path d="M0 0 h1"/></g>""";
        ShapeKeys(svg).Should().Equal("x=\"1\" y=\"2\" width=\"3\" height=\"4\"", "d=\"M0 0 h1\"");
    }

    [GeneratedRegex("""<(path|rect|ellipse|circle)\b([^>]*)/?>""")]
    private static partial Regex Element();

    [GeneratedRegex("#[0-9A-Fa-f]{6}\\b")]
    private static partial Regex HexColor();
}
