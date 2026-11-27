using static PTScheduler.Portal.Services.Social.MetaApi;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Threads API (Meta): profil, odnawianie tokenu (60 dni), publikacja (tekst, obraz, karuzela, film) i statystyki postu.
/// Publikacja jak na Instagramie: kontener → status → publish. Adres do testów: Social:ThreadsUrl.
/// </summary>
public class ThreadsClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public const string Version = "v1.0";
    public const int MaxText = 500;
    public static readonly string[] Scopes = ["threads_basic", "threads_content_publish", "threads_read_replies", "threads_manage_replies", "threads_manage_insights"];

    private string Graph => (config?["Social:ThreadsUrl"] is { Length: > 0 } g ? g : "https://graph.threads.net").TrimEnd('/');

    private Task<System.Text.Json.JsonElement> Get(string path, string query, string token, CancellationToken ct) =>
        SendAsync(httpFactory, HttpMethod.Get, $"{Graph}/{Version}/{path}?{query}{(query.Length > 0 ? "&" : "")}access_token={Uri.EscapeDataString(token)}", null, ct);

    private Task<System.Text.Json.JsonElement> Post(string path, Dictionary<string, string> form, string token, CancellationToken ct)
    {
        form["access_token"] = token;
        return SendAsync(httpFactory, HttpMethod.Post, $"{Graph}/{Version}/{path}", form, ct);
    }

    public async Task<(string Id, string Username, string? Picture)> MeAsync(string token, CancellationToken ct = default)
    {
        var j = await Get("me", "fields=id,username,threads_profile_picture_url", token, ct);
        return (Str(j, "id") ?? "", Str(j, "username") ?? "", Str(j, "threads_profile_picture_url"));
    }

    /// <summary>Krótki token → długi (60 dni), gdy znamy klucz aplikacji.</summary>
    public async Task<(string Token, DateTime ExpiresAt)> LongLivedAsync(string secret, string shortToken, CancellationToken ct = default)
    {
        var j = await SendAsync(httpFactory, HttpMethod.Get, $"{Graph}/access_token?grant_type=th_exchange_token&client_secret={Uri.EscapeDataString(secret)}&access_token={Uri.EscapeDataString(shortToken)}", null, ct);
        return (Str(j, "access_token") ?? shortToken, DateTime.UtcNow.AddSeconds(Int(j, "expires_in") is > 0 and var s ? s : 5184000));
    }

    public async Task<(string Token, DateTime ExpiresAt)> RefreshAsync(string token, CancellationToken ct = default)
    {
        var j = await SendAsync(httpFactory, HttpMethod.Get, $"{Graph}/refresh_access_token?grant_type=th_refresh_token&access_token={Uri.EscapeDataString(token)}", null, ct);
        return (Str(j, "access_token") ?? token, DateTime.UtcNow.AddSeconds(Int(j, "expires_in") is > 0 and var s ? s : 5184000));
    }

    /// <summary>Kontener posta. Kilka obrazów → karuzela (najpierw elementy, potem kontener z children). Zwraca id kontenera.</summary>
    public async Task<string> CreateAsync(string userId, string token, string text, IReadOnlyList<string> imageUrls, string? videoUrl, CancellationToken ct = default)
    {
        if (videoUrl is not null)
            return Str(await Post($"{userId}/threads", new() { ["media_type"] = "VIDEO", ["video_url"] = videoUrl, ["text"] = text }, token, ct), "id")
                   ?? throw new InstagramApiException("Threads nie zwróciło kontenera.");
        if (imageUrls.Count == 1)
            return Str(await Post($"{userId}/threads", new() { ["media_type"] = "IMAGE", ["image_url"] = imageUrls[0], ["text"] = text }, token, ct), "id")
                   ?? throw new InstagramApiException("Threads nie zwróciło kontenera.");
        if (imageUrls.Count > 1)
        {
            var children = new List<string>();
            foreach (var url in imageUrls)
                children.Add(Str(await Post($"{userId}/threads", new() { ["media_type"] = "IMAGE", ["image_url"] = url, ["is_carousel_item"] = "true" }, token, ct), "id") ?? "");
            return Str(await Post($"{userId}/threads", new() { ["media_type"] = "CAROUSEL", ["children"] = string.Join(',', children), ["text"] = text }, token, ct), "id")
                   ?? throw new InstagramApiException("Threads nie zwróciło kontenera.");
        }
        return Str(await Post($"{userId}/threads", new() { ["media_type"] = "TEXT", ["text"] = text }, token, ct), "id")
               ?? throw new InstagramApiException("Threads nie zwróciło kontenera.");
    }

    /// <summary>FINISHED, IN_PROGRESS, ERROR, EXPIRED albo PUBLISHED.</summary>
    public async Task<(string Status, string? Error)> StatusAsync(string containerId, string token, CancellationToken ct = default)
    {
        var j = await Get(containerId, "fields=status,error_message", token, ct);
        return (Str(j, "status") ?? "IN_PROGRESS", Str(j, "error_message"));
    }

    public async Task<string> PublishAsync(string userId, string token, string containerId, CancellationToken ct = default) =>
        Str(await Post($"{userId}/threads_publish", new() { ["creation_id"] = containerId }, token, ct), "id")
        ?? throw new InstagramApiException("Threads nie zwróciło id posta.");

    public async Task<string?> PermalinkAsync(string mediaId, string token, CancellationToken ct = default) =>
        Str(await Get(mediaId, "fields=permalink", token, ct), "permalink");

    public async Task<Dictionary<string, int>> InsightsAsync(string mediaId, string token, CancellationToken ct = default)
    {
        var result = new Dictionary<string, int>();
        var j = await Get($"{mediaId}/insights", "metric=views,likes,replies,reposts,quotes", token, ct);
        if (!j.TryGetProperty("data", out var data)) return result;
        foreach (var m in data.EnumerateArray())
            if (Str(m, "name") is { } name && m.TryGetProperty("values", out var vals) && vals.GetArrayLength() > 0)
                result[name] = Int(vals[0], "value");
        return result;
    }
}
