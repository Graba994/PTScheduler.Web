using System.Text.Json;
using static PTScheduler.Portal.Services.Social.MetaApi;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Facebook Graph API dla strony firmowej: wymiana tokenu, lista stron (z podpiętym Instagramem),
/// publikacja (tekst, zdjęcie, kilka zdjęć, film), posty, komentarze i odpowiedzi.
/// Adresy można podmienić do testów: Social:FacebookGraphUrl, Social:FacebookVideoUrl.
/// </summary>
public class FacebookClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public const string Version = "v24.0";

    /// <summary>Uprawnienia do zaznaczenia przy generowaniu tokenu (strona + Instagram przez stronę).</summary>
    public static readonly string[] Scopes =
    [
        "pages_show_list", "pages_read_engagement", "pages_manage_posts", "pages_manage_engagement", "pages_read_user_content", "read_insights",
        "business_management", "instagram_basic", "instagram_content_publish", "instagram_manage_comments", "instagram_manage_insights",
        "pages_messaging", "instagram_manage_messages"
    ];

    private string Graph => (config?["Social:FacebookGraphUrl"] is { Length: > 0 } g ? g : "https://graph.facebook.com").TrimEnd('/');
    private string VideoHost => (config?["Social:FacebookVideoUrl"] is { Length: > 0 } g ? g : "https://graph-video.facebook.com").TrimEnd('/');

    public sealed record Page(string Id, string Name, string Token, string? Picture, string? Link, int Followers, string? IgId, string? IgUsername, string? IgPicture);
    public sealed record Post(string Id, string? Message, DateTime CreatedAt, string? Permalink, string? Picture, int Reactions, int Comments, int Shares);
    public sealed record Comment(string Id, string? ParentId, string Author, string Text, DateTime At, bool Hidden);

    private Task<JsonElement> Get(string path, string query, string token, CancellationToken ct) =>
        SendAsync(httpFactory, HttpMethod.Get, $"{Graph}/{Version}/{path}?{query}{(query.Length > 0 ? "&" : "")}access_token={Uri.EscapeDataString(token)}", null, ct);

    private Task<JsonElement> Post_(string path, Dictionary<string, string> form, string token, CancellationToken ct, string? host = null)
    {
        form["access_token"] = token;
        return SendAsync(httpFactory, HttpMethod.Post, $"{host ?? Graph}/{Version}/{path}", form, ct, host is null ? 60 : 300);
    }

    // ── Tokeny i strony ──

    /// <summary>Krótki token użytkownika (np. z Graph API Explorer) → długi (ok. 60 dni). Strony z niego mają tokeny bez terminu.</summary>
    public async Task<string> LongLivedUserTokenAsync(string appId, string secret, string shortToken, CancellationToken ct = default)
    {
        var j = await SendAsync(httpFactory, HttpMethod.Get,
            $"{Graph}/{Version}/oauth/access_token?grant_type=fb_exchange_token&client_id={Uri.EscapeDataString(appId)}&client_secret={Uri.EscapeDataString(secret)}&fb_exchange_token={Uri.EscapeDataString(shortToken)}", null, ct);
        return Str(j, "access_token") ?? shortToken;
    }

    public async Task<List<Page>> PagesAsync(string userToken, CancellationToken ct = default)
    {
        var j = await Get("me/accounts", "fields=id,name,access_token,link,followers_count,fan_count,picture{url},instagram_business_account{id,username,profile_picture_url}&limit=50", userToken, ct);
        var list = new List<Page>();
        if (!j.TryGetProperty("data", out var data)) return list;
        foreach (var p in data.EnumerateArray())
        {
            var ig = p.TryGetProperty("instagram_business_account", out var i) ? i : default;
            list.Add(new(Str(p, "id") ?? "", Str(p, "name") ?? "", Str(p, "access_token") ?? "",
                p.TryGetProperty("picture", out var pic) && pic.TryGetProperty("data", out var pd) ? Str(pd, "url") : null,
                Str(p, "link"), Math.Max(Int(p, "followers_count"), Int(p, "fan_count")),
                ig.ValueKind == JsonValueKind.Object ? Str(ig, "id") : null,
                ig.ValueKind == JsonValueKind.Object ? Str(ig, "username") : null,
                ig.ValueKind == JsonValueKind.Object ? Str(ig, "profile_picture_url") : null));
        }
        return list;
    }

    public async Task<(string Name, int Followers, string? Picture, string? Link)> PageInfoAsync(string pageId, string token, CancellationToken ct = default)
    {
        var j = await Get(pageId, "fields=name,followers_count,fan_count,link,picture{url}", token, ct);
        return (Str(j, "name") ?? "", Math.Max(Int(j, "followers_count"), Int(j, "fan_count")),
            j.TryGetProperty("picture", out var pic) && pic.TryGetProperty("data", out var pd) ? Str(pd, "url") : null, Str(j, "link"));
    }

    // ── Publikacja ──

    /// <summary>Post: sam tekst, jedno zdanie, kilka zdjęć (karuzela jako album w poście) albo film. Zwraca id posta.</summary>
    public async Task<string> PublishAsync(string pageId, string token, string message, IReadOnlyList<string> imageUrls, string? videoUrl, CancellationToken ct = default)
    {
        if (videoUrl is not null)
        {
            var v = await Post_($"{pageId}/videos", new() { ["file_url"] = videoUrl, ["description"] = message }, token, ct, VideoHost);
            return Str(v, "id") ?? throw new InstagramApiException("Facebook nie zwrócił id filmu.");
        }
        if (imageUrls.Count == 1)
        {
            var p = await Post_($"{pageId}/photos", new() { ["url"] = imageUrls[0], ["caption"] = message }, token, ct);
            return Str(p, "post_id") ?? Str(p, "id") ?? throw new InstagramApiException("Facebook nie zwrócił id posta.");
        }
        var form = new Dictionary<string, string> { ["message"] = message };
        for (var i = 0; i < imageUrls.Count; i++)
        {
            var photo = await Post_($"{pageId}/photos", new() { ["url"] = imageUrls[i], ["published"] = "false" }, token, ct);
            form[$"attached_media[{i}]"] = JsonSerializer.Serialize(new { media_fbid = Str(photo, "id") });
        }
        var feed = await Post_($"{pageId}/feed", form, token, ct);
        return Str(feed, "id") ?? throw new InstagramApiException("Facebook nie zwrócił id posta.");
    }

    public async Task<string?> PermalinkAsync(string objectId, string token, CancellationToken ct = default) =>
        Str(await Get(objectId, "fields=permalink_url", token, ct), "permalink_url");

    // ── Posty i komentarze ──

    public async Task<List<Post>> PostsAsync(string pageId, string token, int limit = 25, CancellationToken ct = default)
    {
        var j = await Get($"{pageId}/posts", $"fields=id,message,created_time,permalink_url,full_picture,shares,reactions.summary(total_count).limit(0),comments.summary(total_count).limit(0)&limit={limit}", token, ct);
        var list = new List<Post>();
        if (!j.TryGetProperty("data", out var data)) return list;
        foreach (var p in data.EnumerateArray())
            list.Add(new(Str(p, "id") ?? "", Str(p, "message"), Date(p, "created_time"), Str(p, "permalink_url"), Str(p, "full_picture"),
                Summary(p, "reactions"), Summary(p, "comments"), p.TryGetProperty("shares", out var sh) ? Int(sh, "count") : 0));
        return list;
    }

    public async Task<List<Comment>> CommentsAsync(string postId, string token, CancellationToken ct = default)
    {
        var j = await Get($"{postId}/comments", "fields=id,message,from{name},created_time,is_hidden,comments{id,message,from{name},created_time,is_hidden}&filter=toplevel&limit=50", token, ct);
        var list = new List<Comment>();
        if (!j.TryGetProperty("data", out var data)) return list;
        foreach (var c in data.EnumerateArray())
        {
            var id = Str(c, "id") ?? "";
            list.Add(new(id, null, Author(c), Str(c, "message") ?? "", Date(c, "created_time"), Bool(c, "is_hidden")));
            if (c.TryGetProperty("comments", out var rep) && rep.TryGetProperty("data", out var rd))
                foreach (var r in rd.EnumerateArray())
                    list.Add(new(Str(r, "id") ?? "", id, Author(r), Str(r, "message") ?? "", Date(r, "created_time"), Bool(r, "is_hidden")));
        }
        return list;
    }

    /// <summary>Autor komentarza — bez uprawnień do danych osób Facebook podaje tylko część pól; wtedy „Użytkownik Facebooka”.</summary>
    private static string Author(JsonElement c) => c.TryGetProperty("from", out var f) ? Str(f, "name") ?? "Użytkownik Facebooka" : "Użytkownik Facebooka";

    public async Task<string?> ReplyAsync(string commentId, string token, string message, CancellationToken ct = default) =>
        Str(await Post_($"{commentId}/comments", new() { ["message"] = message }, token, ct), "id");

    public Task HideAsync(string commentId, string token, bool hide, CancellationToken ct = default) =>
        Post_(commentId, new() { ["is_hidden"] = hide ? "true" : "false" }, token, ct);
}
