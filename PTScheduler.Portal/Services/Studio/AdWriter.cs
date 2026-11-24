using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;
using R = PTScheduler.Portal.Services.Studio.StudioRules;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Scenarzysta reklam wideo 9:16 (AI przez OpenRouter): pisze spot z Hantelkiem jako lektorem, sprawdza fakty względem
/// wiedzy o produkcie, redaguje polszczyznę, przygotowuje tekst dla lektora i ogląda klatki gotowego filmu.
/// Schemat odpowiedzi jest w promptcie, a pilnuje go kod (AdRules) — bez structured outputs.
/// </summary>
public class AdWriter(AiService ai, StudioService studio, StudioScreens screens, SiteSettingsService settings, ILogger<AdWriter> logger)
{
    private const int Timeout = 240;

    private async Task<string> ModelAsync() => (await studio.OptionsAsync()).Model;

    private async Task<string> AskAsync(string system, string user, int maxTokens, CancellationToken ct, IReadOnlyList<string>? images = null)
    {
        var r = await ai.CompleteAsync(StudioService.Feature, null, system, user, maxTokens: maxTokens, ct: ct,
            modelOverride: await ModelAsync(), timeoutSeconds: Timeout, imagesJpegBase64: images);
        return r.Text ?? throw new InvalidOperationException(r.Error ?? "AI nie odpowiedziało.");
    }

    public async Task<string> WebsiteAsync() => "https://" + (await settings.GetAsync(K.MainDomain)).Trim().TrimEnd('/');

    // ── Scenariusz ───────────────────────────────────────────────────

    /// <summary>Pisze (albo poprawia z uwagą) reklamę. Zwraca szkic po walidacji, sprawdzeniu faktów i redakcji.</summary>
    public async Task<AdRules.Draft> WriteAsync(string brief, bool garmin, AdRules.Draft? previous, string? comment, CancellationToken ct)
    {
        var knowledge = await studio.KnowledgeAsync()
            ?? throw new StudioJobFatalException("Najpierw zbuduj wiedzę o Hantly (Studio reklam → Wiedza o Hantly).");
        var k = StudioService.ForPrompt(knowledge, garmin);
        var state = await screens.StateAsync();
        var usable = StudioScreens.Usable(state, garmin);
        var website = await WebsiteAsync();
        var shortUrl = website.Replace("https://", "");
        var system = SystemPrompt(k, garmin, usable, shortUrl, (await settings.GetAsync(K.InstagramVoice)).Trim());

        var user = previous is null
            ? $"Zrób reklamę.\nBrief: {(string.IsNullOrWhiteSpace(brief) ? "wybierz najmocniejszą bolączkę trenera i funkcję, która ją rozwiązuje" : brief)}"
              + (string.IsNullOrWhiteSpace(comment) ? "" : $"\n\nUwagi właściciela: {comment}")
            : $"""
              Popraw tę reklamę zgodnie z uwagami właściciela. Zachowaj to, co dobre; zmień tylko to, czego dotyczą uwagi.
              Uwagi: {comment}

              Obecna wersja:
              {DraftJson(previous)}
              """;
        user += $"\n\nOdpowiedz JSON-em wg schematu:\n{Schema}";

        var text = await AskAsync(system, user, 7000, ct);
        var draft = AdRules.ParseDraft(text) ?? throw new InvalidOperationException("AI zwróciło scenariusz w złym formacie.");
        var cta = k.Cta.Length is > 0 and <= 50 ? k.Cta : shortUrl;
        draft = AdRules.Sanitize(draft, usable.Select(s => s.File).ToList(), cta);

        // Fakty i polszczyzna — błąd którejkolwiek poprawki nie przekreśla scenariusza.
        try { draft = await FactCheckAsync(draft, k, garmin, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException) { logger.LogWarning(ex, "Studio: sprawdzanie faktów pominięte."); }
        try { draft = await EditPolishAsync(draft, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException) { logger.LogWarning(ex, "Studio: redakcja językowa pominięta."); }
        return AdRules.Sanitize(draft, usable.Select(s => s.File).ToList(), cta);
    }

    private const string Schema = """
        {"title":"≤ 60 znaków, 1 emoji","description":"2 zdania korzyści, adres strony, potem 4–6 hashtagów w jednej linii (pierwszy #shorts)",
         "tags":["5–10 tagów bez #"],"hooks":["3 alternatywne otwarcia (inne pomysły na scenę 0)"],
         "pinnedComment":"pytanie do trenerów o ich największą bolączkę + jak zacząć",
         "scenes":[{"kind":"hook|fact|outro","headline":"etykieta ≤ 22 znaki","icon":"1 emoji","text":"lektor: 1–2 zdania, ≤ ok. 14 słów","story":{"template":"…","…":"parametry ujęcia"}}]}
        """;

    private static string SystemPrompt(R.Knowledge k, bool garmin, List<StudioScreens.Screen> usable, string shortUrl, string brandVoice) => $"""
        Jesteś reżyserem i copywriterem krótkich reklam aplikacji (Reels/TikTok/Shorts 9:16) dla Hantly — polskiej aplikacji dla trenerów personalnych, strona: {shortUrl}.
        To ma być PROFESJONALNA REKLAMA, a nie film edukacyjny: szybki montaż, emocja, konkretne ekrany produktu, mocne wezwanie do działania.

        ## Kto mówi
        Lektorem i bohaterem spotu jest Hantelek — asystent AI z aplikacji Hantly (fioletowa hantelka z miną, maskotka z aplikacji).
        Mówi do trenera na „ty”, energicznie i ciepło, z lekkim humorem, krótkimi zdaniami. Jak dobry spot, nie jak wykład.

        ## Struktura (6–8 scen, łącznie 55–85 słów lektora, ok. 22–35 s)
        1. Scena 0 (kind "hook", ujęcie "ad-hook"): bolączka trenera w 1–2 sekundy. Najwyżej 8 słów lektora, bez nazwy produktu.
        2. Zwrot akcji: "ad-mascot" (Hantelek wchodzi: „Spokojnie, mam to.”) albo od razu pierwszy ekran.
        3. Pokaz produktu: co najmniej 2 sceny "ad-screen" z PRAWDZIWYMI zrzutami z listy niżej (jeśli lista jest pusta — zamiast nich "ad-benefits" i "ad-compare")
           i dokładnie 1 scena "ad-assistant" — asystent AI przy pracy na konkretnym przykładzie z wiedzy o produkcie (fikcyjne imiona).
           Opcjonalnie "ad-compare" albo "ad-benefits" jako przyspieszenie.
        4. Ostatnia scena (kind "outro", ujęcie "ad-cta"): wezwanie do działania z wiedzy o produkcie (darmowy start, cena, adres {shortUrl}).
        - Tekst lektora sceny MUSI pasować do tego, co widać w tej scenie.
        - Napisy na ekranie nie powtarzają lektora słowo w słowo — dopowiadają (lektor mówi emocję, ekran pokazuje konkret).
        - W scenariuszu liczby cyframi (lektor dostanie osobno wersję słowną).

        ## Ujęcia (pole "story" każdej sceny = obiekt JSON z polem "template" i parametrami; trzymaj się limitów znaków)
        {AdRules.Catalog}

        ## Zrzuty aplikacji (plik: co widać [funkcja; widok; wysokość] dymki)
        {(usable.Count == 0 ? "Brak zrzutów — nie używaj ujęcia ad-screen." : StudioScreens.ForPrompt(usable))}

        ## Twarde zasady
        - Tylko prawdziwe funkcje, liczby i ceny z WIEDZY poniżej. Nie wymyślaj opinii, statystyk, liczby klientów ani funkcji.
        - Tylko fikcyjne imiona klientów (Ania, Marek…). Bez korpomowy i bez „rewolucyjny/innowacyjny/kompleksowe rozwiązanie”.
        - Ekran w scenie "ad-screen" musi pokazywać to, o czym mówi lektor (sprawdź opis zrzutu).
        - Czego nie mówić: {string.Join(" ", k.DoNotSay.Concat(R.AlwaysDoNotSay).Distinct())}
        {(garmin ? StudioService.GarminOn : StudioService.GarminOff)}

        {StudioService.PolishRules}
        {(brandVoice.Length > 0 ? "Styl marki od właściciela: " + brandVoice : "")}

        Odpowiadasz wyłącznie poprawnym JSON-em, bez komentarzy i bez ```.

        WIEDZA O PRODUKCIE (JSON):
        {JsonSerializer.Serialize(k, R.Json)}
        """;

    public static string DraftJson(AdRules.Draft d) => new JsonObject
    {
        ["title"] = d.Title, ["description"] = d.Description, ["tags"] = new JsonArray([.. d.Tags.Select(t => (JsonNode)t)]),
        ["hooks"] = new JsonArray([.. d.Hooks.Select(t => (JsonNode)t)]), ["pinnedComment"] = d.PinnedComment,
        ["scenes"] = new JsonArray([.. d.Scenes.Select((s, i) => (JsonNode)new JsonObject
        {
            ["index"] = i, ["kind"] = s.Kind, ["headline"] = s.Headline, ["icon"] = s.Icon, ["text"] = s.Text, ["story"] = s.Story.DeepClone(),
        })]),
    }.ToJsonString(R.Json);

    // ── Fakty ─────────────────────────────────────────────────────────

    private async Task<AdRules.Draft> FactCheckAsync(AdRules.Draft d, R.Knowledge k, bool garmin, CancellationToken ct)
    {
        var system = $"""
            Sprawdzasz reklamę Hantly wyłącznie względem WIEDZY O PRODUKCIE (bez internetu).
            Każda wspomniana funkcja, liczba, cena i obietnica — w tekście lektora i w napisach na ekranie — musi wynikać z wiedzy.
            Zmyślone albo przesadzone popraw: podobna długość, ten sam styl mówiony, polszczyzna jak u rodowitego Polaka.
            {(garmin ? StudioService.GarminOn : StudioService.GarminOff)}
            Odpowiadasz wyłącznie poprawnym JSON-em, bez komentarzy i bez ```.

            WIEDZA O PRODUKCIE (JSON):
            {JsonSerializer.Serialize(k, R.Json)}
            """;
        var user = $$$"""
            Sprawdź reklamę. Zwróć tylko sceny, które trzeba poprawić (index od 0). W "story" podaj całe ujęcie z poprawionymi tekstami
            (bez zmiany szablonu i liczb) albo pomiń pole, gdy napisy są w porządku.
            {"ok":true,"summary":"krótko po polsku, co sprawdzono i poprawiono","fixes":[{"index":0,"text":"poprawiony lektor","story":{…}}]}

            Reklama:
            {{{DraftJson(d)}}}
            """;
        var json = InstagramRules.JsonPart(await AskAsync(system, user, 4000, ct)) ?? throw new InvalidOperationException("Sprawdzanie faktów: zły format.");
        var root = JsonNode.Parse(json) as JsonObject;
        foreach (var fix in root?["fixes"] as JsonArray ?? [])
        {
            if (fix?["index"] is not JsonValue iv || !iv.TryGetValue<int>(out var i) || i < 0 || i >= d.Scenes.Count) continue;
            if (AdRules.Str(fix["text"]) is { Length: > 0 } t) d.Scenes[i].Text = t.Trim();
            if (fix["story"] is JsonObject st) d.Scenes[i].Story = AdRules.MergeTexts(d.Scenes[i].Story, st);
        }
        logger.LogInformation("Studio: fakty — {Summary}", AdRules.Str(root?["summary"]));
        return d;
    }

    // ── Polszczyzna ──────────────────────────────────────────────────

    private async Task<AdRules.Draft> EditPolishAsync(AdRules.Draft d, CancellationToken ct)
    {
        var system = $"""
            Jesteś doświadczonym polskim redaktorem tekstów reklamowych do radia i social mediów.
            Sprawiasz, że scenariusz brzmi jak napisany od razu po polsku przez świetnego copywritera — nie jak tłumaczenie.
            Nie zmieniaj faktów, liczb, cen, kolejności scen ani sensu. Długość każdej sceny ma zostać podobna (±15%).
            Zmieniaj wyłącznie polskie teksty: lektor, etykiety, napisy i dymki w ujęciach. Nigdy szablonu, nazw plików, liczb sterujących (y, scroll), nastroju ani strony.
            {StudioService.PolishRules}
            Odpowiadasz wyłącznie poprawnym JSON-em, bez komentarzy i bez ```.
            """;
        var user = $$$"""
            Zredaguj tę reklamę. Zwróć WSZYSTKIE sceny (także bez zmian) z tymi samymi indeksami oraz tytuł i opis:
            {"title":"…","description":"…","scenes":[{"index":0,"headline":"…","text":"…","story":{…}}]}

            {{{DraftJson(d)}}}
            """;
        var json = InstagramRules.JsonPart(await AskAsync(system, user, 7000, ct)) ?? throw new InvalidOperationException("Redakcja: zły format.");
        var root = JsonNode.Parse(json) as JsonObject;
        if (AdRules.Str(root?["title"]) is { Length: > 0 } title) d.Title = title;
        if (AdRules.Str(root?["description"]) is { Length: > 0 } desc) d.Description = desc;
        foreach (var s in root?["scenes"] as JsonArray ?? [])
        {
            if (s?["index"] is not JsonValue iv || !iv.TryGetValue<int>(out var i) || i < 0 || i >= d.Scenes.Count) continue;
            if (AdRules.Str(s["headline"]) is { Length: > 0 } h) d.Scenes[i].Headline = h;
            if (AdRules.Str(s["text"]) is { Length: > 0 } t) d.Scenes[i].Text = t;
            if (s["story"] is JsonObject st) d.Scenes[i].Story = AdRules.MergeTexts(d.Scenes[i].Story, st);
        }
        return d;
    }

    // ── Tekst dla lektora ────────────────────────────────────────────

    /// <summary>Liczby, daty, procenty i skróty słownie w poprawnym przypadku — reszta słów i szyk bez zmian.</summary>
    public async Task<Dictionary<int, string>> SpeechAsync(IReadOnlyList<(int Index, string Text)> scenes, CancellationToken ct)
    {
        const string system = """
            Przygotowujesz tekst dla polskiego lektora syntezatora mowy (TTS). Syntezator źle czyta cyfry, skróty i symbole,
            więc zapisujesz je słownie, dokładnie tak, jak wymawia je Polak — w poprawnym przypadku i rodzaju.
            - Liczby słownie i odmienione: „po 3 tygodniach” → „po trzech tygodniach”; „8 osób” → „ośmiu osób” albo „osiem osób” zależnie od zdania.
            - Kwoty: „7 250 zł” → „siedem tysięcy dwieście pięćdziesiąt złotych”; „0 zł” → „zero złotych”; „49 zł/mies.” → „czterdzieści dziewięć złotych miesięcznie”.
            - Procenty i zakresy: „70–80%” → „od siedemdziesięciu do osiemdziesięciu procent”.
            - Ułamki: „1,5 kg” → „półtora kilograma”. Godziny: „23:07” → „dwudziesta trzecia zero siedem”. „24/7” → „dwadzieścia cztery na siedem”.
            - Skróty: „np.” → „na przykład”, „ok.” → „około”, „zł” → odmienione „złotych/złote”. Skrótowce literowane po polsku (KSeF → „ksef”, PDF → „pe-de-ef”), BLIK zostaw.
            - Adresy stron: „hantly.pl” → „hantly kropka pe el”.
            - NIE zmieniaj żadnych innych słów, kolejności ani interpunkcji. Nie dodawaj i nie usuwaj treści.
            Odpowiadasz wyłącznie poprawnym JSON-em: {"scenes":[{"index":0,"speech":"…"}]}
            """;
        var user = "Tekst dla lektora każdej sceny (te same indeksy):\n" + string.Join("\n", scenes.Select(s => $"[{s.Index}] {s.Text}"));
        var json = InstagramRules.JsonPart(await AskAsync(system, user, 3000, ct)) ?? throw new InvalidOperationException("Tekst dla lektora: zły format.");
        var result = new Dictionary<int, string>();
        foreach (var n in (JsonNode.Parse(json) as JsonObject)?["scenes"] as JsonArray ?? [])
            if (n?["index"] is JsonValue iv && iv.TryGetValue<int>(out var i) && AdRules.Str(n["speech"]).Trim() is { Length: > 0 } sp)
                result[i] = sp;
        return result;
    }

    // ── Kontrola klatek ──────────────────────────────────────────────

    /// <summary>AI ogląda po 2 klatki z każdej sceny i zgłasza usterki obrazu. Zwraca JSON {ok, summary, issues[]}.</summary>
    public async Task<string> ReviewFramesAsync(string episodeJson, IReadOnlyList<RendererClient.Still> stills, CancellationToken ct)
    {
        const string system = """
            Jesteś redaktorem wideo krótkich reklam (pion 9:16). Oceniasz klatki z gotowego filmu.
            Zgłaszaj tylko realne problemy widoczne na klatkach: elementy nachodzące na siebie, ucięte albo nieczytelne napisy,
            tekst wychodzący poza dymek lub kadr, napisy zasłonięte przez telefon albo maskotkę, pusty telefon, scena niepasująca do treści,
            literówki i błędy językowe w napisach. Nie zgłaszaj kwestii gustu. Pisz po polsku, krótko.
            Odpowiadasz wyłącznie poprawnym JSON-em:
            {"ok":true,"summary":"1–2 zdania","issues":[{"scene":0,"severity":"wysoka|średnia|niska","problem":"co jest nie tak","fix":"jak poprawić (np. krótszy tekst w dymku, inne ujęcie)"}]}
            """;
        var sb = new StringBuilder("Scenariusz (do porównania z obrazem):\n").AppendLine(episodeJson).AppendLine().AppendLine("Klatki w kolejności obrazów:");
        foreach (var (s, i) in stills.Select((s, i) => (s, i))) sb.AppendLine($"{i + 1}. scena {s.Scene}, {s.Ratio * 100:0}% sceny");
        var json = InstagramRules.JsonPart(await AskAsync(system, sb.ToString(), 2500, ct, stills.Select(s => s.Jpeg).ToList()))
            ?? throw new InvalidOperationException("Kontrola klatek: zły format.");
        return (JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("Kontrola klatek: zły format.")).ToJsonString(R.Json);
    }
}
