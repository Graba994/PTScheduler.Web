using System.Text.Json;

namespace PTScheduler.Portal.Services.Social;

/// <summary>Wspólne wywołania HTTP do API Meta (Facebook, Threads): JSON, błędy po polsku (<see cref="InstagramApiException"/>).</summary>
public static class MetaApi
{
    public static async Task<JsonElement> SendAsync(IHttpClientFactory httpFactory, HttpMethod method, string url, Dictionary<string, string>? form, CancellationToken ct, int timeoutSeconds = 60)
    {
        using var req = new HttpRequestMessage(method, url);
        if (form is not null) req.Content = new FormUrlEncodedContent(form);
        var http = httpFactory.CreateClient("meta");
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InstagramApiException(ex is TaskCanceledException ? "Serwer Meta nie odpowiedział na czas." : "Brak połączenia z serwerem Meta.");
        }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            JsonElement json;
            try { json = string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}").RootElement.Clone() : JsonDocument.Parse(body).RootElement.Clone(); }
            catch (JsonException) { throw new InstagramApiException($"Nieczytelna odpowiedź serwera Meta (HTTP {(int)resp.StatusCode})."); }
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var err))
            {
                var msg = err.ValueKind == JsonValueKind.Object ? Str(err, "error_user_msg") ?? Str(err, "message") : err.ToString();
                int? code = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : null;
                int? sub = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var si) ? si : null;
                throw new InstagramApiException(InstagramClient.Translate(msg, code, sub), code, sub);
            }
            if (!resp.IsSuccessStatusCode) throw new InstagramApiException($"Serwer Meta odrzucił zapytanie (HTTP {(int)resp.StatusCode}).");
            return json;
        }
    }

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;

    public static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? (int)Math.Min(n, int.MaxValue)
            : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s) ? s : 0
            : 0;

    public static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static DateTime Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s.Replace("+0000", "+00:00"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.UtcDateTime : DateTime.UtcNow;

    /// <summary>Liczba z „summary.total_count” (reakcje, komentarze na Facebooku).</summary>
    public static int Summary(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetProperty("summary", out var s) ? Int(s, "total_count") : 0;
}
