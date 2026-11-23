using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Studio reklam — reguły bez sieci i bazy: schematy wyników AI, limity znaków (Meta, Google, post),
/// czyszczenie materiałów (HTML, Razor, commity) i eksport CSV. Wszystko testowalne jednostkowo.
/// </summary>
public static partial class StudioRules
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── Wiedza o produkcie (§5.1) ──

    public sealed class Feature
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("benefit")] public string Benefit { get; set; } = "";
        [JsonPropertyName("detail")] public string Detail { get; set; } = "";
        [JsonPropertyName("screen")] public string Screen { get; set; } = "";
    }

    public sealed class Novelty
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("when")] public string When { get; set; } = "";
        [JsonPropertyName("benefit")] public string Benefit { get; set; } = "";
    }

    public sealed class Knowledge
    {
        [JsonPropertyName("oneLiner")] public string OneLiner { get; set; } = "";
        [JsonPropertyName("audience")] public string Audience { get; set; } = "";
        [JsonPropertyName("painPoints")] public List<string> PainPoints { get; set; } = [];
        [JsonPropertyName("features")] public List<Feature> Features { get; set; } = [];
        [JsonPropertyName("novelties")] public List<Novelty> Novelties { get; set; } = [];
        [JsonPropertyName("proofPoints")] public List<string> ProofPoints { get; set; } = [];
        [JsonPropertyName("tone")] public string Tone { get; set; } = "";
        [JsonPropertyName("cta")] public string Cta { get; set; } = "";
        [JsonPropertyName("doNotSay")] public List<string> DoNotSay { get; set; } = [];
        [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>Zasady stałe, niezależne od AI — zawsze dopinane do „czego nie mówić”.</summary>
    public static readonly string[] AlwaysDoNotSay =
    [
        "Nie używaj nazwy „PTScheduler” — produkt nazywa się Hantly.",
        "AI w aplikacji niczego nie wysyła samo — przygotowuje szkice i podpowiedzi, decyduje trener.",
        "Żadnych wymyślonych opinii, liczby klientów, statystyk ani nagród.",
        "Bez słów „rewolucyjny”, „innowacyjny”, „kompleksowe rozwiązanie”.",
    ];

    // ── Teksty reklam (§5.4) ──

    public sealed class MetaAd
    {
        [JsonPropertyName("angle")] public string Angle { get; set; } = "";
        [JsonPropertyName("primaryText")] public string PrimaryText { get; set; } = "";
        [JsonPropertyName("headline")] public string Headline { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
    }

    public sealed class GoogleAds
    {
        [JsonPropertyName("headlines")] public List<string> Headlines { get; set; } = [];
        [JsonPropertyName("descriptions")] public List<string> Descriptions { get; set; } = [];
        [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = [];
    }

    public sealed class Ads
    {
        [JsonPropertyName("meta")] public List<MetaAd> Meta { get; set; } = [];
        [JsonPropertyName("google")] public GoogleAds Google { get; set; } = new();
        [JsonPropertyName("audiences")] public List<string> Audiences { get; set; } = [];
        [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    }

    // ── Post (§5.3, bez makiety ekranu — ta przyjdzie z robotem od zrzutów) ──

    public sealed class Post
    {
        [JsonPropertyName("headline")] public string Headline { get; set; } = "";
        [JsonPropertyName("sub")] public string Sub { get; set; } = "";
        [JsonPropertyName("bullets")] public List<string> Bullets { get; set; } = [];
        [JsonPropertyName("caption")] public string Caption { get; set; } = "";
        [JsonPropertyName("hashtags")] public List<string> Hashtags { get; set; } = [];
        [JsonPropertyName("altText")] public string AltText { get; set; } = "";
    }

    /// <summary>Limity znaków (Meta: nagłówek 40, opis 30, pierwsze 125 znaków tekstu głównego; Google RSA: 30 / 90).</summary>
    public static class Limits
    {
        public const int MetaPrimaryVisible = 125, MetaHeadline = 40, MetaDescription = 30;
        public const int GoogleHeadline = 30, GoogleDescription = 90;
        public const int GoogleHeadlinesMin = 10, GoogleHeadlinesMax = 15, GoogleDescriptions = 4;
        public const int PostHeadline = 40, PostSub = 70, PostBullet = 32;
    }

    public sealed record Issue(string Field, string Problem);

    public static List<Issue> Check(Ads a)
    {
        var list = new List<Issue>();
        if (a.Meta.Count != 3) list.Add(new("Meta", $"wariantów: {a.Meta.Count} (powinny być 3)"));
        for (var i = 0; i < a.Meta.Count; i++)
        {
            var m = a.Meta[i];
            if (m.Headline.Length > Limits.MetaHeadline) list.Add(new($"Meta {i + 1} — nagłówek", $"{m.Headline.Length}/{Limits.MetaHeadline} znaków"));
            if (m.Description.Length > Limits.MetaDescription) list.Add(new($"Meta {i + 1} — opis", $"{m.Description.Length}/{Limits.MetaDescription} znaków"));
            if (string.IsNullOrWhiteSpace(m.PrimaryText)) list.Add(new($"Meta {i + 1} — tekst główny", "pusty"));
        }
        var g = a.Google;
        if (g.Headlines.Count is < Limits.GoogleHeadlinesMin or > Limits.GoogleHeadlinesMax)
            list.Add(new("Google — nagłówki", $"{g.Headlines.Count} (powinno być {Limits.GoogleHeadlinesMin}–{Limits.GoogleHeadlinesMax})"));
        var longH = g.Headlines.Count(h => h.Length > Limits.GoogleHeadline);
        if (longH > 0) list.Add(new("Google — nagłówki", $"{longH} za długie (max {Limits.GoogleHeadline})"));
        if (g.Descriptions.Count != Limits.GoogleDescriptions) list.Add(new("Google — teksty", $"{g.Descriptions.Count} (powinny być {Limits.GoogleDescriptions})"));
        var longD = g.Descriptions.Count(d => d.Length > Limits.GoogleDescription);
        if (longD > 0) list.Add(new("Google — teksty", $"{longD} za długie (max {Limits.GoogleDescription})"));
        return list;
    }

    public static List<Issue> Check(Post p)
    {
        var list = new List<Issue>();
        if (string.IsNullOrWhiteSpace(p.Headline)) list.Add(new("Nagłówek", "pusty"));
        if (p.Headline.Length > Limits.PostHeadline) list.Add(new("Nagłówek", $"{p.Headline.Length}/{Limits.PostHeadline} znaków"));
        if (p.Sub.Length > Limits.PostSub) list.Add(new("Podtytuł", $"{p.Sub.Length}/{Limits.PostSub} znaków"));
        var longB = p.Bullets.Count(b => b.Length > Limits.PostBullet);
        if (longB > 0) list.Add(new("Korzyści", $"{longB} za długie (max {Limits.PostBullet})"));
        if (p.Bullets.Count != 3) list.Add(new("Korzyści", $"{p.Bullets.Count} (powinny być 3)"));
        if (string.IsNullOrWhiteSpace(p.Caption)) list.Add(new("Opis", "pusty"));
        list.AddRange(InstagramRules.CaptionProblems(p.Caption + " " + string.Join(" ", p.Hashtags.Select(h => "#" + h.TrimStart('#')))).Select(x => new Issue("Opis", x)));
        return list;
    }

    /// <summary>Opis do wklejenia: tekst + hashtagi w osobnym akapicie.</summary>
    public static string CaptionWithTags(Post p)
    {
        var tags = p.Hashtags.Select(InstagramRules.NormalizeTag).Where(t => t is not null).Distinct().ToList();
        return tags.Count == 0 ? p.Caption.Trim() : $"{p.Caption.Trim()}\n\n{string.Join(" ", tags)}";
    }

    // ── Parsowanie odpowiedzi AI ──

    public static T? Parse<T>(string? text) where T : class
    {
        var json = InstagramRules.JsonPart(text);
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Porządki po AI: przycięte spacje, puste pozycje i hashtagi bez „#”.</summary>
    public static Ads Clean(Ads a)
    {
        a.Meta = a.Meta.Where(m => !string.IsNullOrWhiteSpace(m.PrimaryText) || !string.IsNullOrWhiteSpace(m.Headline)).Take(3).ToList();
        foreach (var m in a.Meta) { m.Angle = m.Angle.Trim(); m.PrimaryText = m.PrimaryText.Trim(); m.Headline = m.Headline.Trim(); m.Description = m.Description.Trim(); }
        a.Google.Headlines = Distinct(a.Google.Headlines).Take(Limits.GoogleHeadlinesMax).ToList();
        a.Google.Descriptions = Distinct(a.Google.Descriptions).Take(Limits.GoogleDescriptions).ToList();
        a.Google.Keywords = Distinct(a.Google.Keywords).Take(20).ToList();
        a.Audiences = Distinct(a.Audiences).ToList();
        return a;
    }

    public static Post Clean(Post p)
    {
        p.Headline = p.Headline.Trim(); p.Sub = p.Sub.Trim(); p.Caption = p.Caption.Trim(); p.AltText = p.AltText.Trim();
        p.Bullets = Distinct(p.Bullets).Take(3).ToList();
        p.Hashtags = p.Hashtags.Select(h => h.Trim().TrimStart('#').Replace(" ", "")).Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(15).ToList();
        // Hashtagi wpisane w opis przenosimy na listę.
        foreach (var tag in InstagramRules.Hashtags(p.Caption))
        {
            p.Caption = p.Caption.Replace(tag, "").Trim();
            if (!p.Hashtags.Contains(tag.TrimStart('#'), StringComparer.OrdinalIgnoreCase)) p.Hashtags.Add(tag.TrimStart('#'));
        }
        p.Caption = MultiSpaceRx().Replace(p.Caption, " ");
        return p;
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> items) =>
        items.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    // ── CSV (Excel: średnik, UTF-8 z BOM) ──

    public static byte[] Csv(Ads a)
    {
        var sb = new StringBuilder();
        sb.Append("Platforma;Typ;Tekst;Znaki\r\n");
        void Row(string platform, string type, string text) => sb.Append($"{platform};{type};{Cell(text)};{text.Length}\r\n");
        for (var i = 0; i < a.Meta.Count; i++)
        {
            var m = a.Meta[i];
            Row("Meta", $"Wariant {i + 1} — kąt", m.Angle);
            Row("Meta", $"Wariant {i + 1} — tekst główny", m.PrimaryText);
            Row("Meta", $"Wariant {i + 1} — nagłówek", m.Headline);
            Row("Meta", $"Wariant {i + 1} — opis", m.Description);
        }
        foreach (var h in a.Google.Headlines) Row("Google", "Nagłówek", h);
        foreach (var d in a.Google.Descriptions) Row("Google", "Tekst reklamy", d);
        foreach (var k in a.Google.Keywords) Row("Google", "Słowo kluczowe", k);
        foreach (var au in a.Audiences) Row("Wszystkie", "Grupa odbiorców", au);
        if (!string.IsNullOrWhiteSpace(a.Notes)) Row("Wszystkie", "Uwagi", a.Notes);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Cell(string text)
    {
        var t = text.Replace("\r", "");
        return t.IndexOfAny([';', '"', '\n']) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t;
    }

    // ── Materiały do wiedzy ──

    [GeneratedRegex(@"<(script|style|svg|noscript|template)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex BlockRx();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRx();
    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex MultiSpaceRx();
    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex MultiLineRx();

    /// <summary>HTML strony → sam tekst (bez skryptów, stylów i grafik).</summary>
    public static string HtmlToText(string html, int max = 25000)
    {
        var s = BlockRx().Replace(html, " ");
        s = Regex.Replace(s, @"<(br|/p|/div|/li|/h[1-6]|/section|/tr)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = TagRx().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = MultiSpaceRx().Replace(s, " ");
        s = string.Join('\n', s.Split('\n').Select(l => l.Trim()));
        s = MultiLineRx().Replace(s, "\n").Trim();
        return Cut(s, max);
    }

    /// <summary>Plik .razor → tekst widoczny dla użytkownika (bez dyrektyw @…, bloku @code i znaczników).</summary>
    public static string RazorToText(string razor, int max = 15000)
    {
        var code = razor.IndexOf("@code", StringComparison.Ordinal);
        var s = code >= 0 ? razor[..code] : razor;
        s = Regex.Replace(s, @"@\*.*?\*@", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"^\s*@(page|using|inject|attribute|layout|rendermode|implements|inherits|namespace|typeparam)\b.*$", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"@\([^)]*\)|@[A-Za-z_][\w.]*(\([^)]*\))?", " ");
        return HtmlToText(s, max);
    }

    /// <summary>Wiersz historii: „RRRR-MM-DD | pierwsza linia opisu commita” — bez commitów technicznych (merge, bump).</summary>
    public static string? CommitLine(DateTime date, string message)
    {
        var first = message.Split('\n')[0].Trim();
        if (first.Length == 0 || first.StartsWith("Merge ", StringComparison.Ordinal)) return null;
        return $"{date:yyyy-MM-dd} | {Cut(first, 160)}";
    }

    public static string Cut(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
