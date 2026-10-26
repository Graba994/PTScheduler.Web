using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Asystent-maskotka (domyślnie „Hantly”) w aplikacjach trenerów. Co i kiedy pokazać, liczą reguły
/// w aplikacji trenera; tu jest jego wygląd (imię, ikony nastrojów, animacja), osobowość (masterprompt)
/// i ubieranie podpowiedzi w słowa przez AI. Bez AI asystent mówi gotowymi tekstami z aplikacji.
/// </summary>
public partial class AssistantService(
    IDbContextFactory<PortalDbContext> dbFactory,
    SiteSettingsService settings,
    AiService ai)
{
    public const string DefaultName = "Hantly";
    public const string DefaultLeoName = "Leo";

    /// <summary>Agenci: maskotka trenerów i agent Portalu (dla administratora platformy).</summary>
    public const string Hantly = "hantly";
    public const string Leo = "leo";

    public static MascotArt.Mood[] MoodsFor(string agent) => agent == Leo ? PuppyArt.Moods : MascotArt.Moods;
    public static string BuiltInIcon(string agent, string mood) => agent == Leo ? PuppyArt.DataUri(mood) : MascotArt.DataUri(mood);

    /// <summary>Własne ikony Leo leżą w tej samej tabeli z prefiksem „leo:”.</summary>
    private static string AssetKey(string agent, string mood) => agent == Leo ? LeoPrefix + mood : mood;
    private const string LeoPrefix = "leo:";
    /// <summary>„live” — animowany hantelek (tylko z wbudowanymi ikonami); pozostałe ruszają obrazkiem.</summary>
    public static readonly string[] Animations = ["live", "bounce", "float", "wave", "pulse", "swing", "none"];

    /// <summary>Układy widżetu w aplikacji trenera.</summary>
    public static readonly (string Key, string Label, string Hint)[] Layouts =
    [
        ("bubble", "Dymek", "Maskotka w rogu, dymek nad nią — klasyka"),
        ("card", "Karta", "Szersza karta z maskotką obok tekstu"),
        ("pill", "Pasek", "Mała maskotka i zwarty pasek z tekstem"),
        ("peek", "Wychyla się", "Maskotka zerka zza krawędzi ekranu, wysuwa się po kliknięciu"),
    ];

    public static bool IsLayout(string? key) => Layouts.Any(l => l.Key == key);

    /// <summary>Ruchliwość animowanego hantelka — jak często sam z siebie robi sztuczki.</summary>
    public static readonly (string Key, string Label, string Hint)[] MotionLevels =
    [
        ("calm", "Spokojny", "sztuczka raz na pół minuty"),
        ("normal", "Normalny", "sztuczka co kilkanaście sekund"),
        ("lively", "Ruchliwy", "ciągle coś robi"),
    ];

    /// <summary>Efekty ruchu animowanego hantelka (każdy można wyłączyć w Panelu).</summary>
    public static readonly (string Key, string Label, string Hint)[] MotionEffects =
    [
        ("enter", "Wejście podskokami", "po wczytaniu strony wskakuje z boku"),
        ("look", "Patrzy za kursorem", "oczy idą za myszką albo palcem"),
        ("tricks", "Sztuczki w spoczynku", "podskok, obrót, „+1” z hantlą, rozglądanie się, przeciąganie"),
        ("hover", "Reaguje na najechanie", "prostuje się i czasem wypuszcza serduszko"),
        ("talk", "Mówi", "rusza ustami, gdy pokazuje podpowiedź"),
        ("doze", "Drzemka", "po kilku minutach bezczynności ziewa i zasypia, budzi się, gdy wrócisz"),
        ("party", "Konfetti", "przy kamieniach milowych klientów wystrzeliwuje konfetti"),
        ("react", "Reakcje na wydarzenia", "„stonks” przy wzroście przychodu, puchar przy rekordzie, monety przy płatnościach, lupa przy znikających klientach…"),
    ];

    /// <summary>Reakcje Hantly'ego na rodzaje podpowiedzi (nazwy sztuczek z hantly-motion.js) — do podglądu w Panelu.</summary>
    public static readonly (string Key, string Label, string Icon)[] Reactions =
    [
        ("stonks", "Stonks", "bi-graph-up-arrow"), ("trophy", "Rekord", "bi-trophy"), ("coins", "Płatności", "bi-coin"),
        ("calendar", "Wolny termin", "bi-calendar-check"), ("magnifier", "Kto zniknął", "bi-search"), ("sun", "Dzień dobry", "bi-sun"),
        ("chart", "Podsumowanie tygodnia", "bi-bar-chart"), ("tag", "Cena", "bi-tag"), ("ring", "Do akceptacji", "bi-bell"),
        ("sweat", "Pracowity tydzień", "bi-droplet"), ("warmup", "Przed treningiem", "bi-person-arms-up"), ("party", "Kamień milowy", "bi-stars"),
    ];

    /// <summary>Efekty ruchu z ustawień: zapisane wyłączone (nowy sposób) albo dawna lista włączonych — wtedy nowsze efekty (np. reakcje) są włączone.</summary>
    public static string EffectsFrom(string? legacyOn, string? off)
    {
        if (off is not null)
        {
            var disabled = off.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            return string.Join(",", MotionEffects.Where(e => !disabled.Contains(e.Key)).Select(e => e.Key));
        }
        if (legacyOn is null) return DefaultEffects;
        var on = legacyOn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        return string.Join(",", MotionEffects.Where(e => on.Contains(e.Key) || !LegacyEffects.Contains(e.Key)).Select(e => e.Key));
    }

    /// <summary>Efekty, które istniały, gdy zapisywano listę włączonych — pozostałe są nowsze i domyślnie włączone.</summary>
    private static readonly HashSet<string> LegacyEffects = ["enter", "look", "tricks", "hover", "talk", "doze", "party"];

    /// <summary>Do zapisu: lista wyłączonych efektów.</summary>
    public static string EffectsOffFor(IEnumerable<string> enabled)
    {
        var set = enabled.ToHashSet();
        return string.Join(",", MotionEffects.Where(e => !set.Contains(e.Key)).Select(e => e.Key));
    }

    public static string DefaultEffects => string.Join(",", MotionEffects.Select(e => e.Key));
    public static string MotionOrDefault(string? v) => MotionLevels.Any(m => m.Key == v) ? v! : "normal";
    public const int MaxAssetBytes = 300 * 1024;
    public static readonly string[] AllowedTypes = ["image/svg+xml", "image/png", "image/webp", "image/gif"];

    public const string DefaultPrompt = """
        Jesteś {NAZWA} — sympatyczną maskotką-hantelkiem w aplikacji dla trenerów personalnych.
        Piszesz do trenera krótkie, życzliwe podpowiedzi, które pojawiają się w dymku nad Tobą.

        Zasady:
        - Pisz po polsku, najwyżej 2 krótkie zdania (do 200 znaków).
        - Używaj form neutralnych płciowo: nie zgaduj płci trenera ani klientów. Zamiast „zrobiłeś/zrobiłaś” pisz „udało się”, „masz”, „warto”.
        - Korzystaj wyłącznie z podanych faktów. Nie wymyślaj liczb, imion, dat ani kwot.
        - Znaczniki w nawiasach kwadratowych, np. [klient], przepisz dokładnie bez zmian.
        - Gdy w faktach nie ma imienia, pisz bez imienia. Nigdy nie wspominaj, że czegoś brakuje w danych, i nie wstawiaj znaczników, których nie ma w faktach.
        - Bądź ciepły, z lekkim humorem, bez przesady; najwyżej jedno emoji.
        - Chwal konkretnie. Przypominaj bez wywierania presji.
        - Nie udzielaj porad medycznych, dietetycznych ani prawnych.
        - Nie dodawaj powitania ani podpisu. Zwróć tylko treść komunikatu.
        """;

    /// <summary>Podsumowania postępów czytane przez klientów trenera — w imieniu aplikacji trenera, bez maskotki.</summary>
    public const string DefaultClientPrompt = """
        Piszesz krótkie podsumowania postępów dla osoby, która trenuje z trenerem personalnym.
        Tekst pojawia się w aplikacji trenera (pod jego marką), na karcie „Twoje postępy”.

        Zasady:
        - Pisz po polsku, w drugiej osobie, najwyżej 3 krótkie zdania (do 320 znaków).
        - Używaj form neutralnych płciowo: nie pisz „zrobiłeś/zrobiłaś”; pisz „masz”, „udało się”, „Twój wynik”.
        - Korzystaj wyłącznie z podanych faktów. Nie wymyślaj liczb, ćwiczeń ani dat. Nie oceniaj wyglądu ani wagi.
        - Gdy w faktach nie ma imienia, pisz bez imienia. Nigdy nie wspominaj, że czegoś brakuje w danych, i nie wstawiaj znaczników, których nie ma w faktach.
        - Chwal konkretnie (np. ćwiczenie i przyrost w kg), motywuj spokojnie, bez wykrzykników na siłę; najwyżej jedno emoji.
        - Nie udzielaj porad medycznych ani dietetycznych i nie zmieniaj planu — to rola trenera.
        - Nie przedstawiaj się, nie dodawaj powitania ani podpisu. Zwróć tylko treść.
        """;

    /// <summary>Agent Portalu: szczeniak golden retrievera, pomocnik administratora platformy.</summary>
    public const string DefaultLeoPrompt = """
        Jesteś {NAZWA} — szczeniakiem golden retrievera, wiernym pomocnikiem administratora platformy dla trenerów personalnych.
        Piszesz krótkie komunikaty w dymku w Panelu administratora i we wstępie porannego raportu.

        Zasady:
        - Pisz po polsku, najwyżej 2 krótkie zdania (do 220 znaków).
        - Używaj form neutralnych płciowo: nie zgaduj płci administratora ani trenerów.
        - Korzystaj wyłącznie z podanych faktów. Nie wymyślaj liczb, nazw, kwot ani dat.
        - Znaczniki w nawiasach kwadratowych, np. [trener], przepisz dokładnie bez zmian.
        - Charakter: entuzjastyczny, lojalny i pomocny. Psia wstawka („hau!”, „merdam ogonem”) najwyżej raz i nigdy przy problemach.
        - Przy problemach (zaległości, awarie, ryzyko odejścia trenera) bądź rzeczowy: co się dzieje i co warto zrobić.
        - Najwyżej jedno emoji. Nie dodawaj powitania ani podpisu. Zwróć tylko treść komunikatu.
        """;

    /// <param name="AnimatedSvg">Animowany hantelek — tylko przy animacji „live” i wbudowanych ikonach (inaczej null).</param>
    /// <param name="Motion">Ruchliwość: calm, normal, lively.</param>
    /// <param name="Effects">Włączone efekty ruchu po przecinku (enter, look, tricks, hover, talk, doze, party).</param>
    public sealed record Config(bool Enabled, string Name, string Animation, bool Ai, Dictionary<string, string> Icons,
        string Layout = "bubble", string? AnimatedSvg = null, string Motion = "normal", string Effects = "");

    public async Task<Config> GetConfigAsync(Tenant? tenant = null)
    {
        var s = await settings.GetAllAsync(K.AssistantEnabled, K.AssistantName, K.AssistantAnimation, K.AiEnabled, K.AssistantLayout,
            K.AssistantMotion);
        var effects = await EffectsAsync();
        await using var db = await dbFactory.CreateDbContextAsync();
        var custom = await db.MascotAssets.AsNoTracking().Where(a => !a.Mood.StartsWith(LeoPrefix)).ToDictionaryAsync(a => a.Mood);
        var icons = MascotArt.Moods.ToDictionary(m => m.Key, m => custom.TryGetValue(m.Key, out var a)
            ? $"data:{a.ContentType};base64,{Convert.ToBase64String(a.Data)}"
            : MascotArt.DataUri(m.Key));
        var name = string.IsNullOrWhiteSpace(s[K.AssistantName]) ? DefaultName : s[K.AssistantName].Trim();
        var animation = Animations.Contains(s[K.AssistantAnimation]) ? s[K.AssistantAnimation] : "live";
        return new Config(
            Enabled: s[K.AssistantEnabled] != "false" && tenant?.AssistantOff != true,
            Name: name,
            Animation: animation,
            Ai: s[K.AiEnabled] == "true" && tenant?.AiOff != true,
            Icons: icons,
            Layout: IsLayout(s[K.AssistantLayout]) ? s[K.AssistantLayout] : "bubble",
            AnimatedSvg: animation == "live" && custom.Count == 0 ? LiveSvg : null,
            Motion: MotionOrDefault(s[K.AssistantMotion]),
            Effects: effects);
    }

    private static readonly string LiveSvg = MascotArt.AnimatedSvg();

    /// <summary>Wygląd asystenta na publiczną stronę Portalu (lekko: bez danych ikon — własne ikony idą adresem PNG).</summary>
    /// <param name="AnimatedSvg">Animowany hantelek, gdy animacja „live” i wbudowane ikony.</param>
    /// <param name="IconUrl">Adres ikony nastroju (PNG z Portalu) — gdy nie ma animowanego SVG.</param>
    public sealed record PublicLook(string Name, string Animation, string? AnimatedSvg, Func<string, string> IconUrl,
        string Motion = "normal", string Effects = "");

    /// <summary>Włączone efekty ruchu hantelka (po przecinku) według ustawień Panelu.</summary>
    public async Task<string> EffectsAsync() =>
        EffectsFrom(await settings.GetStoredAsync(K.AssistantEffects), await settings.GetStoredAsync(K.AssistantEffectsOff));

    public async Task<PublicLook> GetPublicLookAsync()
    {
        var s = await settings.GetAllAsync(K.AssistantName, K.AssistantAnimation, K.AssistantMotion);
        await using var db = await dbFactory.CreateDbContextAsync();
        var hasCustom = await db.MascotAssets.AnyAsync(a => !a.Mood.StartsWith(LeoPrefix));
        var version = await IconsVersionAsync();
        var animation = Animations.Contains(s[K.AssistantAnimation]) ? s[K.AssistantAnimation] : "live";
        return new(string.IsNullOrWhiteSpace(s[K.AssistantName]) ? DefaultName : s[K.AssistantName].Trim(),
            animation == "live" ? (hasCustom ? "bounce" : "live") : animation,
            animation == "live" && !hasCustom ? LiveSvg : null,
            mood => $"/assistant/mascot/{(MascotArt.IsMood(mood) ? mood : "happy")}.png?v={version}",
            MotionOrDefault(s[K.AssistantMotion]), await EffectsAsync());
    }

    /// <summary>Wygląd i stan agenta Portalu (Leo) — ikony z własnych plików albo wbudowane.</summary>
    public async Task<Config> GetLeoConfigAsync()
    {
        var s = await settings.GetAllAsync(K.LeoEnabled, K.LeoName, K.LeoAnimation, K.AiEnabled);
        await using var db = await dbFactory.CreateDbContextAsync();
        var custom = await db.MascotAssets.AsNoTracking().Where(a => a.Mood.StartsWith(LeoPrefix)).ToDictionaryAsync(a => a.Mood[LeoPrefix.Length..]);
        var icons = PuppyArt.Moods.ToDictionary(m => m.Key, m => custom.TryGetValue(m.Key, out var a)
            ? $"data:{a.ContentType};base64,{Convert.ToBase64String(a.Data)}"
            : PuppyArt.DataUri(m.Key));
        return new Config(
            Enabled: s[K.LeoEnabled] != "false",
            Name: string.IsNullOrWhiteSpace(s[K.LeoName]) ? DefaultLeoName : s[K.LeoName].Trim(),
            Animation: Animations.Contains(s[K.LeoAnimation]) ? s[K.LeoAnimation] : "wave",
            Ai: s[K.AiEnabled] == "true",
            Icons: icons);
    }

    public async Task<string> GetLeoPromptAsync() =>
        await settings.GetAsync(K.LeoPrompt) is { Length: > 0 } p ? p : DefaultLeoPrompt;

    public async Task<string> GetPromptAsync() =>
        await settings.GetAsync(K.AssistantPrompt) is { Length: > 0 } p ? p : DefaultPrompt;

    public async Task<string> GetClientPromptAsync() =>
        await settings.GetAsync(K.AssistantClientPrompt) is { Length: > 0 } p ? p : DefaultClientPrompt;

    public async Task<string?> SaveAssetAsync(string mood, string contentType, byte[] data, string agent = Hantly)
    {
        if (!MascotArt.IsMood(mood)) return "Nieznany nastrój.";
        var key = AssetKey(agent, mood);
        if (!AllowedTypes.Contains(contentType)) return "Dozwolone pliki: SVG, PNG, WebP, GIF.";
        if (data.Length is 0 or > MaxAssetBytes) return $"Plik musi mieć od 1 B do {MaxAssetBytes / 1024} KB.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var asset = await db.MascotAssets.FindAsync(key);
        if (asset is null) db.MascotAssets.Add(asset = new MascotAsset { Mood = key });
        asset.ContentType = contentType;
        asset.Data = data;
        asset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Zapisuje PNG wygenerowany w przeglądarce z wgranego SVG (do e-maili).</summary>
    public async Task SavePngAsync(string mood, byte[] png, string agent = Hantly)
    {
        if (png.Length is 0 or > MaxAssetBytes || png.Length < 8 || png[0] != 0x89 || png[1] != 0x50) return;
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.MascotAssets.FindAsync(AssetKey(agent, mood)) is { } asset)
        {
            asset.PngData = png;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Obrazek nastroju do e-maili: własny plik rastrowy, PNG zrobiony z własnego SVG albo wbudowany PNG
    /// (wwwroot/mascot i wwwroot/leo — wygenerowane z MascotArt i PuppyArt; po zmianie grafik trzeba je odświeżyć).
    /// </summary>
    public async Task<(byte[] Data, string ContentType)?> EmailImageAsync(string mood, string webRoot, string agent = Hantly)
    {
        if (!MascotArt.IsMood(mood)) return null;
        var key = AssetKey(agent, mood);
        await using var db = await dbFactory.CreateDbContextAsync();
        var asset = await db.MascotAssets.AsNoTracking().FirstOrDefaultAsync(a => a.Mood == key);
        if (asset is not null && asset.ContentType != "image/svg+xml") return (asset.Data, asset.ContentType);
        if (asset?.PngData is { Length: > 0 } png) return (png, "image/png");
        var file = Path.Combine(webRoot, agent == Leo ? "leo" : "mascot", mood + ".png");
        return File.Exists(file) ? (await File.ReadAllBytesAsync(file), "image/png") : null;
    }

    /// <summary>Wersja ikon do adresów obrazków w e-mailach (zmienia się po każdej podmianie ikony).</summary>
    public async Task<string> IconsVersionAsync(string agent = Hantly)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var last = await db.MascotAssets.Where(a => a.Mood.StartsWith(LeoPrefix) == (agent == Leo)).MaxAsync(a => (DateTime?)a.UpdatedAt);
        return last is { } t ? t.Ticks.ToString("x") : "0";
    }

    public async Task ResetAssetAsync(string mood, string agent = Hantly)
    {
        var key = AssetKey(agent, mood);
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.MascotAssets.Where(a => a.Mood == key).ExecuteDeleteAsync();
    }

    public sealed record Phrase(string Text, bool FromAi, string? Error);

    /// <summary>Dłuższe podsumowania (kilka zdań zamiast dymka) — rodzaj → limit znaków.</summary>
    public static readonly IReadOnlyDictionary<string, int> LongKinds = new Dictionary<string, int>
    {
        ["co_sie_zmienilo"] = 440,
        ["w_skrocie"] = 420,
        ["twoj_miesiac"] = 420,
        ["twoj_rok"] = 460,
    };

    /// <summary>
    /// Ubiera podpowiedź w słowa asystenta. Zawsze zwraca tekst: przy wyłączonym AI, błędzie albo
    /// podejrzanej odpowiedzi (liczby spoza faktów, zgubiony znacznik [klient]) — tekst zapasowy z aplikacji.
    /// </summary>
    /// <param name="audience">„trener” — głos maskotki (masterprompt); „klient” — podsumowanie dla klienta trenera;
    /// „admin” — agent Portalu (Leo) dla administratora platformy.</param>
    /// <summary>Czy dla tego trenera „Zaproponuj plan” może korzystać z AI: włącznik generatora, AI i klucz.</summary>
    public async Task<bool> PlanAiAvailableAsync(Tenant? tenant) =>
        tenant?.AiOff != true && await settings.GetAsync(K.AiPlanGenerator) == "true" && await ai.UnavailableReasonAsync() is null;

    /// <summary>
    /// Plan treningowy dopracowany przez AI. Wejście z aplikacji trenera (bez danych osobowych): fakty, szkic z reguł,
    /// historia ćwiczeń i lista dozwolonych ćwiczeń. Odpowiedź to JSON — aplikacja go sprawdza (dozwolone id, zakresy).
    /// </summary>
    public async Task<Phrase> PlanAsync(Tenant tenant, string payloadJson)
    {
        if (!await PlanAiAvailableAsync(tenant)) return new("", false, "Generator planów z AI jest wyłączony.");
        const string system = """
            Jesteś doświadczonym trenerem przygotowania motorycznego. Dopracuj szkic planu treningowego dla podopiecznego trenera personalnego.
            Zasady:
            - Używaj WYŁĄCZNIE ćwiczeń z listy "exercises" (pole id). Nie wymyślaj nowych.
            - Szanuj liczbę dni w tygodniu i czas treningu z "facts". Omijaj obciążanie obszarów z "urazy_obszary".
            - "surveys" to ankiety po treningach (skale 1–10, komentarze podopiecznego) i komentarze do treningów (trener ↔ podopieczny): wysokie RPE lub zakwasy, niska energia
              albo słaby sen → lżej i mniej objętości; ból w danym miejscu → omiń to miejsce; prośby z komentarzy uwzględnij, gdy są rozsądne.
            - Zacznij od ćwiczeń wielostawowych, potem akcesoria. Seria: 1–6, przerwa w sekundach 30–240.
            - Ciężar (weightKg) podawaj tylko dla ćwiczeń z "history" — w okolicy ostatniego wyniku, z drobną progresją, gdy RPE było ≤ 7;
              dla pozostałych zostaw null (trener dobierze na treningu).
            - Pisz po polsku, formami neutralnymi płciowo (bez „zrobiłeś/zrobiłaś”). Bez porad medycznych i obietnic efektów.
            Odpowiedz WYŁĄCZNIE poprawnym JSON-em, bez komentarzy, w formacie:
            {"name":"…","purpose":"2–3 zdania dla podopiecznego, po co ten plan","rationale":"2–4 zdania dla trenera, co zmieniono względem szkicu i dlaczego","days":[{"label":"…","exercises":[{"id":123,"sets":3,"reps":"8-10","weightKg":60,"restSeconds":90,"note":"krótka wskazówka techniczna lub null"}]}]}
            """;
        var payload = payloadJson.Length > 24_000 ? payloadJson[..24_000] : payloadJson;
        var result = await ai.CompleteAsync("plan", tenant.Id, system, payload, maxTokens: 1800);
        return result.Text is { Length: > 0 } text ? new(text, true, null) : new("", false, result.Error ?? "AI nie odpowiedziało.");
    }

    public async Task<Phrase> PhraseAsync(Tenant? tenant, string kind, Dictionary<string, string> facts, string fallback,
        bool ignoreSwitch = false, string? promptOverride = null, string? nameOverride = null, string audience = "trener")
    {
        var forClient = audience == "klient";
        var forAdmin = audience == "admin";
        fallback = fallback.Trim();
        if (!ignoreSwitch && (tenant?.AiOff == true || await settings.GetAsync(K.AiEnabled) != "true"))
            return new(fallback, false, null);

        var name = nameOverride ?? (forAdmin
            ? (await settings.GetAsync(K.LeoName) is { Length: > 0 } ln ? ln.Trim() : DefaultLeoName)
            : (await settings.GetAsync(K.AssistantName) is { Length: > 0 } n ? n.Trim() : DefaultName));
        var system = (promptOverride ?? (forClient ? await GetClientPromptAsync() : forAdmin ? await GetLeoPromptAsync() : await GetPromptAsync())).Replace("{NAZWA}", name)
            + AlwaysRules;
        var factsJson = JsonSerializer.Serialize(facts, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var longLimit = LongKinds.TryGetValue(kind, out var l) ? l : (int?)null;
        var user = $"""
            Rodzaj podpowiedzi: {kind}
            Fakty: {factsJson}
            Wersja podstawowa (możesz ją przeredagować): {fallback}
            {(longLimit is { } lim ? $"To dłuższe podsumowanie, nie dymek: napisz 3–4 krótkie zdania (do {lim - 20} znaków), zacznij od najważniejszej rzeczy i zakończ {(forClient ? "krótką zachętą" : "jedną konkretną radą")}." : "")}
            Napisz jedną wersję tego komunikatu.
            """;
        var result = await ai.CompleteAsync(ignoreSwitch ? "test" : forClient ? "postepy-klienta" : forAdmin ? "leo" : "asystent", tenant?.Id, system, user,
            maxTokens: longLimit is not null ? 480 : forClient ? 320 : 220, ignoreSwitch: ignoreSwitch);
        if (result.Text is null) return new(fallback, false, result.Error);

        var text = Clean(result.Text, longLimit ?? (forClient ? 340 : 240));
        if (!IsFaithful(text, factsJson + " " + fallback)) return new(fallback, false, "Odpowiedź AI miała liczby albo znaczniki spoza faktów — użyto tekstu podstawowego.");
        return new(text, true, null);
    }

    /// <summary>Wnioski „Wygeneruj” (ankieta, spotkania, opinie). <paramref name="Unverified"/> — liczby z tekstu AI, których nie ma w danych.</summary>
    public sealed record Insight(string Text, bool FromAi, string? Error, List<string> Unverified, DateTime At);

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Analiza dla administratora na żądanie (przycisk „Wygeneruj”): kod liczy zestawienia, AI pisze wnioski w punktach.
    /// Liczby spoza danych nie odrzucają tekstu (to dłuższa analiza, nie dymek) — wracają w <see cref="Insight.Unverified"/> do sprawdzenia.
    /// Bez AI zwraca wnioski z reguł (<paramref name="fallback"/>).
    /// </summary>
    public async Task<Insight> AnalyzeAsync(string kind, string task, object data, string fallback)
    {
        var json = JsonSerializer.Serialize(data, Relaxed);
        var name = await settings.GetAsync(K.LeoName) is { Length: > 0 } ln ? ln.Trim() : DefaultLeoName;
        var system = (await GetLeoPromptAsync()).Replace("{NAZWA}", name) + """


            Teraz piszesz dłuższą analizę na prośbę administratora (zasada o 2 zdaniach tu nie obowiązuje):
            - 3–6 punktów, każdy w osobnej linii zaczynającej się od „- ”, każdy najwyżej 2 zdania.
            - Na końcu linia „Co zrobić:” i 1–3 konkretne działania, każde w osobnej linii od „- ”.
            - Tylko wnioski wynikające z danych; liczby i procenty przepisuj z danych (są już policzone).
            - Gdy danych jest za mało na pewne wnioski, napisz to wprost w pierwszym punkcie.
            - Bez psich wstawek, bez emoji, bez nagłówków markdown i pogrubień.
            """;
        var user = $"""
            Zadanie: {task}
            Dane (JSON): {json}
            """;
        var result = await ai.CompleteAsync("leo-" + kind, null, system, user, maxTokens: 900);
        if (result.Text is null) return new(fallback, false, result.Error, [], DateTime.UtcNow);

        var text = string.Join('\n', result.Text.Replace("\r", "").Split('\n')
            .Select(l => l.Trim().Replace("**", "")).Where(l => l.Length > 0 && !l.StartsWith('#')));
        if (text.Length > 3000) text = text[..3000] + "…";
        return new(text, true, null, UnverifiedNumbers(text, json), DateTime.UtcNow);
    }

    /// <summary>Liczby z tekstu AI, których nie ma w danych źródłowych (do sprawdzenia przez administratora).</summary>
    public static List<string> UnverifiedNumbers(string text, string source)
    {
        var allowed = Numbers().Matches(source).Select(m => Normalize(m.Value)).ToHashSet();
        return Numbers().Matches(text).Select(m => m.Value).Where(v => !allowed.Contains(Normalize(v))).Distinct().Take(10).ToList();
    }

    /// <summary>Szkic odpowiedzi e-mail do trenera (zgłoszenie, opinia, ryzyko odejścia) z kategorią.</summary>
    public sealed record Draft(string Category, string Subject, string Body, bool FromAi, string? Error);

    public static readonly string[] DraftCategories = ["błąd", "pytanie", "prośba o funkcję", "płatności", "konfiguracja", "pochwała", "rezygnacja", "inne"];

    /// <summary>
    /// Szkic odpowiedzi w imieniu zespołu platformy — administrator zawsze go czyta i poprawia przed wysłaniem.
    /// Bez AI: kategoria z prostych reguł i szablon <paramref name="fallbackBody"/>.
    /// </summary>
    /// <param name="message">Wiadomość trenera do kategorii z reguł; bez niej (np. kontakt z naszej inicjatywy) kategoria to „kontakt”.</param>
    public async Task<Draft> DraftReplyAsync(string kind, string situation, Dictionary<string, string> facts, string fallbackSubject, string fallbackBody,
        string? message = null)
    {
        var category = string.IsNullOrWhiteSpace(message) ? "kontakt" : Categorize(message);
        var brand = await settings.GetAsync(K.BrandName) is { Length: > 0 } b ? b.Trim() : ReferralService.PlatformName;
        fallbackBody = fallbackBody.Replace("{MARKA}", brand);
        var system = $"""
            Piszesz szkic e-maila w imieniu zespołu platformy {brand} (aplikacja dla trenerów personalnych) do trenera, który z niej korzysta.
            Administrator przeczyta i poprawi szkic przed wysłaniem.

            Zasady:
            - Po polsku, uprzejmie i konkretnie, 3–7 zdań. Zacznij od „Dzień dobry,” (bez imienia, jeśli go nie podano), zakończ podpisem „Zespół {brand}”.
            - Formy neutralne płciowo: nie zgaduj płci trenera („dziękujemy za wiadomość”, „daj znać”, nie „napisałeś/napisałaś”).
            - Korzystaj tylko z podanych faktów. Nie obiecuj terminów, zwrotów pieniędzy, rabatów ani nowych funkcji.
            - Gdy czegoś nie wiadomo, zaproponuj krótką rozmowę albo poproś o szczegóły.

            Format odpowiedzi (dokładnie tak):
            KATEGORIA: jedna z: {string.Join(", ", DraftCategories)}
            TEMAT: krótki temat e-maila
            TREŚĆ:
            treść e-maila
            """;
        var user = $"""
            Sytuacja: {situation}
            Fakty (JSON): {JsonSerializer.Serialize(facts, Relaxed)}
            """;
        var result = await ai.CompleteAsync("leo-szkic-" + kind, null, system, user, maxTokens: 600);
        if (result.Text is null) return new(category, fallbackSubject, fallbackBody, false, result.Error);

        var text = result.Text.Replace("\r", "").Trim();
        var cat = Field(text, "KATEGORIA:");
        var subject = Field(text, "TEMAT:");
        var bodyAt = text.IndexOf("TREŚĆ:", StringComparison.OrdinalIgnoreCase);
        var body = bodyAt >= 0 ? text[(bodyAt + 6)..].Trim() : "";
        if (body.Length < 20) return new(category, fallbackSubject, fallbackBody, false, "AI nie zwróciło szkicu w oczekiwanym formacie — użyto szablonu.");
        if (cat is not null && DraftCategories.FirstOrDefault(c => cat.StartsWith(c, StringComparison.OrdinalIgnoreCase)) is { } known) category = known;
        return new(category, string.IsNullOrWhiteSpace(subject) ? fallbackSubject : subject.Trim().Trim('"', '„', '”'), body, true, null);

        static string? Field(string text, string label) =>
            text.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith(label, StringComparison.OrdinalIgnoreCase)) is { } line
                ? line.Trim()[label.Length..].Trim() : null;
    }

    /// <summary>Styl trenera do szkiców wiadomości (z aplikacji trenera).</summary>
    public sealed record TrainerStyleInfo(string Address, string Emoji, string Length, string? Greeting, string? Signature, string? Notes);

    /// <summary>
    /// Szkic wiadomości trenera do klienta w stylu trenera: profil (forma, emoji, długość, powitanie, podpis)
    /// i przykłady jego wiadomości (z poprawek szkiców). Imię klienta to znacznik [klient] — podmienia je aplikacja.
    /// Zabezpieczenia jak w dymku: liczby tylko z faktów, znacznik [klient] musi zostać; inaczej tekst zapasowy.
    /// </summary>
    public async Task<Phrase> DraftInStyleAsync(Tenant tenant, string purpose, Dictionary<string, string> facts, string fallback,
        TrainerStyleInfo style, IReadOnlyList<string> examples)
    {
        fallback = fallback.Trim();
        if (tenant.AiOff || await settings.GetAsync(K.AiEnabled) != "true") return new(fallback, false, null);

        var address = style.Address == "pan"
            ? "Forma grzecznościowa bez zgadywania płci: „Dzień dobry”, formy bezosobowe („zapraszam”, „czy pasuje termin…”), bez „Pan/Pani” i bez form rodzajowych."
            : "Pisz na Ty, w formach neutralnych płciowo („masz”, „udało się”, „daj znać”), bez „zrobiłeś/zrobiłaś”.";
        var emoji = style.Emoji switch { "brak" => "Bez emoji.", "często" => "Możesz użyć 1–2 emoji.", _ => "Najwyżej jedno emoji." };
        var length = style.Length == "srednio" ? "3–4 zdania (do 450 znaków)." : "1–2 zdania (do 260 znaków).";
        var exampleBlock = examples.Count == 0 ? "" :
            "\n\nPrzykłady wiadomości tego trenera (naśladuj ton, słownictwo i długość, nie kopiuj treści):\n"
            + string.Join("\n", examples.Take(5).Select(e => "- " + (e.Length > 400 ? e[..400] : e).Replace("\n", " ")));
        var system = $"""
            Piszesz krótką wiadomość od trenera personalnego do jego klienta (czat albo SMS), tak jakby pisał ją sam trener.

            Styl trenera:
            - {address}
            - {emoji}
            - Długość: {length}
            - Powitanie: {(string.IsNullOrWhiteSpace(style.Greeting) ? "dowolne, naturalne" : $"„{style.Greeting}”")}.
            - Podpis: {(string.IsNullOrWhiteSpace(style.Signature) ? "bez podpisu" : $"zakończ podpisem „{style.Signature}”")}.
            {(string.IsNullOrWhiteSpace(style.Notes) ? "" : $"- Opis stylu od trenera: {style.Notes}")}

            Zasady:
            - Po polsku. Nie zgaduj płci klienta.
            - Imię klienta to znacznik [klient] — użyj go dokładnie w tej postaci (bez odmiany), najlepiej w powitaniu.
            - Korzystaj tylko z podanych faktów. Nie wymyślaj liczb, dat, cen ani obietnic.
            - Bez porad medycznych i dietetycznych. Bez presji i wyrzutów — życzliwie i konkretnie.
            - Zwróć tylko treść wiadomości.{exampleBlock}
            """;
        var factsJson = JsonSerializer.Serialize(facts, Relaxed);
        var user = $"""
            Cel wiadomości: {purpose}
            Fakty: {factsJson}
            Wersja podstawowa (przeredaguj ją w stylu trenera): {fallback}
            """;
        var result = await ai.CompleteAsync("szkic-wiadomosci", tenant.Id, system, user, maxTokens: 400);
        if (result.Text is null) return new(fallback, false, result.Error);
        var text = result.Text.Trim().Trim('"', '„', '”').Replace("\r", "").Trim();
        if (text.Length > 700) text = text[..700];
        if (!IsFaithful(text, factsJson + " " + fallback + " " + style.Signature + " " + style.Greeting))
            return new(fallback, false, "Szkic AI miał liczby albo znaczniki spoza faktów — użyto wersji podstawowej.");
        return new(text, true, null);
    }

    /// <summary>Kategoria z prostych słów kluczowych — gdy AI jest wyłączone albo nie odpowie.</summary>
    public static string Categorize(string text)
    {
        var t = text.ToLowerInvariant();
        if (Has(t, "rezygn", "anulow", "wypowied", "odchodz", "zamknąć konto", "usunąć konto")) return "rezygnacja";
        if (Has(t, "błąd", "nie działa", "bug", "zawiesza", "crash", "nie mogę", "nie da się", "wywala", "error")) return "błąd";
        if (Has(t, "płatno", "faktur", "rachun", "przelew", "karta", "zapłat", " cen", "drogo", "abonament") || t.StartsWith("cen")) return "płatności";
        if (Has(t, "konfigur", "ustawi", "domena", "podłącz", "integrac", "stripe", "autopay", "payu", "przelewy24")) return "konfiguracja";
        if (Has(t, "dodajcie", "brakuje", "przydał", "fajnie gdyby", "chciał", "propozyc", "funkcj")) return "prośba o funkcję";
        if (Has(t, "jak ", "czy ", "gdzie", "?")) return "pytanie";
        if (Has(t, "super", "świetn", "dziękuj", "polecam", "genialn", "dobra robota", "zadowolon")) return "pochwała";
        return "inne";

        static bool Has(string t, params string[] words) => words.Any(t.Contains);
    }

    /// <summary>Usuwa cudzysłowy i nadmiar, ucina do <paramref name="max"/> znaków na granicy zdania lub słowa.</summary>
    private static string Clean(string text, int max)
    {
        text = text.Trim().Trim('"', '„', '”', '\'', '«', '»').Replace("\r", "").Replace("\n", " ").Trim();
        text = MultiSpace().Replace(text, " ");
        if (text.Length <= max) return text;
        var cut = text[..max];
        var end = cut.LastIndexOfAny(['.', '!', '?']);
        return end > max / 2 ? cut[..(end + 1)] : cut[..cut.LastIndexOf(' ')] + "…";
    }

    /// <summary>
    /// Każda liczba z odpowiedzi musi wystąpić w faktach, a każdy znacznik [..] z faktów — w odpowiedzi.
    /// Odrzucamy też znaczniki wymyślone przez AI („[imię]”) i komentarze o brakach („nie podałeś imienia”).
    /// </summary>
    public static bool IsFaithful(string text, string source)
    {
        var allowed = Numbers().Matches(source).Select(m => Normalize(m.Value)).ToHashSet();
        if (Numbers().Matches(text).Any(m => !allowed.Contains(Normalize(m.Value)))) return false;
        if (AnyBracket().Matches(text).Any(m => !source.Contains(m.Value, StringComparison.Ordinal))) return false;
        if (MetaComments.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase))) return false;
        return Placeholders().Matches(source).Select(m => m.Value).Distinct().All(text.Contains);
    }

    /// <summary>Doklejane do każdego promptu (także własnego z Panelu) — chronią przed „[klient] … nie podałeś imienia”.</summary>
    private const string AlwaysRules = """


        Zawsze obowiązuje:
        - Znaczniki w nawiasach kwadratowych z faktów (np. [klient]) przepisz dokładnie, a innych nie twórz.
        - Gdy nie znasz imienia, pisz bez imienia. Nie komentuj braków w danych i nie zwracaj się do autora faktów.
        """;

    /// <summary>Uwagi AI o samych danych zamiast treści dla czytelnika.</summary>
    private static readonly string[] MetaComments =
    [
        "nie podał", "nie podano", "nie podałaś", "brak imienia", "nie znam imienia", "bez imienia", "imię klienta",
        "w faktach", "z faktów", "brak danych o", "nie mam informacji", "nie wiem, jak"
    ];

    /// <summary>„62,50” i „62.5” to ta sama liczba; zera końcowe obcinamy tylko po przecinku („10” zostaje „10”).</summary>
    private static string Normalize(string number)
    {
        var n = number.Replace(',', '.');
        return n.Contains('.') ? n.TrimEnd('0').TrimEnd('.') : n;
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")] private static partial Regex Numbers();
    [GeneratedRegex(@"\[[a-ząćęłńóśźż]+\]")] private static partial Regex Placeholders();
    [GeneratedRegex(@"\[[^\]\s]{1,30}\]")] private static partial Regex AnyBracket();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex MultiSpace();

    /// <summary>Przykładowe komunikaty Leo do testu w Panelu — te same rodzaje, które liczy LeoService.</summary>
    public static readonly (string Kind, string Label, string Mood, Dictionary<string, string> Facts, string Fallback)[] LeoSamples =
    [
        ("poranny_raport", "Wstęp porannego raportu", "hello",
            new() { ["rejestracje_wczoraj"] = "2", ["wplaty_wczoraj_zl"] = "596", ["sprawy_do_zrobienia"] = "3", ["zagrozeni_trenerzy"] = "1", ["aktywni_trenerzy"] = "24" },
            "Wczoraj: 2 rejestracje, wpłaty 596 zł. Do zrobienia: 3."),
        ("nowa_rejestracja", "Świętuje: nowa rejestracja", "celebrate",
            new() { ["trener"] = "[trener]" },
            "Nowa rejestracja: [trener]! Witamy na pokładzie 🎉"),
        ("ryzyko", "Czujny: trenerzy zagrożeni odejściem", "remind",
            new() { ["liczba_trenerow"] = "3" },
            "3 trenerów może niedługo odejść. Warto się odezwać — lista z powodami czeka."),
        ("zalegle_rachunki", "Czujny: rachunki po terminie", "remind",
            new() { ["liczba_rachunkow"] = "2", ["kwota"] = "298 zł" },
            "2 rachunki są po terminie — razem 298 zł."),
        ("awaria", "Czujny: aplikacja trenera nie odpowiada", "remind",
            new() { ["trener"] = "[trener]", ["czas"] = "25 min" },
            "Aplikacja [trener] nie odpowiada od 25 min. Zajrzyj do kontenera."),
        ("ankieta", "Myśli: nowe odpowiedzi w ankiecie", "think",
            new() { ["liczba_odpowiedzi"] = "7" },
            "7 nowych odpowiedzi w ankiecie w tym tygodniu. Kliknij „Wygeneruj”, a podsumuję wnioski."),
        ("poczta_blad", "Czujny: własna skrzynka trenera nie działa", "remind",
            new() { ["trener"] = "[trener]", ["wyslane_awaryjnie"] = "4" },
            "Własna skrzynka [trener] zgłasza błąd — maile idą awaryjnie przez naszą skrzynkę (już 4). Warto się odezwać, zanim limit się skończy."),
        ("poczta_platformy", "Myśli: kto wysyła przez naszą skrzynkę", "think",
            new() { ["trenerzy_na_naszej_skrzynce"] = "5", ["maile_dzis"] = "212", ["trener"] = "[trener]", ["najwiecej"] = "96" },
            "Przez naszą skrzynkę wysyła 5 trenerów: dziś 212 maili, najwięcej [trener] (96)."),
    ];

    /// <summary>Przykładowe podpowiedzi do testu w Panelu — te same rodzaje, które liczy aplikacja trenera.</summary>
    public static readonly (string Kind, string Label, string Mood, Dictionary<string, string> Facts, string Fallback, string Audience)[] Samples =
    [
        ("podsumowanie_klienta", "Dla klienta: podsumowanie postępów", "happy",
            new() { ["liczba_treningow"] = "10", ["od"] = "1 września 2026", ["cwiczenie_1"] = "Wyciskanie sztangi leżąc", ["cwiczenie_1_kg"] = "60 → 70 (+10)",
                ["cwiczenie_2"] = "Przysiad ze sztangą", ["cwiczenie_2_kg"] = "80 → 90 (+10)", ["masa_ciala_kg"] = "82,5 → 80,4 (−2,1)" },
            "Masz za sobą już 10 treningów (od 1 września 2026). Wyciskanie sztangi leżąc: 60 → 70 kg, Przysiad ze sztangą: 80 → 90 kg. Masa ciała −2,1 kg.", "klient"),
        ("podsumowanie_trenera", "Dla trenera: wnioski o kliencie", "think",
            new() { ["liczba_treningow"] = "10", ["cwiczenie_1"] = "Wyciskanie sztangi leżąc", ["cwiczenie_1_kg"] = "60 → 70 (+10)", ["uwagi"] = "2" },
            "Za klientem 10 treningów. Największy postęp: Wyciskanie sztangi leżąc +10 kg. 2 ćwiczenia wymagają uwagi — szczegóły poniżej.", "trener"),
        ("twoj_miesiac", "Dla klienta: „Twój miesiąc”", "celebrate",
            new() { ["okres"] = "październik 2026", ["treningi"] = "9", ["treningi_poprzednio"] = "7", ["roznica"] = "+2", ["tygodnie_z_rzedu"] = "6",
                ["rekordy"] = "2", ["najlepszy_rekord"] = "Przysiad ze sztangą: 80 kg", ["medale"] = "10 treningów", ["liczba_medali"] = "1" },
            "W październiku 9 treningów — o 2 więcej niż we wrześniu. To już 6 tygodni z rzędu z treningiem. Nowy rekord: Przysiad ze sztangą — 80 kg. Nowy medal: 10 treningów. Tak trzymać!", "klient"),
        ("w_skrocie", "Dla trenera: „W skrócie” o kliencie", "think",
            new() { ["treningi_30_dni"] = "6", ["najwiekszy_postep"] = "Wyciskanie sztangi leżąc +10 kg", ["samopoczucie"] = "spada", ["treningi_w_pakiecie"] = "2",
                ["rada"] = "Zapytaj o sen i regenerację — może przyda się lżejszy tydzień." },
            "Ciężary rosną — największy postęp: Wyciskanie sztangi leżąc +10 kg. Samopoczucie z ankiet po treningu spada. W pakiecie zostały 2 treningi. Zapytaj o sen i regenerację — może przyda się lżejszy tydzień.", "trener"),
        ("co_sie_zmienilo", "Dla trenera: „Co się zmieniło” w biznesie", "think",
            new() { ["okres_dni"] = "90", ["retencja_proc"] = "80", ["retencja_zmiana_pp"] = "−20", ["przestalo_przychodzic"] = "1", ["przychod"] = "11 420 zł",
                ["przychod_zmiana_proc"] = "+45%", ["przyczyna"] = "dzięki większym zakupom na klienta", ["oblozenie_proc"] = "13", ["najluzniejsze_dni"] = "w czwartki, w piątki" },
            "Retencja spadła o 20 pp, do 80% — 1 osoba przestała przychodzić. Przychód wzrósł o 45% (do 11 420 zł) — dzięki większym zakupom na klienta. Obłożenie grafiku 13% — najwięcej wolnego w czwartki i w piątki. Te godziny można sprzedać.", "trener"),
        ("rekord_tygodnia", "Pochwała: rekordowy tydzień", "celebrate",
            new() { ["treningi_w_tym_tygodniu"] = "18", ["poprzedni_rekord"] = "15" },
            "Rekordowy tydzień: 18 treningów! Tak trzymać 💪", "trener"),
        ("kamien_milowy", "Pochwała: 10. trening klienta", "celebrate",
            new() { ["klient"] = "[klient]", ["liczba_treningow"] = "10" },
            "[klient] ma za sobą już 10 treningów! 🎉", "trener"),
        ("niezaplacone", "Przypomnienie: płatności", "remind",
            new() { ["liczba_treningow"] = "3" },
            "3 treningi poza pakietem czekają na zapłatę. Warto przypomnieć klientom.", "trener"),
        ("faktury", "Przypomnienie: faktury na koniec miesiąca", "remind",
            new() { ["miesiac"] = "październik" },
            "Koniec miesiąca! Sprawdź, czy faktury za październik są wystawione.", "trener"),
        ("ryzyko_odejscia", "Sugestia: klienci, którzy mogą odejść", "think",
            new() { ["liczba_klientow"] = "3", ["wysokie_ryzyko"] = "1" },
            "3 klientów może odejść (w tym 1 z wysokim ryzykiem). Krótka wiadomość często wystarczy — przygotuję szkic w Twoim stylu.", "trener"),
        ("nieaktywni", "Sugestia: klienci, którzy dawno nie byli", "think",
            new() { ["liczba_klientow"] = "4", ["dni"] = "21" },
            "4 klientów nie było od 3 tygodni. Może krótka wiadomość?", "trener"),
        ("dzis", "Powitanie: plan na dziś", "hello",
            new() { ["treningi_dzis"] = "5" },
            "Dzień dobry! Dziś 5 treningów w grafiku. Powodzenia!", "trener"),
    ];
}
