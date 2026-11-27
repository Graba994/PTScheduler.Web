using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Reklama wideo 9:16 — reguły bez sieci i bazy: katalog ujęć (studio/src/ad/AdShort.tsx), poprawianie szkicu od AI
/// (znane ujęcia, istniejące zrzuty, CTA na końcu), bezpieczne wklejanie poprawek językowych, episode.json i opis.txt.
/// </summary>
public static class AdRules
{
    public static readonly string[] Templates = ["ad-hook", "ad-mascot", "ad-screen", "ad-assistant", "ad-compare", "ad-benefits", "ad-cta"];

    public const string Accent = "#06D6A0";
    public static readonly string[] Background = ["#110E1B", "#5B21B6"];

    /// <summary>Ujęcia z limitami tekstów. *gwiazdki* = wyróżnione słowa (mogą objąć kilka słów i stać przed interpunkcją).</summary>
    public const string Catalog = """
        - "ad-hook" – otwarcie: wiadomości od klientów wpadają jedna po drugiej, potem wielki napis i puenta.
          {"template":"ad-hook","messages":["0–4 wiadomości od klientów, każda ≤ 38 znaków"],"big":"≤ 8 znaków, np. 23:07 albo 3 h","line":"≤ 48 znaków, z *wyróżnieniem*","mood":"think|remind|sleep"}
        - "ad-mascot" – Hantelek duży na środku, mówi do widza dymkiem.
          {"template":"ad-mascot","bubble":"≤ 70 znaków","mood":"hello|happy","name":"podpis ≤ 30 znaków, np. Twój asystent AI w Hantly"}
        - "ad-screen" – PRAWDZIWY ekran aplikacji w telefonie (wjazd 3D, powolne przewijanie) i dymki przy telefonie.
          {"template":"ad-screen","screen":"dokładna nazwa pliku z listy zrzutów","caption":"nagłówek ≤ 40 znaków z *wyróżnieniem*","callouts":[{"text":"≤ 26 znaków","y":0.0–1.0}],"scroll":0.0–1.0,"side":"right|left","mood":"happy|think|celebrate"}
          callouts: 0–3, "y" weź z dymków w opisie zrzutu (pozycja przy scroll=0). "scroll" > 0 tylko, gdy ekran jest wyższy niż 2,2× szerokości i ciekawe rzeczy są niżej.
        - "ad-assistant" – czat z asystentem AI: AI pisze (kropki), podpowiada, trener odpowiada krótko.
          {"template":"ad-assistant","title":"≤ 40 znaków z *wyróżnieniem*","agent":"nazwa asystenta ≤ 24 znaki","messages":[{"from":"ai|me","text":"≤ 110 znaków"}],"mood":"hello|happy"}
          2–4 wiadomości, pierwsza od AI – konkretna podpowiedź z wiedzy o produkcie (fikcyjne imiona klientów).
        - "ad-compare" – przed/po: stary sposób przekreślany na czerwono, potem nowy z ptaszkami.
          {"template":"ad-compare","before":["3× ≤ 30 znaków"],"after":["3× ≤ 30 znaków"],"beforeLabel":"≤ 16 znaków","afterLabel":"≤ 16 znaków"}
        - "ad-benefits" – lista korzyści wjeżdżająca z ptaszkami.
          {"template":"ad-benefits","title":"≤ 40 znaków z *wyróżnieniem*","items":["3–4× ≤ 34 znaki"],"mood":"happy"}
        - "ad-cta" – finał: Hantelek salto, logo, cena, pulsujący przycisk, adres.
          {"template":"ad-cta","headline":"≤ 40 znaków z *wyróżnieniem*","price":"≤ 10 znaków, np. 0 zł","button":"≤ 24 znaki","note":"≤ 50 znaków"}
        """;

    // ── Szkic od AI ──────────────────────────────────────────────────

    public sealed class Draft
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Tags { get; set; } = [];
        public List<string> Hooks { get; set; } = [];
        public string PinnedComment { get; set; } = "";
        public List<DraftScene> Scenes { get; set; } = [];
    }

    public sealed class DraftScene
    {
        public string Kind { get; set; } = "fact";
        public int? Number { get; set; }
        public string Headline { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Text { get; set; } = "";
        public JsonObject Story { get; set; } = [];
    }

    /// <summary>Odczyt szkicu: "story" może przyjść jako obiekt albo jako JSON w stringu.</summary>
    public static Draft? ParseDraft(string? text)
    {
        var json = InstagramRules.JsonPart(text);
        if (json is null) return null;
        JsonObject? root;
        try { root = JsonNode.Parse(json) as JsonObject; } catch (JsonException) { return null; }
        if (root?["scenes"] is not JsonArray scenes || scenes.Count == 0) return null;
        return new Draft
        {
            Title = Str(root["title"]), Description = Str(root["description"]), PinnedComment = Str(root["pinnedComment"]),
            Tags = Strings(root["tags"]), Hooks = Strings(root["hooks"]),
            Scenes = scenes.OfType<JsonObject>().Select(s => new DraftScene
            {
                Kind = Str(s["kind"]), Number = s["number"] is JsonValue nv && nv.TryGetValue<int>(out var n) ? n : null,
                Headline = Str(s["headline"]), Icon = Str(s["icon"]), Text = Str(s["text"]), Story = StoryOf(s["story"]),
            }).ToList(),
        };
    }

    public static JsonObject StoryOf(JsonNode? node)
    {
        try
        {
            return node switch
            {
                JsonObject o => (JsonObject)o.DeepClone(),
                JsonValue v when v.TryGetValue<string>(out var s) && s.TrimStart().StartsWith('{') => JsonNode.Parse(s) as JsonObject ?? [],
                _ => [],
            };
        }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// Pilnuje, żeby reklama dała się wyrenderować i była uczciwa: znane ujęcia, tylko istniejące (i dozwolone) zrzuty,
    /// ostatnia scena = CTA, rodzaje scen hook/fact/outro, rozsądne długości. Brak zrzutów → telefon zamienia się w listę korzyści.
    /// </summary>
    public static Draft Sanitize(Draft d, IReadOnlyList<string> usableScreens, string ctaNote)
    {
        d.Scenes = d.Scenes.Where(s => !string.IsNullOrWhiteSpace(s.Text) || s.Story.Count > 0).Take(10).ToList();
        var used = new HashSet<string>();
        foreach (var s in d.Scenes)
        {
            if (!Templates.Contains(Template(s.Story)))
                s.Story = new JsonObject { ["template"] = "ad-mascot", ["bubble"] = Cut(s.Headline.Length > 0 ? s.Headline : s.Text, 70), ["mood"] = "happy" };
            if (Str(s.Story["screen"]) is { Length: > 0 } f && usableScreens.Contains(f)) used.Add(f);
        }
        foreach (var s in d.Scenes.Where(s => Template(s.Story) == "ad-screen"))
        {
            if (usableScreens.Contains(Str(s.Story["screen"]))) continue;
            var spare = usableScreens.FirstOrDefault(x => !used.Contains(x));
            if (spare is not null)
            {
                s.Story["screen"] = spare;
                s.Story["callouts"] = new JsonArray(); // dymki opisywały inny ekran
                used.Add(spare);
                continue;
            }
            var items = new JsonArray();
            foreach (var co in s.Story["callouts"] as JsonArray ?? []) if (Str(co?["text"]) is { Length: > 0 } t) items.Add(t);
            s.Story = new JsonObject { ["template"] = "ad-benefits", ["title"] = Str(s.Story["caption"]), ["items"] = items, ["mood"] = "happy" };
        }
        if (d.Scenes.Count > 0 && Template(d.Scenes[^1].Story) != "ad-cta")
            d.Scenes[^1].Story = new JsonObject
            {
                ["template"] = "ad-cta", ["headline"] = "Zacznij *za darmo*", ["price"] = "0 zł", ["button"] = "Zacznij za darmo", ["note"] = Cut(ctaNote, 50),
            };
        var number = 0;
        for (var i = 0; i < d.Scenes.Count; i++)
        {
            var s = d.Scenes[i];
            s.Kind = i == 0 ? "hook" : i == d.Scenes.Count - 1 ? "outro" : "fact";
            s.Number = s.Kind == "fact" ? ++number : null;
            s.Headline = Cut(s.Headline.Trim(), 22);
            s.Icon = s.Icon.Trim() is { Length: > 0 and <= 8 } icon ? icon : "✨";
            s.Text = s.Text.Trim();
        }
        d.Title = Cut(d.Title.Trim(), 100);
        d.Tags = d.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(15).ToList();
        d.Hooks = d.Hooks.Select(t => t.Trim()).Where(t => t.Length > 0).Take(3).ToList();
        return d;
    }

    public static string Template(JsonObject story) => Str(story["template"]);

    /// <summary>Zrzuty użyte w scenach (do sekcji „screens” w episode.json i do pilnowania RODO).</summary>
    public static IEnumerable<string> ScreensUsed(IEnumerable<JsonObject> stories) =>
        stories.Where(s => Template(s) == "ad-screen").Select(s => Str(s["screen"])).Where(f => f.Length > 0).Distinct();

    // ── Poprawki językowe ────────────────────────────────────────────

    /// <summary>
    /// Wkleja do ujęcia tylko polskie teksty z wersji po redakcji: napisy, dymki, wiadomości. Szablon, liczby (y, scroll),
    /// nazwy plików, nastroje i strony zostają z oryginału — redaktor nie może zepsuć animacji.
    /// </summary>
    public static JsonObject MergeTexts(JsonObject original, JsonObject? edited)
    {
        var result = (JsonObject)original.DeepClone();
        if (edited is null) return result;
        foreach (var (key, value) in original)
        {
            if (key is "template" or "screen" or "mood" or "side" or "from" or "y" or "scroll") continue;
            var e = edited[key];
            switch (value)
            {
                case JsonValue v when v.TryGetValue<string>(out _) && e is JsonValue ev && ev.TryGetValue<string>(out var text) && text.Trim().Length > 0:
                    result[key] = text.Trim();
                    break;
                case JsonArray arr when e is JsonArray earr && earr.Count == arr.Count:
                    var merged = new JsonArray();
                    for (var i = 0; i < arr.Count; i++)
                        merged.Add(arr[i] switch
                        {
                            JsonObject o => MergeTexts(o, earr[i] as JsonObject),
                            JsonValue sv when sv.TryGetValue<string>(out _) && earr[i] is JsonValue esv && esv.TryGetValue<string>(out var s) && s.Trim().Length > 0 => s.Trim(),
                            var other => other?.DeepClone(),
                        });
                    result[key] = merged;
                    break;
            }
        }
        return result;
    }

    // ── Pliki dla renderera ──────────────────────────────────────────

    /// <summary>episode.json reklamy (format „ad”) — to, co czyta studio/src/ad/AdShort.tsx.</summary>
    public static string EpisodeJson(StudioVideo v, IReadOnlyList<StudioScene> scenes, string website, string screensDir,
        IReadOnlyDictionary<string, (int W, int H)> screenSizes, object sound)
    {
        var stories = scenes.Select(s => StoryOf(JsonNode.Parse(string.IsNullOrWhiteSpace(s.StoryJson) ? "{}" : s.StoryJson))).ToList();
        var used = ScreensUsed(stories).Where(screenSizes.ContainsKey).ToDictionary(f => f, f => new { w = screenSizes[f].W, h = screenSizes[f].H });
        var root = new
        {
            id = v.Slug,
            title = v.Title,
            format = "ad",
            website,
            screensDir,
            theme = new { accent = Accent, background = Background },
            channel = new { name = "Hantly", tagline = "" },
            sound,
            screens = used,
            scenes = scenes.Select((s, i) => new
            {
                kind = s.Kind, number = s.Number, headline = s.Headline, keyline = "", icon = s.Icon,
                expression = "happy", gesture = "point", text = s.Text, story = stories[i],
            }),
        };
        return JsonSerializer.Serialize(root, StudioRules.Json);
    }

    /// <summary>opis.txt do ręcznej publikacji: tytuł, opis, hashtagi, komentarz do przypięcia, warianty haka.</summary>
    public static string DescriptionTxt(StudioVideo v, IReadOnlyList<string> tags, IReadOnlyList<string> hooks, string website, bool garmin)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TYTUŁ").AppendLine(v.Title).AppendLine();
        sb.AppendLine("OPIS").AppendLine(v.Description.Trim());
        var hashtags = tags.Select(t => "#" + t.TrimStart('#').Replace(" ", "")).Where(t => t.Length > 1).Distinct().ToList();
        if (hashtags.Count > 0 && !v.Description.Contains('#')) sb.AppendLine().AppendLine(string.Join(" ", hashtags));
        if (garmin && (v.Title + v.Description).Contains("Garmin", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine().AppendLine(StudioService.GarminAttribution);
        sb.AppendLine().AppendLine("KOMENTARZ DO PRZYPIĘCIA").AppendLine(v.PinnedComment.Trim());
        if (hooks.Count > 0)
        {
            sb.AppendLine().AppendLine("INNE OTWARCIA (do testów)");
            foreach (var h in hooks) sb.AppendLine("- " + h);
        }
        sb.AppendLine().AppendLine("STRONA").AppendLine(website);
        return sb.ToString();
    }

    /// <summary>Słowa lektora i szacunkowy czas (ok. 2,6 słowa na sekundę).</summary>
    public static (int Words, double Seconds) Length(IEnumerable<string> texts)
    {
        var words = texts.Sum(t => t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        return (words, Math.Round(words / 2.6, 0));
    }

    /// <summary>Nazwa katalogu filmu dla renderera — tylko [a-z0-9-].</summary>
    public static string NewSlug(DateTime utcNow) =>
        $"reklama-{utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Random.Shared.Next(0x1000, 0xFFFF):x4}";

    // ── Pomocnicze ───────────────────────────────────────────────────

    public static string Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public static List<string> Strings(JsonNode? n) =>
        n is JsonArray a ? a.Select(Str).Where(s => s.Length > 0).ToList() : [];

    public static string Cut(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();
}
