using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services.Studio;

/// <summary>
/// Głos lektora i dźwięk filmów Studia reklam: klucze Azure Speech i ElevenLabs (szyfrowane Data Protection, nigdy w repo
/// ani w obrazie), ustawienia głosu, lista głosów ElevenLabs i biblioteka efektów/muzyki generowanych RAZ (ElevenLabs Sound
/// Effects / Music) do {Studio:DataDir}/library. Renderer dostaje klucze w każdym żądaniu i niczego nie przechowuje.
/// </summary>
public partial class StudioMedia(
    SiteSettingsService settings,
    IDataProtectionProvider dataProtection,
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<StudioMedia> logger)
{
    private IDataProtector Secrets => dataProtection.CreateProtector("studio-voice-keys");

    // ── Klucze ────────────────────────────────────────────────────────

    public sealed record KeysState(bool Azure, string AzureRegion, bool ElevenLabs);

    public async Task<KeysState> KeysStateAsync()
    {
        var s = await settings.GetAllAsync(K.StudioAzureKey, K.StudioAzureRegion, K.StudioElevenLabsKey);
        return new(!string.IsNullOrEmpty(s[K.StudioAzureKey]), s[K.StudioAzureRegion].Trim(), !string.IsNullOrEmpty(s[K.StudioElevenLabsKey]));
    }

    /// <summary>Zapis kluczy: null = bez zmian, pusty napis = usuń.</summary>
    public async Task SaveKeysAsync(string? azureKey, string azureRegion, string? elevenLabsKey)
    {
        var values = new Dictionary<string, string> { [K.StudioAzureRegion] = azureRegion.Trim().ToLowerInvariant() };
        if (azureKey is not null) values[K.StudioAzureKey] = Protect(azureKey);
        if (elevenLabsKey is not null) values[K.StudioElevenLabsKey] = Protect(elevenLabsKey);
        await settings.SetManyAsync(values);
    }

    private string Protect(string key) => string.IsNullOrWhiteSpace(key) ? "" : Secrets.Protect(key.Trim());

    private async Task<string?> SecretAsync(string settingKey)
    {
        var raw = await settings.GetAsync(settingKey);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return Secrets.Unprotect(raw); }
        catch (CryptographicException)
        {
            logger.LogWarning("Studio: nie da się odszyfrować klucza {Key} — wpisz go ponownie w Studiu reklam.", settingKey);
            return null;
        }
    }

    /// <summary>Klucze dla renderera (pole „credentials” w /tts i /preview-voice).</summary>
    public async Task<Credentials> CredentialsAsync() => new(
        await SecretAsync(K.StudioAzureKey), (await settings.GetAsync(K.StudioAzureRegion)).Trim(), await SecretAsync(K.StudioElevenLabsKey));

    public sealed record Credentials(
        [property: JsonPropertyName("azureKey")] string? AzureKey,
        [property: JsonPropertyName("azureRegion")] string AzureRegion,
        [property: JsonPropertyName("elevenLabsKey")] string? ElevenLabsKey);

    // ── Głos lektora ──────────────────────────────────────────────────

    public sealed record Voice
    {
        /// <summary>Azure | ElevenLabs</summary>
        public string Provider { get; init; } = "Azure";
        /// <summary>Azure: np. pl-PL-MarekNeural. ElevenLabs: voice_id (ok. 20 znaków).</summary>
        public string VoiceId { get; init; } = "pl-PL-MarekNeural";
        /// <summary>Azure: „+6%”. ElevenLabs: szybkość 0.7–1.2 („1.05”), pusty = domyślna.</summary>
        public string Rate { get; init; } = "+6%";
        /// <summary>Tylko Azure: „+0Hz”.</summary>
        public string Pitch { get; init; } = "+0Hz";
        public string Model { get; init; } = "eleven_multilingual_v2";
        public double Stability { get; init; } = 0.5;
        public double Similarity { get; init; } = 0.75;
        public double Style { get; init; } = 0.15;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Voice> VoiceAsync()
    {
        var raw = await settings.GetAsync(K.StudioVoice);
        if (string.IsNullOrWhiteSpace(raw)) return new Voice();
        try { return JsonSerializer.Deserialize<Voice>(raw, Json) ?? new Voice(); }
        catch (JsonException) { return new Voice(); }
    }

    public async Task<string?> SaveVoiceAsync(Voice v)
    {
        v = Normalize(v);
        if (VoiceProblem(v) is { } problem) return problem;
        await settings.SetAsync(K.StudioVoice, JsonSerializer.Serialize(v, Json));
        return null;
    }

    public static Voice Normalize(Voice v) => v with
    {
        Provider = v.Provider.Equals("ElevenLabs", StringComparison.OrdinalIgnoreCase) ? "ElevenLabs" : "Azure",
        VoiceId = v.VoiceId.Trim(),
        Rate = v.Rate.Trim().Replace(',', '.'),
        Pitch = v.Pitch.Trim(),
        Model = string.IsNullOrWhiteSpace(v.Model) ? "eleven_multilingual_v2" : v.Model.Trim(),
        Stability = Math.Clamp(v.Stability, 0, 1),
        Similarity = Math.Clamp(v.Similarity, 0, 1),
        Style = Math.Clamp(v.Style, 0, 1),
    };

    /// <summary>Czytelny powód, czemu ustawienia głosu są złe; null = w porządku.</summary>
    public static string? VoiceProblem(Voice v)
    {
        if (v.Provider == "Azure")
        {
            if (!AzureVoice().IsMatch(v.VoiceId)) return "Głos Azure wygląda jak „pl-PL-MarekNeural” (język-KRAJ-NazwaNeural).";
            if (!AzureRate().IsMatch(v.Rate)) return "Tempo Azure podaj w procentach, np. „+6%” albo „-5%”.";
            if (!AzurePitch().IsMatch(v.Pitch)) return "Wysokość Azure podaj w hercach, np. „+0Hz” albo „-20Hz”.";
            return null;
        }
        if (!LooksLikeElevenLabsVoiceId(v.VoiceId)) return "ID głosu ElevenLabs to ok. 20 liter i cyfr (z listy głosów albo z ElevenLabs → Voices).";
        if (v.Rate.Length > 0 && !(double.TryParse(v.Rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && speed is >= 0.7 and <= 1.2))
            return "Szybkość ElevenLabs to liczba od 0.7 do 1.2 (np. 1.05) albo puste pole.";
        return null;
    }

    /// <summary>ID głosu ElevenLabs to ok. 20 znaków alfanumerycznych — na pewno nie „pl-PL-…Neural”.</summary>
    public static bool LooksLikeElevenLabsVoiceId(string id) => id.Length is >= 15 and <= 40 && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>Głos w kształcie, którego oczekuje renderer (studio/server/tts.mjs).</summary>
    public static object RendererVoice(Voice v) => new
    {
        provider = v.Provider,
        voiceId = v.VoiceId,
        rate = v.Rate,
        pitch = v.Pitch,
        model = v.Model,
        extra = new { stability = v.Stability, similarity_boost = v.Similarity, style = v.Style },
    };

    /// <summary>Czy da się nagrać głos: jest klucz dostawcy i poprawne ustawienia. Null = tak, inaczej powód.</summary>
    public async Task<string?> VoiceUnavailableAsync()
    {
        var v = await VoiceAsync();
        if (VoiceProblem(v) is { } problem) return problem;
        var c = await CredentialsAsync();
        if (v.Provider == "Azure" && (c.AzureKey is null || c.AzureRegion.Length == 0)) return "Brak klucza albo regionu Azure Speech (Studio reklam → Głos i dźwięk).";
        if (v.Provider == "ElevenLabs" && c.ElevenLabsKey is null) return "Brak klucza ElevenLabs (Studio reklam → Głos i dźwięk).";
        return null;
    }

    public sealed record ElevenLabsVoice(string Id, string Name, string Category);

    /// <summary>Głosy z konta ElevenLabs (klucz potrzebuje uprawnienia „Voices: Read”).</summary>
    public async Task<(List<ElevenLabsVoice> Voices, string? Error)> ElevenLabsVoicesAsync(CancellationToken ct = default)
    {
        if (await SecretAsync(K.StudioElevenLabsKey) is not { } key) return ([], "Najpierw zapisz klucz ElevenLabs.");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/voices");
            req.Headers.Add("xi-api-key", key);
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return ([], ElevenLabsError((int)res.StatusCode, body, "Voices: Read"));
            using var doc = JsonDocument.Parse(body);
            var list = doc.RootElement.GetProperty("voices").EnumerateArray()
                .Select(v => new ElevenLabsVoice(v.GetProperty("voice_id").GetString() ?? "", v.GetProperty("name").GetString() ?? "?",
                    v.TryGetProperty("category", out var cat) ? cat.GetString() ?? "" : ""))
                .Where(v => v.Id.Length > 0)
                .OrderBy(v => v.Category is "generated" or "cloned" or "professional" ? 0 : 1).ThenBy(v => v.Name)
                .ToList();
            return (list, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return ([], $"Nie udało się pobrać głosów: {ex.Message}");
        }
    }

    public static string ElevenLabsError(int status, string body, string permission)
    {
        if (body.Contains("api_key_id_used_as_api_key")) return "Zapisany jest identyfikator klucza ElevenLabs zamiast samego klucza (prawdziwy zaczyna się od „sk_”).";
        if (MissingPermission().Match(body) is { Success: true } m)
            return $"Klucz ElevenLabs nie ma uprawnienia „{PermissionName(m.Groups[1].Value)}”. Włącz je w ElevenLabs → Developers → API Keys → edycja klucza "
                 + "(dla Studia: Text to Speech, Voices: Read, Sound Effects, Music Generation).";
        if (status is 401 or 403 || body.Contains("permission")) return $"ElevenLabs odrzucił klucz — sprawdź, czy ma uprawnienie „{permission}”.";
        if (status == 429) return "ElevenLabs: wyczerpany limit albo za dużo zapytań naraz — sprawdź plan konta.";
        return $"ElevenLabs odpowiedział {status}.";
    }

    private static string PermissionName(string code) => code switch
    {
        "text_to_speech" => "Text to Speech",
        "voices_read" => "Voices: Read",
        "sound_generation" => "Sound Effects",
        "music_generation" => "Music Generation",
        _ => code,
    };

    [GeneratedRegex(@"missing the permission (\w+)")]
    private static partial Regex MissingPermission();

    // ── Biblioteka dźwięków ───────────────────────────────────────────

    public enum SoundKind { Effect, Music }

    public sealed record Sound(string Key, SoundKind Kind, string Name, string Prompt, double Seconds);

    /// <summary>Zestaw reklamy — klucze efektów używa kompozycja (adCues w studio/src/ad/AdShort.tsx).</summary>
    public static readonly Sound[] Library =
    [
        new("whoosh", SoundKind.Effect, "Przejście (świst)", "quick soft airy whoosh transition, modern app commercial, clean, no reverb tail", 0.8),
        new("pop", SoundKind.Effect, "Pyk (dymek)", "cute soft bubble pop, friendly UI click, clean", 0.5),
        new("ding", SoundKind.Effect, "Powiadomienie (wiadomość)", "bright friendly phone notification ding, single chime, clean", 1.0),
        new("stamp", SoundKind.Effect, "Stempel (hasło)", "punchy rubber stamp thud, short impact, slightly playful", 0.7),
        new("sparkle", SoundKind.Effect, "Iskierki (przed/po)", "magical sparkle shimmer, short, light, positive", 1.2),
        new("confetti", SoundKind.Effect, "Konfetti (finał)", "party popper burst with short light cheer, celebratory, clean", 2.0),
        new("music-upbeat", SoundKind.Music, "Muzyka: energiczna", "upbeat modern background music for a fitness app commercial, punchy drums, bright synth plucks, around 120 bpm, positive, motivating, no vocals, loopable", 60),
        new("music-groove", SoundKind.Music, "Muzyka: luźny groove", "light funky groove background music for a short app advert, clean electric guitar, bass and claps, around 105 bpm, friendly, no vocals, loopable", 60),
    ];

    public static string FileFor(string key) => key.StartsWith("music", StringComparison.Ordinal) ? $"{key}.mp3" : $"sfx-{key}.mp3";

    public string LibraryDir => Path.Combine(DataDir, "library");
    private string DataDir => config["Studio:DataDir"] is { Length: > 0 } d ? d : "/opt/ptscheduler/studio";

    /// <summary>Dźwięki, które są już w bibliotece (plik istnieje).</summary>
    public HashSet<string> AvailableSounds() =>
        Library.Where(s => File.Exists(Path.Combine(LibraryDir, FileFor(s.Key)))).Select(s => s.Key).ToHashSet();

    /// <summary>Generuje brakujące dźwięki (istniejących nie rusza). Wolne — wołaj z kolejki.</summary>
    public async Task<(int Created, List<string> Errors)> GenerateMissingSoundsAsync(CancellationToken ct)
    {
        if (await SecretAsync(K.StudioElevenLabsKey) is not { } key) return (0, ["Brak klucza ElevenLabs."]);
        var have = AvailableSounds();
        var created = 0;
        var errors = new List<string>();
        Directory.CreateDirectory(LibraryDir);
        foreach (var s in Library.Where(x => !have.Contains(x.Key)))
        {
            try
            {
                var bytes = await GenerateAsync(key, s, ct);
                var file = Path.Combine(LibraryDir, FileFor(s.Key));
                await File.WriteAllBytesAsync(file + ".tmp", bytes, ct);
                File.Move(file + ".tmp", file, overwrite: true);
                created++;
                logger.LogInformation("Studio: dźwięk {Key} w bibliotece.", s.Key);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException)
            {
                errors.Add($"{s.Name}: {ex.Message}");
            }
        }
        return (created, errors);
    }

    private async Task<byte[]> GenerateAsync(string apiKey, Sound s, CancellationToken ct)
    {
        using var req = s.Kind == SoundKind.Music
            ? new HttpRequestMessage(HttpMethod.Post, "https://api.elevenlabs.io/v1/music?output_format=mp3_44100_128")
            { Content = JsonContent.Create(new { prompt = s.Prompt, music_length_ms = (int)(s.Seconds * 1000) }) }
            : new HttpRequestMessage(HttpMethod.Post, "https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128")
            { Content = JsonContent.Create(new { text = s.Prompt, duration_seconds = s.Seconds, prompt_influence = 0.5 }) };
        req.Headers.Add("xi-api-key", apiKey);
        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        using var res = await http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return await res.Content.ReadAsByteArrayAsync(ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(ElevenLabsError((int)res.StatusCode, body, s.Kind == SoundKind.Music ? "Music Generation" : "Sound Effects"));
    }

    // ── Dźwięk w filmie ───────────────────────────────────────────────

    public sealed record SoundOptions(string Music, double MusicVolume, bool Sfx, bool Qa);

    public async Task<SoundOptions> SoundOptionsAsync()
    {
        var s = await settings.GetAllAsync(K.StudioMusic, K.StudioMusicVolume, K.StudioSfx, K.StudioQa);
        var volume = double.TryParse(s[K.StudioMusicVolume], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 0.5) : 0.09;
        return new(s[K.StudioMusic].Trim() is { Length: > 0 } m ? m : "losowo", volume, s[K.StudioSfx] != "false", s[K.StudioQa] != "false");
    }

    public Task SaveSoundOptionsAsync(SoundOptions o) => settings.SetManyAsync(new Dictionary<string, string>
    {
        [K.StudioMusic] = o.Music is "brak" or "losowo" || Library.Any(s => s.Kind == SoundKind.Music && s.Key == o.Music) ? o.Music : "losowo",
        [K.StudioMusicVolume] = Math.Clamp(o.MusicVolume, 0, 0.5).ToString("0.###", CultureInfo.InvariantCulture),
        [K.StudioSfx] = o.Sfx ? "true" : "false",
        [K.StudioQa] = o.Qa ? "true" : "false",
    });

    /// <summary>Sekcja „sound” w episode.json: muzyka w tle (wybrana albo losowa wg filmu) i dostępne efekty.</summary>
    public static object SoundSection(SoundOptions o, IReadOnlySet<string> available, int videoId)
    {
        var music = Library.Where(s => s.Kind == SoundKind.Music && available.Contains(s.Key)).Select(s => s.Key).Order().ToList();
        string? track = o.Music switch
        {
            "brak" => null,
            "losowo" => music.Count > 0 ? music[Math.Abs(videoId) % music.Count] : null,
            var key => music.Contains(key) ? key : null,
        };
        var sfx = o.Sfx ? Library.Where(s => s.Kind == SoundKind.Effect && available.Contains(s.Key)).Select(s => s.Key).ToArray() : [];
        return new { music = track is null ? null : FileFor(track), musicVolume = o.MusicVolume, sfx };
    }

    [GeneratedRegex(@"^[a-z]{2,3}-[A-Z]{2}-[A-Za-z0-9]+Neural$")]
    private static partial Regex AzureVoice();

    [GeneratedRegex(@"^[+-]\d{1,2}%$")]
    private static partial Regex AzureRate();

    [GeneratedRegex(@"^[+-]\d{1,3}Hz$")]
    private static partial Regex AzurePitch();
}
