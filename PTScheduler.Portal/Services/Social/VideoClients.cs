using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static PTScheduler.Portal.Services.Social.MetaApi;

namespace PTScheduler.Portal.Services.Social;

/// <summary>Tokeny OAuth kanału wideo (zapisywane zaszyfrowane w <c>SocialChannel.Token</c>).</summary>
public sealed record OAuthTokens(string Access, string? Refresh, DateTime ExpiresAt);

/// <summary>Wspólne wywołania JSON dla TikToka i Google (błędy po polsku jako <see cref="InstagramApiException"/>).</summary>
internal static class VideoHttp
{
    /// <summary>JSON z nagłówkiem długości (bez przesyłania „chunked”, którego nie każde API lubi).</summary>
    public static StringContent Json(object body) => new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

    public static async Task<JsonElement> SendAsync(HttpClient http, HttpRequestMessage req, string service, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InstagramApiException(ex is TaskCanceledException ? $"{service} nie odpowiedział na czas." : $"Brak połączenia z {service}.");
        }
        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            JsonElement json;
            try { json = string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}").RootElement.Clone() : JsonDocument.Parse(body).RootElement.Clone(); }
            catch (JsonException) { throw new InstagramApiException($"{service}: nieczytelna odpowiedź (HTTP {(int)resp.StatusCode})."); }
            // TikTok: {"error":{"code":"ok"|"…","message":…}} (także przy sukcesie); Google API: {"error":{"code":401,"message":…}};
            // Google OAuth: {"error":"invalid_grant","error_description":…}. Token nieważny → kod 190 (jak w Meta), żeby panel poprosił o ponowne połączenie.
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object && Str(err, "code") is var code && code != "ok")
                {
                    var invalid = code is "access_token_invalid" or "scope_not_authorized" or "401" || resp.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                    throw new InstagramApiException($"{service}: {Str(err, "message") ?? code ?? "nieznany błąd"}", invalid ? 190 : null);
                }
                if (err.ValueKind == JsonValueKind.String)
                    throw new InstagramApiException($"{service}: {Str(json, "error_description") ?? err.GetString()}", err.GetString() == "invalid_grant" ? 190 : null);
            }
            if (!resp.IsSuccessStatusCode)
                throw new InstagramApiException($"{service} odrzucił zapytanie (HTTP {(int)resp.StatusCode}).", resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? 190 : null);
            return json;
        }
    }
}

/// <summary>
/// TikTok Content Posting API: logowanie (OAuth), odświeżanie tokenu, wysyłka filmu (FILE_UPLOAD — bez weryfikacji domeny)
/// i stan publikacji. Przed audytem aplikacji TikTok przyjmuje tylko posty prywatne (SELF_ONLY).
/// </summary>
public class TikTokClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public static readonly string[] Scopes = ["user.info.basic", "video.publish", "video.upload"];
    private string Api => (config?["Social:TikTokUrl"] is { Length: > 0 } u ? u : "https://open.tiktokapis.com").TrimEnd('/');
    private string AuthBase => (config?["Social:TikTokAuthUrl"] is { Length: > 0 } u ? u : "https://www.tiktok.com").TrimEnd('/');

    public string AuthorizeUrl(string clientKey, string redirectUri, string state) =>
        $"{AuthBase}/v2/auth/authorize/?client_key={Uri.EscapeDataString(clientKey)}&scope={Uri.EscapeDataString(string.Join(',', Scopes))}" +
        $"&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";

    private HttpClient Http(int timeoutSeconds = 60)
    {
        var http = httpFactory.CreateClient("tiktok");
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return http;
    }

    private async Task<OAuthTokens> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}/v2/oauth/token/") { Content = new FormUrlEncodedContent(form) };
        var j = await VideoHttp.SendAsync(Http(), req, "TikTok", ct);
        return new OAuthTokens(Str(j, "access_token") ?? throw new InstagramApiException("TikTok nie zwrócił tokenu."), Str(j, "refresh_token"),
            DateTime.UtcNow.AddSeconds(Math.Max(60, Int(j, "expires_in") - 120)));
    }

    public Task<OAuthTokens> ExchangeAsync(string clientKey, string secret, string code, string redirectUri, CancellationToken ct = default) =>
        TokenAsync(new() { ["client_key"] = clientKey, ["client_secret"] = secret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri }, ct);

    public async Task<OAuthTokens> RefreshAsync(string clientKey, string secret, string refreshToken, CancellationToken ct = default)
    {
        var t = await TokenAsync(new() { ["client_key"] = clientKey, ["client_secret"] = secret, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);
        return t with { Refresh = t.Refresh ?? refreshToken };
    }

    private HttpRequestMessage Json(string path, object body, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Api}{path}") { Content = VideoHttp.Json(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    public async Task<(string Username, string? Avatar)> UserAsync(string token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}/v2/user/info/?fields=open_id,display_name,avatar_url,username");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var j = await VideoHttp.SendAsync(Http(), req, "TikTok", ct);
        var u = j.TryGetProperty("data", out var d) && d.TryGetProperty("user", out var user) ? user : default;
        return (Str(u, "username") ?? Str(u, "display_name") ?? "TikTok", Str(u, "avatar_url"));
    }

    /// <summary>Wysyła film jednym kawałkiem (do 64 MB) i zwraca publish_id. privacy: SELF_ONLY przed audytem.</summary>
    public async Task<string> UploadAsync(string token, byte[] video, string title, string privacy, CancellationToken ct = default)
    {
        if (video.Length > 64 * 1024 * 1024) throw new InstagramApiException("Film jest za duży dla TikToka z Portalu (najwyżej 64 MB).");
        using var init = Json("/v2/post/publish/video/init/", new
        {
            post_info = new { title, privacy_level = privacy, disable_comment = false, disable_duet = false, disable_stitch = false },
            source_info = new { source = "FILE_UPLOAD", video_size = video.Length, chunk_size = video.Length, total_chunk_count = 1 }
        }, token);
        var j = await VideoHttp.SendAsync(Http(), init, "TikTok", ct);
        var data = j.GetProperty("data");
        var publishId = Str(data, "publish_id") ?? throw new InstagramApiException("TikTok nie zwrócił publish_id.");
        var uploadUrl = Str(data, "upload_url") ?? throw new InstagramApiException("TikTok nie zwrócił adresu wysyłki.");
        using var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(video) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        put.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, video.Length - 1, video.Length);
        await VideoHttp.SendAsync(Http(600), put, "TikTok", ct);
        return publishId;
    }

    /// <summary>Stan: PROCESSING_UPLOAD, PROCESSING_DOWNLOAD, SEND_TO_USER_INBOX, PUBLISH_COMPLETE, FAILED; id posta, gdy publiczny.</summary>
    public async Task<(string Status, string? PostId, string? Error)> StatusAsync(string token, string publishId, CancellationToken ct = default)
    {
        using var req = Json("/v2/post/publish/status/fetch/", new { publish_id = publishId }, token);
        var j = await VideoHttp.SendAsync(Http(), req, "TikTok", ct);
        var d = j.GetProperty("data");
        string? postId = d.TryGetProperty("publicaly_available_post_id", out var ids) && ids.ValueKind == JsonValueKind.Array && ids.GetArrayLength() > 0
            ? ids[0].ToString() : null;
        return (Str(d, "status") ?? "", postId, Str(d, "fail_reason"));
    }
}

/// <summary>
/// YouTube Data API v3: logowanie Google (OAuth, offline), kanał i wysyłka filmu (resumable). Pionowy film do 3 minut
/// YouTube traktuje jako Shorts. Projekty niezweryfikowane przez Google wrzucają filmy jako prywatne.
/// </summary>
public class YouTubeClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public static readonly string[] Scopes = ["https://www.googleapis.com/auth/youtube.upload", "https://www.googleapis.com/auth/youtube.readonly"];
    private string Api => (config?["Social:YouTubeUrl"] is { Length: > 0 } u ? u : "https://www.googleapis.com").TrimEnd('/');
    private string TokenUrl => (config?["Social:GoogleTokenUrl"] is { Length: > 0 } u ? u : "https://oauth2.googleapis.com/token");

    public static string AuthorizeUrl(string clientId, string redirectUri, string state) =>
        $"https://accounts.google.com/o/oauth2/v2/auth?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&response_type=code&scope={Uri.EscapeDataString(string.Join(' ', Scopes))}&access_type=offline&prompt=consent&state={Uri.EscapeDataString(state)}";

    private HttpClient Http(int timeoutSeconds = 60)
    {
        var http = httpFactory.CreateClient("youtube");
        http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return http;
    }

    private async Task<OAuthTokens> TokenAsync(Dictionary<string, string> form, string? previousRefresh, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        var j = await VideoHttp.SendAsync(Http(), req, "Google", ct);
        return new OAuthTokens(Str(j, "access_token") ?? throw new InstagramApiException("Google nie zwrócił tokenu."), Str(j, "refresh_token") ?? previousRefresh,
            DateTime.UtcNow.AddSeconds(Math.Max(60, Int(j, "expires_in") - 120)));
    }

    public Task<OAuthTokens> ExchangeAsync(string clientId, string secret, string code, string redirectUri, CancellationToken ct = default) =>
        TokenAsync(new() { ["client_id"] = clientId, ["client_secret"] = secret, ["code"] = code, ["grant_type"] = "authorization_code", ["redirect_uri"] = redirectUri }, null, ct);

    public Task<OAuthTokens> RefreshAsync(string clientId, string secret, string refreshToken, CancellationToken ct = default) =>
        TokenAsync(new() { ["client_id"] = clientId, ["client_secret"] = secret, ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, refreshToken, ct);

    public async Task<(string Id, string Title, string? Picture, int Subscribers)> ChannelAsync(string token, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Api}/youtube/v3/channels?part=snippet,statistics&mine=true");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var j = await VideoHttp.SendAsync(Http(), req, "YouTube", ct);
        if (!j.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw new InstagramApiException("To konto Google nie ma kanału YouTube.");
        var c = items[0];
        var sn = c.GetProperty("snippet");
        var pic = sn.TryGetProperty("thumbnails", out var th) && th.TryGetProperty("default", out var df) ? Str(df, "url") : null;
        return (Str(c, "id") ?? "", Str(sn, "title") ?? "YouTube", pic, c.TryGetProperty("statistics", out var st) ? Int(st, "subscriberCount") : 0);
    }

    /// <summary>Wysyłka filmu (resumable, jednym kawałkiem). privacy: private | unlisted | public. Zwraca id filmu.</summary>
    public async Task<string> UploadAsync(string token, byte[] video, string title, string description, IEnumerable<string> tags, string privacy, CancellationToken ct = default)
    {
        var meta = new
        {
            snippet = new { title = title.Length > 100 ? title[..100] : title, description = description.Length > 5000 ? description[..5000] : description, tags = tags.Take(15).ToArray(), categoryId = "26" },
            status = new { privacyStatus = privacy, selfDeclaredMadeForKids = false }
        };
        using var start = new HttpRequestMessage(HttpMethod.Post, $"{Api}/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status") { Content = VideoHttp.Json(meta) };
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        start.Headers.Add("X-Upload-Content-Type", "video/mp4");
        start.Headers.Add("X-Upload-Content-Length", video.Length.ToString());
        string location;
        using (var resp = await Http().SendAsync(start, ct))
        {
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new InstagramApiException($"YouTube odrzucił wysyłkę (HTTP {(int)resp.StatusCode}): {Clip(body, 300)}",
                    resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? 190 : null);
            }
            location = resp.Headers.Location?.ToString() ?? throw new InstagramApiException("YouTube nie zwrócił adresu wysyłki.");
        }
        using var put = new HttpRequestMessage(HttpMethod.Put, location) { Content = new ByteArrayContent(video) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var j = await VideoHttp.SendAsync(Http(900), put, "YouTube", ct);
        return Str(j, "id") ?? throw new InstagramApiException("YouTube nie zwrócił id filmu.");
    }

    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
}
