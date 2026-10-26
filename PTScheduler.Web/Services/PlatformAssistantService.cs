using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace PTScheduler.Web.Services;

/// <summary>
/// Asystent-maskotka z Portalu: wygląd (imię, ikony nastrojów, animacja) i teksty podpowiedzi ubrane
/// w słowa przez AI. Wygląd trzymamy 30 minut, a gotowy tekst danej podpowiedzi — do końca dnia,
/// więc AI pisze go raz, nie przy każdym wejściu. Gdy Portal milczy, zostaje tekst zapasowy z reguł.
/// </summary>
public class PlatformAssistantService(IHttpClientFactory httpFactory, ILogger<PlatformAssistantService> logger)
{
    /// <param name="EmailIcons">Adresy PNG nastrojów w Portalu — do e-maili (data URI i SVG nie działają w Gmailu).</param>
    /// <param name="Layout">Układ widżetu: bubble, card, pill, peek.</param>
    /// <param name="AnimatedSvg">Animowany hantelek z Portalu (gdy animacja „live” i wbudowane ikony), inaczej null.</param>
    /// <param name="PlanAi">Generator planów z AI — włącza administrator platformy (Portal → Asystent i AI).</param>
    /// <param name="Motion">Ruchliwość animowanego hantelka z Portalu: calm, normal, lively.</param>
    /// <param name="Effects">Włączone efekty ruchu po przecinku; null (starszy Portal) = wszystkie.</param>
    public sealed record Config(bool Enabled, string Name, string Animation, bool Ai, Dictionary<string, string> Icons,
        Dictionary<string, string>? EmailIcons = null, string Layout = "bubble", string? AnimatedSvg = null, bool PlanAi = false,
        string Motion = "normal", string? Effects = null)
    {
        public const string AllEffects = "enter,look,tricks,hover,talk,doze,party";
        public string EffectList => Effects ?? AllEffects;
        public bool Has(string effect) => EffectList.Split(',').Contains(effect);
    }

    /// <summary>Maskotka w treści strony (karta „Na dziś”, kreator /witaj): oczy za kursorem, sztuczki, najechanie.</summary>
    public static string PageFx(Config c) => string.Join(",", c.EffectList.Split(',').Where(e => e is "look" or "tricks" or "hover"));

    private static readonly TimeSpan ConfigTtl = TimeSpan.FromMinutes(30);
    private Config? _config;
    private DateTime _configAt;
    private readonly ConcurrentDictionary<string, (string Text, DateOnly Day)> _phrases = new();

    public async Task<Config?> GetConfigAsync()
    {
        if (!PlatformInfoService.Configured) return null;
        if (_config is not null && DateTime.UtcNow - _configAt < ConfigTtl) return _config;
        try
        {
            using var req = Request(HttpMethod.Get, "assistant");
            using var resp = await Client(8).SendAsync(req);
            if (resp.IsSuccessStatusCode && await resp.Content.ReadFromJsonAsync<Config>() is { } cfg)
            {
                _config = cfg;
                _configAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "Ustawienia asystenta z Portalu niedostępne.");
        }
        return _config;
    }

    /// <summary>Tekst podpowiedzi: z pamięci (ten sam dzień), od AI przez Portal albo zapasowy.</summary>
    /// <param name="audience">„trener” — głos maskotki; „klient” — podsumowanie dla klienta trenera (bez maskotki).</param>
    public async Task<string> PhraseAsync(string key, string kind, IReadOnlyDictionary<string, string> facts, string fallback, bool ai,
        string audience = "trener")
    {
        if (!ai || !PlatformInfoService.Configured) return fallback;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var cacheKey = audience + "|" + key + "|" + string.Join(";", facts.OrderBy(f => f.Key).Select(f => f.Key + "=" + f.Value));
        if (_phrases.TryGetValue(cacheKey, out var hit) && hit.Day == today) return hit.Text;
        try
        {
            using var req = Request(HttpMethod.Post, "assistant/phrase");
            req.Content = JsonContent.Create(new { kind, facts, fallback, audience });
            using var resp = await Client(30).SendAsync(req);
            if (resp.IsSuccessStatusCode && await resp.Content.ReadFromJsonAsync<PhraseResponse>() is { Text.Length: > 0 } r
                && Domain.Rules.AiTextGuard.IsClean(r.Text, string.Join(" ", facts.Values) + " " + fallback))
            {
                if (_phrases.Count > 2000) _phrases.Clear();
                _phrases[cacheKey] = (r.Text, today);
                return r.Text;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "Tekst asystenta z Portalu niedostępny — używam zapasowego.");
        }
        return fallback;
    }

    private sealed record PhraseResponse(string Text, bool Ai);

    /// <summary>
    /// Szkic wiadomości do klienta w stylu trenera (bez pamięci podręcznej — każde kliknięcie to nowa wersja).
    /// Zwraca (tekst, czy z AI); przy braku Portalu albo AI — tekst zapasowy.
    /// </summary>
    public async Task<(string Text, bool Ai)> DraftAsync(string purpose, IReadOnlyDictionary<string, string> facts, string fallback,
        Domain.Entities.TrainerStyle style, IReadOnlyList<string> examples, bool ai)
    {
        if (!ai || !PlatformInfoService.Configured) return (fallback, false);
        try
        {
            using var req = Request(HttpMethod.Post, "assistant/draft");
            req.Content = JsonContent.Create(new
            {
                purpose, facts, fallback,
                address = style.Address, emoji = style.Emoji, length = style.Length,
                greeting = style.Greeting, signature = style.Signature, notes = style.Notes, examples
            });
            using var resp = await Client(35).SendAsync(req);
            if (resp.IsSuccessStatusCode && await resp.Content.ReadFromJsonAsync<PhraseResponse>() is { Text.Length: > 0 } r)
                return (r.Text, r.Ai);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogDebug(ex, "Szkic wiadomości z Portalu niedostępny — używam szablonu.");
        }
        return (fallback, false);
    }

    /// <summary>
    /// Plan treningowy dopracowany przez AI (Portal). Wejście: JSON z faktami bez danych osobowych, szkicem z reguł
    /// i listą dozwolonych ćwiczeń. Zwraca (tekst odpowiedzi, czy z AI) — sprawdza go <see cref="PlanGeneratorService"/>.
    /// </summary>
    public async Task<(string? Text, bool Ai)> PlanAsync(string payloadJson)
    {
        if (!PlatformInfoService.Configured) return (null, false);
        using var req = Request(HttpMethod.Post, "assistant/plan");
        req.Content = JsonContent.Create(new { payload = payloadJson });
        using var resp = await Client(90).SendAsync(req);
        if (!resp.IsSuccessStatusCode) return (null, false);
        var r = await resp.Content.ReadFromJsonAsync<PhraseResponse>();
        return (r?.Text, r?.Ai == true);
    }

    private HttpClient Client(int seconds)
    {
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(seconds);
        return http;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path)
    {
        var portal = Environment.GetEnvironmentVariable("PORTAL_URL")!.TrimEnd('/');
        var slug = Environment.GetEnvironmentVariable("TENANT_SLUG")!;
        var req = new HttpRequestMessage(method, $"{portal}/api/internal/tenants/{Uri.EscapeDataString(slug)}/{path}");
        req.Headers.Add("X-Internal-Secret", Environment.GetEnvironmentVariable("TENANT_INTERNAL_SECRET"));
        return req;
    }
}
