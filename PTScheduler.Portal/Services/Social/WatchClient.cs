using System.Text.Json;
using static PTScheduler.Portal.Services.Social.MetaApi;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Obserwacja na Instagramie (API przez stronę na Facebooku): Business Discovery — publiczne dane innych kont firmowych
/// i twórców (obserwujący, ostatnie posty z polubieniami i komentarzami) oraz wyszukiwanie hasztagów (najpopularniejsze
/// i najnowsze posty). Meta: najwyżej 30 różnych hasztagów w 7 dni.
/// </summary>
public class WatchClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public const string Version = "v24.0";
    private string Graph => (config?["Social:FacebookGraphUrl"] is { Length: > 0 } g ? g : "https://graph.facebook.com").TrimEnd('/');

    public sealed record Media(string Id, string? Caption, int Likes, int Comments, string Type, string? Permalink, DateTime At);
    public sealed record Profile(string Username, string? Name, string? Bio, int Followers, int MediaCount, string? Picture, List<Media> Media);

    private Task<JsonElement> Get(string path, string query, string token, CancellationToken ct) =>
        SendAsync(httpFactory, HttpMethod.Get, $"{Graph}/{Version}/{path}?{query}&access_token={Uri.EscapeDataString(token)}", null, ct);

    private static List<Media> ParseMedia(JsonElement parent)
    {
        var list = new List<Media>();
        var data = parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty("data", out var d) ? d : parent;
        if (data.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in data.EnumerateArray())
            list.Add(new(Str(m, "id") ?? "", Str(m, "caption"), Int(m, "like_count"), Int(m, "comments_count"),
                Str(m, "media_product_type") is "REELS" ? "REELS" : Str(m, "media_type") ?? "", Str(m, "permalink"), Date(m, "timestamp")));
        return list;
    }

    public async Task<Profile> ProfileAsync(string ownIgId, string token, string username, CancellationToken ct = default)
    {
        var fields = $"business_discovery.username({username}){{username,name,biography,followers_count,media_count,profile_picture_url," +
                     "media.limit(12){id,caption,like_count,comments_count,media_type,media_product_type,permalink,timestamp}}";
        var j = await Get(ownIgId, $"fields={Uri.EscapeDataString(fields)}", token, ct);
        if (!j.TryGetProperty("business_discovery", out var b)) throw new InstagramApiException("Instagram nie zwrócił danych konta.");
        return new Profile(Str(b, "username") ?? username, Str(b, "name"), Str(b, "biography"), Int(b, "followers_count"), Int(b, "media_count"),
            Str(b, "profile_picture_url"), b.TryGetProperty("media", out var media) ? ParseMedia(media) : []);
    }

    public async Task<string> HashtagIdAsync(string ownIgId, string token, string tag, CancellationToken ct = default)
    {
        var j = await Get("ig_hashtag_search", $"user_id={Uri.EscapeDataString(ownIgId)}&q={Uri.EscapeDataString(tag)}", token, ct);
        return j.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array && d.GetArrayLength() > 0 && Str(d[0], "id") is { } id
            ? id : throw new InstagramApiException($"Instagram nie zna hasztagu #{tag}.");
    }

    /// <summary>top_media albo recent_media hasztagu (bez autorów — API ich nie podaje).</summary>
    public async Task<List<Media>> HashtagMediaAsync(string hashtagId, string ownIgId, string token, bool recent, CancellationToken ct = default)
    {
        var j = await Get($"{hashtagId}/{(recent ? "recent_media" : "top_media")}",
            $"user_id={Uri.EscapeDataString(ownIgId)}&fields=id,caption,like_count,comments_count,media_type,permalink,timestamp&limit=25", token, ct);
        return ParseMedia(j);
    }
}
