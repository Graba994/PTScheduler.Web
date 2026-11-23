using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Klient renderera Studia reklam (kontener hantly-renderer, tylko sieć wewnętrzna). API opisane w studio/server/server.mjs.
/// Klucze głosu idą w każdym żądaniu — renderer ich nie przechowuje. Render trwa minuty, więc długie limity czasu.
/// </summary>
public class RendererClient(IHttpClientFactory httpFactory, IConfiguration config)
{
    public const string HttpClientName = "studio-renderer";

    public string BaseUrl => (config["Studio:RendererUrl"] is { Length: > 0 } u ? u : "http://hantly-renderer:3000").TrimEnd('/');

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record Health(bool Ok, string? Rendering, int Progress, string? Busy, int Queued, int Voicing, string? Commit);

    /// <summary>Stan renderera; null = nie odpowiada.</summary>
    public async Task<Health?> HealthAsync(CancellationToken ct = default)
    {
        try { return await Http(TimeSpan.FromSeconds(5)).GetFromJsonAsync<Health>($"{BaseUrl}/health", Json, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    public sealed record TtsScene(int Index, string Text, bool Regenerate);
    public sealed record TtsResult(List<TtsSceneResult> Scenes);
    public sealed record TtsSceneResult(int DurationMs, int Words);

    /// <summary>Głos scen → scene-N.mp3 + timeline.json w katalogu filmu.</summary>
    public Task<TtsResult> TtsAsync(string slug, object voice, StudioMedia.Credentials credentials, IEnumerable<TtsScene> scenes, CancellationToken ct) =>
        PostAsync<TtsResult>("/tts", new { slug, voice, credentials, scenes }, TimeSpan.FromMinutes(10), ct);

    /// <summary>Próbka głosu (MP3) do odsłuchu w panelu.</summary>
    public async Task<byte[]> PreviewVoiceAsync(object voice, StudioMedia.Credentials credentials, string text, CancellationToken ct = default)
    {
        using var resp = await Http(TimeSpan.FromSeconds(60)).PostAsJsonAsync($"{BaseUrl}/preview-voice", new { voice, credentials, text }, Json, ct);
        if (!resp.IsSuccessStatusCode) throw new RendererException(await ErrorOf(resp, ct));
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    public sealed record RenderResult(double DurationSeconds, double RenderSeconds);

    /// <summary>episode.json + timeline.json → video.mp4 (h264, 1080×1920). Czeka w kolejce renderera.</summary>
    public Task<RenderResult> RenderAsync(string slug, CancellationToken ct) =>
        PostAsync<RenderResult>("/render", new { slug }, TimeSpan.FromMinutes(45), ct);

    public sealed record Still(int Scene, double Ratio, int Frame, string Jpeg);
    public sealed record StillsResult(List<Still> Stills);

    /// <summary>Po 2 klatki z każdej sceny (35% i 80%, JPEG pół rozdzielczości) — do kontroli przez AI.</summary>
    public Task<StillsResult> StillsAsync(string slug, CancellationToken ct) =>
        PostAsync<StillsResult>("/stills", new { slug }, TimeSpan.FromMinutes(20), ct);

    private sealed record PngResult(string Png);

    /// <summary>Grafika posta 4:5 (PNG). props.screenImage = "sources/hantly/screens/x.png" dla prawdziwego zrzutu.</summary>
    public async Task<byte[]> PostImageAsync(object props, CancellationToken ct) =>
        Convert.FromBase64String((await PostAsync<PngResult>("/post", new { props }, TimeSpan.FromMinutes(5), ct)).Png);

    public sealed record ScreenPage(string Path, string? As);
    public sealed record Shot(string File, string Path, string Pattern, string As, int W, int H, string Title, string Text, string Thumb);
    public sealed record Skipped(string Path, string Reason);
    public sealed record ScreensResult(List<Shot> Shots, List<Skipped> Skipped);

    /// <summary>Zrzuty aplikacji: wejście jednorazowymi linkami (trener / klient) na konto demo.</summary>
    public Task<ScreensResult> ScreensAsync(string dir, string? trainerUrl, string? clientUrl, IEnumerable<string> hideEmails,
        IEnumerable<ScreenPage> pages, CancellationToken ct) =>
        PostAsync<ScreensResult>("/screens", new
        {
            dir, mode = "sso",
            entries = new { trainer = trainerUrl, client = clientUrl },
            hideEmails, pages,
        }, TimeSpan.FromMinutes(20), ct);

    // ── HTTP ──────────────────────────────────────────────────────────

    private HttpClient Http(TimeSpan timeout)
    {
        var h = httpFactory.CreateClient(HttpClientName);
        h.Timeout = timeout;
        return h;
    }

    private async Task<T> PostAsync<T>(string path, object body, TimeSpan timeout, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await Http(timeout).PostAsJsonAsync(BaseUrl + path, body, Json, ct); }
        catch (HttpRequestException ex) { throw new RendererException($"Renderer nie odpowiada ({BaseUrl}): {ex.Message}. Sprawdź Panel → Aktualizacje → Renderer filmów."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new RendererException($"Renderer nie skończył w {timeout.TotalMinutes:0} min."); }
        using (resp)
        {
            if (!resp.IsSuccessStatusCode) throw new RendererException(await ErrorOf(resp, ct));
            return await resp.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new RendererException("Renderer zwrócił pustą odpowiedź.");
        }
    }

    private sealed record ErrorBody([property: JsonPropertyName("error")] string? Error);

    private static async Task<string> ErrorOf(HttpResponseMessage resp, CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct);
        try { if (JsonSerializer.Deserialize<ErrorBody>(text)?.Error is { Length: > 0 } e) return e; }
        catch (JsonException) { /* nie-JSON */ }
        return $"Renderer odpowiedział HTTP {(int)resp.StatusCode}.";
    }
}

/// <summary>Błąd renderera z czytelnym komunikatem dla właściciela.</summary>
public class RendererException(string message) : Exception(message);
