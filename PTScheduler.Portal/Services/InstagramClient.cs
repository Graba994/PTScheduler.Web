using System.Globalization;
using System.Text.Json;

namespace PTScheduler.Portal.Services;

public sealed class InstagramApiException(string message, int? code = null, int? subcode = null) : Exception(message)
{
    public int? Code { get; } = code;
    public int? Subcode { get; } = subcode;
    /// <summary>Token wygasł albo został cofnięty — trzeba połączyć konto ponownie.</summary>
    public bool TokenInvalid => Code == 190;
    /// <summary>Limit publikacji z API na dobę.</summary>
    public bool PublishLimit => Code == 9 && Subcode == 2207042;
}

/// <summary>
/// Instagram API z logowaniem przez Instagram (konto profesjonalne, bez strony na Facebooku).
/// Same wywołania HTTP — bez bazy. Hosty: www.instagram.com (logowanie), api.instagram.com (kod → token),
/// graph.instagram.com (wszystko inne).
/// </summary>
public class InstagramClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public const string Version = "v24.0";
    /// <summary>Adresy API — do testów można je podmienić (Instagram:GraphUrl, Instagram:ApiUrl).</summary>
    private string Graph => (config?["Instagram:GraphUrl"] is { Length: > 0 } g ? g : "https://graph.instagram.com").TrimEnd('/');
    private string Api => (config?["Instagram:ApiUrl"] is { Length: > 0 } a ? a : "https://api.instagram.com").TrimEnd('/');

    /// <summary>Uprawnienia: profil, publikacja, komentarze, statystyki.</summary>
    public static readonly string[] Scopes =
        ["instagram_business_basic", "instagram_business_content_publish", "instagram_business_manage_comments", "instagram_business_manage_insights"];

    public sealed record Profile(string UserId, string Username, string? Name, string? Picture, int Followers, int Follows, int MediaCount);
    public sealed record Media(string Id, string MediaType, string? ProductType, string? Caption, string? Permalink, string? MediaUrl, string? ThumbnailUrl,
        DateTime Timestamp, int Likes, int Comments);
    public sealed record Comment(string Id, string? ParentId, string Username, string Text, DateTime At, bool Hidden);

    public static string AuthorizeUrl(string appId, string redirectUri, string state) =>
        "https://www.instagram.com/oauth/authorize?enable_fb_login=0&force_authentication=1"
        + $"&client_id={Uri.EscapeDataString(appId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code"
        + $"&scope={Uri.EscapeDataString(string.Join(',', Scopes))}&state={Uri.EscapeDataString(state)}";

    // ── Tokeny ──

    /// <summary>Kod z logowania → krótki token (1 h). Instagram dokleja do kodu „#_” — obcinamy.</summary>
    public async Task<(string Token, string? UserId)> ExchangeCodeAsync(string appId, string secret, string redirectUri, string code, CancellationToken ct = default)
    {
        var clean = code.Split('#')[0];
        var json = await SendAsync(HttpMethod.Post, $"{Api}/oauth/access_token", new()
        {
            ["client_id"] = appId, ["client_secret"] = secret, ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri, ["code"] = clean
        }, ct);
        // Odpowiedź bywa płaska albo w „data[0]”.
        var root = json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0 ? data[0] : json;
        return (Str(root, "access_token") ?? throw new InstagramApiException("Instagram nie zwrócił tokenu."), Str(root, "user_id"));
    }

    /// <summary>Krótki token → długi (60 dni).</summary>
    public async Task<(string Token, DateTime ExpiresAt)> LongLivedAsync(string secret, string shortToken, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"{Graph}/access_token?grant_type=ig_exchange_token&client_secret={Uri.EscapeDataString(secret)}&access_token={Uri.EscapeDataString(shortToken)}", null, ct);
        return (Str(json, "access_token") ?? shortToken, DateTime.UtcNow.AddSeconds(Int(json, "expires_in") is > 0 and var s ? s : 5184000));
    }

    /// <summary>Odnawia długi token o kolejne 60 dni (token musi mieć co najmniej dobę).</summary>
    public async Task<(string Token, DateTime ExpiresAt)> RefreshAsync(string token, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, $"{Graph}/refresh_access_token?grant_type=ig_refresh_token&access_token={Uri.EscapeDataString(token)}", null, ct);
        return (Str(json, "access_token") ?? token, DateTime.UtcNow.AddSeconds(Int(json, "expires_in") is > 0 and var s ? s : 5184000));
    }

    // ── Profil i posty ──

    public async Task<Profile> MeAsync(string token, CancellationToken ct = default)
    {
        var j = await GetAsync(token, "me", "fields=user_id,username,name,profile_picture_url,followers_count,follows_count,media_count", ct);
        return new(Str(j, "user_id") ?? Str(j, "id") ?? "", Str(j, "username") ?? "", Str(j, "name"), Str(j, "profile_picture_url"),
            Int(j, "followers_count"), Int(j, "follows_count"), Int(j, "media_count"));
    }

    public async Task<List<Media>> MediaAsync(string token, string igId, int limit = 50, CancellationToken ct = default)
    {
        var j = await GetAsync(token, $"{igId}/media", $"fields=id,caption,media_type,media_product_type,permalink,media_url,thumbnail_url,timestamp,like_count,comments_count&limit={limit}", ct);
        var list = new List<Media>();
        if (!j.TryGetProperty("data", out var data)) return list;
        foreach (var m in data.EnumerateArray())
            list.Add(new(Str(m, "id") ?? "", Str(m, "media_type") ?? "", Str(m, "media_product_type"), Str(m, "caption"), Str(m, "permalink"),
                Str(m, "media_url"), Str(m, "thumbnail_url"), Date(m, "timestamp"), Int(m, "like_count"), Int(m, "comments_count")));
        return list;
    }

    /// <summary>
    /// Statystyki posta. Meta co jakiś czas wycofuje wskaźniki, więc pytamy o każdy osobno —
    /// brak jednego nie psuje reszty.
    /// </summary>
    public async Task<Dictionary<string, int>> MediaInsightsAsync(string token, string mediaId, string? productType, CancellationToken ct = default)
    {
        string[] metrics = productType == "STORY"
            ? ["views", "reach", "shares", "total_interactions"]
            : ["views", "reach", "saved", "shares", "total_interactions"];
        var result = new Dictionary<string, int>();
        try
        {
            ReadInsights(await GetAsync(token, $"{mediaId}/insights", $"metric={string.Join(',', metrics)}", ct), result);
            return result;
        }
        catch (InstagramApiException ex) when (!ex.TokenInvalid) { }
        foreach (var metric in metrics)
        {
            try { ReadInsights(await GetAsync(token, $"{mediaId}/insights", $"metric={metric}", ct), result); }
            catch (InstagramApiException ex) when (!ex.TokenInvalid) { }
        }
        return result;
    }

    /// <summary>Statystyki konta za okres (suma): zasięg, wyświetlenia, interakcje, zaangażowane konta.</summary>
    public async Task<Dictionary<string, int>> AccountInsightsAsync(string token, string igId, DateTime sinceUtc, DateTime untilUtc, CancellationToken ct = default)
    {
        var result = new Dictionary<string, int>();
        var since = new DateTimeOffset(DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var until = new DateTimeOffset(DateTime.SpecifyKind(untilUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        foreach (var metric in new[] { "reach", "views", "total_interactions", "accounts_engaged" })
        {
            try { ReadInsights(await GetAsync(token, $"{igId}/insights", $"metric={metric}&period=day&metric_type=total_value&since={since}&until={until}", ct), result); }
            catch (InstagramApiException ex) when (!ex.TokenInvalid) { }
        }
        return result;
    }

    private static void ReadInsights(JsonElement j, Dictionary<string, int> into)
    {
        if (!j.TryGetProperty("data", out var data)) return;
        foreach (var m in data.EnumerateArray())
        {
            var name = Str(m, "name");
            if (name is null) continue;
            if (m.TryGetProperty("total_value", out var tv) && tv.TryGetProperty("value", out var v) && v.TryGetInt32(out var n)) into[name] = n;
            else if (m.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
                into[name] = vals.EnumerateArray().Sum(x => x.TryGetProperty("value", out var vv) && vv.TryGetInt32(out var nn) ? nn : 0);
        }
    }

    // ── Publikacja ──

    /// <summary>Kontener (zdjęcie, film, karuzela, relacja). Zwraca jego id.</summary>
    public async Task<string> CreateContainerAsync(string token, string igId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        var j = await PostAsync(token, $"{igId}/media", fields, ct);
        return Str(j, "id") ?? throw new InstagramApiException("Instagram nie zwrócił kontenera.");
    }

    /// <summary>FINISHED, IN_PROGRESS, ERROR, EXPIRED albo PUBLISHED.</summary>
    public async Task<(string Status, string? Detail)> ContainerStatusAsync(string token, string containerId, CancellationToken ct = default)
    {
        var j = await GetAsync(token, containerId, "fields=status_code,status", ct);
        return (Str(j, "status_code") ?? "IN_PROGRESS", Str(j, "status"));
    }

    public async Task<string> PublishAsync(string token, string igId, string containerId, CancellationToken ct = default)
    {
        var j = await PostAsync(token, $"{igId}/media_publish", new() { ["creation_id"] = containerId }, ct);
        return Str(j, "id") ?? throw new InstagramApiException("Instagram nie zwrócił id posta.");
    }

    public async Task<string?> PermalinkAsync(string token, string mediaId, CancellationToken ct = default) =>
        Str(await GetAsync(token, mediaId, "fields=permalink", ct), "permalink");

    /// <summary>Ile postów opublikowano z API w ciągu doby i ile wolno.</summary>
    public async Task<(int Used, int Total)> PublishingLimitAsync(string token, string igId, CancellationToken ct = default)
    {
        var j = await GetAsync(token, $"{igId}/content_publishing_limit", "fields=quota_usage,config", ct);
        if (!j.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return (0, 100);
        var d = data[0];
        var total = d.TryGetProperty("config", out var cfg) ? Int(cfg, "quota_total") : 0;
        return (Int(d, "quota_usage"), total > 0 ? total : 100);
    }

    // ── Komentarze ──

    public async Task<List<Comment>> CommentsAsync(string token, string mediaId, CancellationToken ct = default)
    {
        var j = await GetAsync(token, $"{mediaId}/comments", "fields=id,text,username,timestamp,hidden,replies{id,text,username,timestamp,hidden}&limit=50", ct);
        var list = new List<Comment>();
        if (!j.TryGetProperty("data", out var data)) return list;
        foreach (var c in data.EnumerateArray())
        {
            var id = Str(c, "id") ?? "";
            list.Add(new(id, null, Str(c, "username") ?? "", Str(c, "text") ?? "", Date(c, "timestamp"), Bool(c, "hidden")));
            if (c.TryGetProperty("replies", out var rep) && rep.TryGetProperty("data", out var rd))
                foreach (var r in rd.EnumerateArray())
                    list.Add(new(Str(r, "id") ?? "", id, Str(r, "username") ?? "", Str(r, "text") ?? "", Date(r, "timestamp"), Bool(r, "hidden")));
        }
        return list;
    }

    public async Task<string?> ReplyAsync(string token, string commentId, string message, CancellationToken ct = default) =>
        Str(await PostAsync(token, $"{commentId}/replies", new() { ["message"] = message }, ct), "id");

    public Task HideAsync(string token, string commentId, bool hide, CancellationToken ct = default) =>
        PostAsync(token, commentId, new() { ["hide"] = hide ? "true" : "false" }, ct);

    public Task DeleteCommentAsync(string token, string commentId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"{Graph}/{Version}/{commentId}?access_token={Uri.EscapeDataString(token)}", null, ct);

    // ── HTTP ──

    private Task<JsonElement> GetAsync(string token, string path, string query, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"{Graph}/{Version}/{path}?{query}&access_token={Uri.EscapeDataString(token)}", null, ct);

    private Task<JsonElement> PostAsync(string token, string path, Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["access_token"] = token;
        return SendAsync(HttpMethod.Post, $"{Graph}/{Version}/{path}", fields, ct);
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string url, Dictionary<string, string>? form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, url);
        if (form is not null) req.Content = new FormUrlEncodedContent(form);
        var http = httpFactory.CreateClient("instagram");
        http.Timeout = TimeSpan.FromSeconds(40);
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InstagramApiException(ex is TaskCanceledException ? "Instagram nie odpowiedział na czas." : "Brak połączenia z Instagramem.");
        }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            JsonElement json;
            try { json = string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone(); }
            catch (JsonException) { throw new InstagramApiException($"Nieczytelna odpowiedź Instagrama (HTTP {(int)resp.StatusCode})."); }
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var err))
            {
                var msg = err.ValueKind == JsonValueKind.Object ? Str(err, "error_user_msg") ?? Str(err, "message") : err.ToString();
                int? code = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : null;
                int? sub = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var si) ? si : null;
                throw new InstagramApiException(Translate(msg, code, sub), code, sub);
            }
            if (!resp.IsSuccessStatusCode)
                throw new InstagramApiException(Str(json, "error_message") ?? $"Instagram odrzucił zapytanie (HTTP {(int)resp.StatusCode}).");
            return json.ValueKind == JsonValueKind.Undefined ? JsonDocument.Parse("{}").RootElement.Clone() : json;
        }
    }

    /// <summary>Najczęstsze błędy po polsku — reszta w oryginale.</summary>
    public static string Translate(string? message, int? code, int? subcode) => (code, subcode) switch
    {
        (190, _) => "Token Instagrama wygasł albo został cofnięty — połącz konto ponownie.",
        (9, 2207042) => "Wyczerpany dzienny limit publikacji z aplikacji — spróbuj jutro.",
        (_, 2207026) => "Film ma nieobsługiwany format — użyj MP4 (H.264, AAC), 3–90 s, pion 9:16.",
        (_, 2207052) => "Instagram nie mógł pobrać pliku z podanego adresu — sprawdź, czy otwiera się publicznie.",
        (_, 2207004) => "Zdjęcie jest za duże albo ma zły format — użyj JPEG do 8 MB.",
        (_, 2207009) => "Proporcje zdjęcia poza zakresem 4:5 – 1,91:1.",
        (_, 2207027) => "Instagram jeszcze przetwarza plik — spróbuję ponownie.",
        (4, _) or (17, _) or (32, _) or (613, _) => "Za dużo zapytań do Instagrama — chwila przerwy i spróbuję ponownie.",
        (10, _) or (200, _) => "Brak uprawnienia w aplikacji Meta — połącz konto ponownie i zaznacz wszystkie zgody.",
        _ => string.IsNullOrWhiteSpace(message) ? "Instagram zwrócił błąd." : message!
    };

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;

    private static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? (int)Math.Min(n, int.MaxValue)
            : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s) ? s : 0
            : 0;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static DateTime Date(JsonElement e, string name) =>
        Str(e, name) is { } s && DateTimeOffset.TryParse(s.Replace("+0000", "+00:00"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.UtcDateTime : DateTime.UtcNow;
}
