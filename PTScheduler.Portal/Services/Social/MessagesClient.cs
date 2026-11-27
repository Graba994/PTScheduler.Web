using System.Text.Json;
using static PTScheduler.Portal.Services.Social.MetaApi;

namespace PTScheduler.Portal.Services.Social;

/// <summary>
/// Wiadomości prywatne przez API Meta: Messenger strony i Instagram (przez stronę albo przez logowanie Instagramem).
/// Rozmowy pobieramy co kilka minut (bez webhooków), odpowiadamy w oknie 24 h od ostatniej wiadomości klienta
/// (zasada Meta). „Prywatna odpowiedź” na komentarz: jedna wiadomość do autora komentarza (Instagram: do 7 dni).
/// </summary>
public class MessagesClient(IHttpClientFactory httpFactory, IConfiguration? config = null)
{
    public const string Version = "v24.0";

    /// <summary>Skąd i czym czytamy rozmowy: Messenger strony, Instagram przez stronę albo Instagram (logowanie Instagramem).</summary>
    public sealed record Box(string Platform, string Host, string Owner, string OwnId, string Token)
    {
        /// <summary>Messenger strony: /{page-id}/conversations?platform=messenger.</summary>
        public static Box Messenger(string graph, string pageId, string token) => new("facebook", graph, pageId, pageId, token);
        /// <summary>Instagram przez stronę: /{page-id}/conversations?platform=instagram, wysyłka przez stronę.</summary>
        public static Box InstagramViaPage(string graph, string pageId, string igId, string token) => new("instagram", graph, pageId, igId, token);
        /// <summary>Instagram (logowanie Instagramem): graph.instagram.com/me/conversations.</summary>
        public static Box InstagramLogin(string graph, string igId, string token) => new("instagram", graph, igId, igId, token);
        public string ApiPlatform => Platform == "facebook" ? "messenger" : "instagram";
    }

    public sealed record Message(string Id, string FromId, string Text, DateTime At, bool HasAttachment);
    public sealed record Conversation(string Id, string ParticipantId, string ParticipantName, DateTime UpdatedAt, List<Message> Messages);

    public string FacebookGraph => (config?["Social:FacebookGraphUrl"] is { Length: > 0 } g ? g : "https://graph.facebook.com").TrimEnd('/');
    public string InstagramGraph => (config?["Instagram:GraphUrl"] is { Length: > 0 } g ? g : "https://graph.instagram.com").TrimEnd('/');

    /// <summary>Ostatnie rozmowy z ostatnimi wiadomościami (od najnowszej).</summary>
    public async Task<List<Conversation>> ConversationsAsync(Box box, CancellationToken ct = default)
    {
        var fields = "id,updated_time,participants,messages.limit(20){id,message,created_time,from,attachments}";
        var url = $"{box.Host}/{Version}/{box.Owner}/conversations?platform={box.ApiPlatform}&fields={Uri.EscapeDataString(fields)}&limit=25&access_token={Uri.EscapeDataString(box.Token)}";
        var j = await MetaApi.SendAsync(httpFactory, HttpMethod.Get, url, null, ct);
        var list = new List<Conversation>();
        if (!j.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        foreach (var c in data.EnumerateArray())
        {
            var (pid, pname) = Other(c, box.OwnId);
            var messages = new List<Message>();
            if (c.TryGetProperty("messages", out var m) && m.TryGetProperty("data", out var md) && md.ValueKind == JsonValueKind.Array)
                foreach (var x in md.EnumerateArray())
                {
                    var from = x.TryGetProperty("from", out var f) ? Str(f, "id") ?? "" : "";
                    if (pid.Length == 0 && from.Length > 0 && from != box.OwnId)
                    {
                        pid = from;
                        pname = Str(f, "username") ?? Str(f, "name") ?? pname;
                    }
                    messages.Add(new(Str(x, "id") ?? "", from, Str(x, "message") ?? "", Date(x, "created_time"), x.TryGetProperty("attachments", out _)));
                }
            if (pid.Length == 0) continue;
            list.Add(new(Str(c, "id") ?? "", pid, pname, Date(c, "updated_time"), messages.OrderBy(x => x.At).ToList()));
        }
        return list;
    }

    /// <summary>Druga strona rozmowy (nie nasza strona / konto).</summary>
    private static (string Id, string Name) Other(JsonElement c, string ownId)
    {
        if (c.TryGetProperty("participants", out var p) && p.TryGetProperty("data", out var pd) && pd.ValueKind == JsonValueKind.Array)
            foreach (var x in pd.EnumerateArray())
                if (Str(x, "id") is { Length: > 0 } id && id != ownId)
                    return (id, Str(x, "username") ?? Str(x, "name") ?? "Bez nazwy");
        return ("", "Bez nazwy");
    }

    /// <summary>Wiadomość do osoby (w oknie 24 h od jej ostatniej wiadomości).</summary>
    public async Task<string?> SendAsync(Box box, string recipientId, string text, CancellationToken ct = default) =>
        Str(await MetaApi.SendAsync(httpFactory, HttpMethod.Post, $"{box.Host}/{Version}/{box.Owner}/messages", new()
        {
            ["recipient"] = JsonSerializer.Serialize(new { id = recipientId }),
            ["message"] = JsonSerializer.Serialize(new { text }),
            ["messaging_type"] = "RESPONSE",
            ["access_token"] = box.Token
        }, ct), "message_id");

    /// <summary>Prywatna odpowiedź na komentarz — wiadomość trafia do autora komentarza (jedna na komentarz).</summary>
    public async Task<string?> PrivateReplyAsync(Box box, string commentId, string text, CancellationToken ct = default) =>
        Str(await MetaApi.SendAsync(httpFactory, HttpMethod.Post, $"{box.Host}/{Version}/{box.Owner}/messages", new()
        {
            ["recipient"] = JsonSerializer.Serialize(new { comment_id = commentId }),
            ["message"] = JsonSerializer.Serialize(new { text }),
            ["access_token"] = box.Token
        }, ct), "message_id");
}
