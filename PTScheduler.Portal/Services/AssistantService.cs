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
    public static readonly string[] Animations = ["bounce", "float", "wave", "none"];
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
        - Bądź ciepły, z lekkim humorem, bez przesady; najwyżej jedno emoji.
        - Chwal konkretnie. Przypominaj bez wywierania presji.
        - Nie udzielaj porad medycznych, dietetycznych ani prawnych.
        - Nie dodawaj powitania ani podpisu. Zwróć tylko treść komunikatu.
        """;

    public sealed record Config(bool Enabled, string Name, string Animation, bool Ai, Dictionary<string, string> Icons);

    public async Task<Config> GetConfigAsync(Tenant? tenant = null)
    {
        var s = await settings.GetAllAsync(K.AssistantEnabled, K.AssistantName, K.AssistantAnimation, K.AiEnabled);
        await using var db = await dbFactory.CreateDbContextAsync();
        var custom = await db.MascotAssets.AsNoTracking().ToDictionaryAsync(a => a.Mood);
        var icons = MascotArt.Moods.ToDictionary(m => m.Key, m => custom.TryGetValue(m.Key, out var a)
            ? $"data:{a.ContentType};base64,{Convert.ToBase64String(a.Data)}"
            : MascotArt.DataUri(m.Key));
        var name = string.IsNullOrWhiteSpace(s[K.AssistantName]) ? DefaultName : s[K.AssistantName].Trim();
        var animation = Animations.Contains(s[K.AssistantAnimation]) ? s[K.AssistantAnimation] : "bounce";
        return new Config(
            Enabled: s[K.AssistantEnabled] != "false" && tenant?.AssistantOff != true,
            Name: name,
            Animation: animation,
            Ai: s[K.AiEnabled] == "true" && tenant?.AiOff != true,
            Icons: icons);
    }

    public async Task<string> GetPromptAsync() =>
        await settings.GetAsync(K.AssistantPrompt) is { Length: > 0 } p ? p : DefaultPrompt;

    public async Task<string?> SaveAssetAsync(string mood, string contentType, byte[] data)
    {
        if (!MascotArt.IsMood(mood)) return "Nieznany nastrój.";
        if (!AllowedTypes.Contains(contentType)) return "Dozwolone pliki: SVG, PNG, WebP, GIF.";
        if (data.Length is 0 or > MaxAssetBytes) return $"Plik musi mieć od 1 B do {MaxAssetBytes / 1024} KB.";
        await using var db = await dbFactory.CreateDbContextAsync();
        var asset = await db.MascotAssets.FindAsync(mood);
        if (asset is null) db.MascotAssets.Add(asset = new MascotAsset { Mood = mood });
        asset.ContentType = contentType;
        asset.Data = data;
        asset.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    public async Task ResetAssetAsync(string mood)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.MascotAssets.Where(a => a.Mood == mood).ExecuteDeleteAsync();
    }

    public sealed record Phrase(string Text, bool FromAi, string? Error);

    /// <summary>
    /// Ubiera podpowiedź w słowa asystenta. Zawsze zwraca tekst: przy wyłączonym AI, błędzie albo
    /// podejrzanej odpowiedzi (liczby spoza faktów, zgubiony znacznik [klient]) — tekst zapasowy z aplikacji.
    /// </summary>
    public async Task<Phrase> PhraseAsync(Tenant? tenant, string kind, Dictionary<string, string> facts, string fallback,
        bool ignoreSwitch = false, string? promptOverride = null, string? nameOverride = null)
    {
        fallback = fallback.Trim();
        if (!ignoreSwitch && (tenant?.AiOff == true || await settings.GetAsync(K.AiEnabled) != "true"))
            return new(fallback, false, null);

        var name = nameOverride ?? (await settings.GetAsync(K.AssistantName) is { Length: > 0 } n ? n.Trim() : DefaultName);
        var system = (promptOverride ?? await GetPromptAsync()).Replace("{NAZWA}", name);
        var factsJson = JsonSerializer.Serialize(facts, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var user = $"""
            Rodzaj podpowiedzi: {kind}
            Fakty: {factsJson}
            Wersja podstawowa (możesz ją przeredagować): {fallback}
            Napisz jedną wersję tego komunikatu.
            """;
        var result = await ai.CompleteAsync(ignoreSwitch ? "test" : "asystent", tenant?.Id, system, user, ignoreSwitch: ignoreSwitch);
        if (result.Text is null) return new(fallback, false, result.Error);

        var text = Clean(result.Text);
        if (!IsFaithful(text, factsJson + " " + fallback)) return new(fallback, false, "Odpowiedź AI miała liczby albo znaczniki spoza faktów — użyto tekstu podstawowego.");
        return new(text, true, null);
    }

    /// <summary>Usuwa cudzysłowy i nadmiar, ucina do 240 znaków na granicy zdania lub słowa.</summary>
    private static string Clean(string text)
    {
        text = text.Trim().Trim('"', '„', '”', '\'', '«', '»').Replace("\r", "").Replace("\n", " ").Trim();
        text = MultiSpace().Replace(text, " ");
        if (text.Length <= 240) return text;
        var cut = text[..240];
        var end = cut.LastIndexOfAny(['.', '!', '?']);
        return end > 120 ? cut[..(end + 1)] : cut[..cut.LastIndexOf(' ')] + "…";
    }

    /// <summary>Każda liczba z odpowiedzi musi wystąpić w faktach, a każdy znacznik [..] z faktów — w odpowiedzi.</summary>
    private static bool IsFaithful(string text, string source)
    {
        var allowed = Numbers().Matches(source).Select(m => Normalize(m.Value)).ToHashSet();
        if (Numbers().Matches(text).Any(m => !allowed.Contains(Normalize(m.Value)))) return false;
        return Placeholders().Matches(source).Select(m => m.Value).Distinct().All(text.Contains);
    }

    /// <summary>„62,50” i „62.5” to ta sama liczba; zera końcowe obcinamy tylko po przecinku („10” zostaje „10”).</summary>
    private static string Normalize(string number)
    {
        var n = number.Replace(',', '.');
        return n.Contains('.') ? n.TrimEnd('0').TrimEnd('.') : n;
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")] private static partial Regex Numbers();
    [GeneratedRegex(@"\[[a-ząćęłńóśźż]+\]")] private static partial Regex Placeholders();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex MultiSpace();

    /// <summary>Przykładowe podpowiedzi do testu w Panelu — te same rodzaje, które liczy aplikacja trenera.</summary>
    public static readonly (string Kind, string Label, string Mood, Dictionary<string, string> Facts, string Fallback)[] Samples =
    [
        ("rekord_tygodnia", "Pochwała: rekordowy tydzień", "celebrate",
            new() { ["treningi_w_tym_tygodniu"] = "18", ["poprzedni_rekord"] = "15" },
            "Rekordowy tydzień: 18 treningów! Tak trzymać 💪"),
        ("kamien_milowy", "Pochwała: 10. trening klienta", "celebrate",
            new() { ["klient"] = "[klient]", ["liczba_treningow"] = "10" },
            "[klient] ma za sobą już 10 treningów! 🎉"),
        ("niezaplacone", "Przypomnienie: płatności", "remind",
            new() { ["liczba_treningow"] = "3" },
            "3 treningi poza pakietem czekają na zapłatę. Warto przypomnieć klientom."),
        ("faktury", "Przypomnienie: faktury na koniec miesiąca", "remind",
            new() { ["miesiac"] = "październik" },
            "Koniec miesiąca! Sprawdź, czy faktury za październik są wystawione."),
        ("nieaktywni", "Sugestia: klienci, którzy dawno nie byli", "think",
            new() { ["liczba_klientow"] = "4", ["dni"] = "21" },
            "4 klientów nie było od 3 tygodni. Może krótka wiadomość?"),
        ("dzis", "Powitanie: plan na dziś", "hello",
            new() { ["treningi_dzis"] = "5" },
            "Dzień dobry! Dziś 5 treningów w grafiku. Powodzenia!"),
    ];
}
