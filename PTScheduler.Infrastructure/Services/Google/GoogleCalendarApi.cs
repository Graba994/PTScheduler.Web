using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PTScheduler.Infrastructure.Services.Google;

/// <summary>Wydarzenie z kalendarza Google — tylko to, czego potrzeba do synchronizacji.</summary>
public sealed record GoogleEvent(
    string Id,
    string? Status,
    string? Transparency,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? App,
    int? SessionId);

/// <summary>Treść wydarzenia wizyty wysyłana do Google.</summary>
public sealed record GoogleEventWrite(string Summary, string Description, DateTime StartUtc, DateTime EndUtc, string App, int SessionId);

public sealed class GoogleApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Wąski klient Calendar API v3 (kalendarz główny użytkownika).</summary>
public interface IGoogleCalendarApi
{
    Task<List<GoogleEvent>> ListAsync(string accessToken, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<string> InsertAsync(string accessToken, GoogleEventWrite e, CancellationToken ct = default);
    /// <summary>False, gdy wydarzenie nie istnieje (usunięte na stałe) — trzeba utworzyć nowe.</summary>
    Task<bool> PatchAsync(string accessToken, string eventId, GoogleEventWrite e, CancellationToken ct = default);
    Task DeleteAsync(string accessToken, string eventId, CancellationToken ct = default);
}

public sealed class GoogleCalendarApi(IHttpClientFactory httpFactory) : IGoogleCalendarApi
{
    private const string Base = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    public const string AppKey = "ptApp";
    public const string SessionKey = "ptSession";

    /// <summary>
    /// Znacznik instancji w wydarzeniach — dwie aplikacje trenerów zapisujące do tego
    /// samego konta Google nie usuną sobie nawzajem wydarzeń.
    /// </summary>
    public static string InstanceKey => PlatformConnection.Slug is { Length: > 0 } slug ? slug : "ptscheduler";

    public async Task<List<GoogleEvent>> ListAsync(string accessToken, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var result = new List<GoogleEvent>();
        string? page = null;
        var guard = 0;
        do
        {
            var url = $"{Base}?singleEvents=true&showDeleted=false&maxResults=2500"
                + $"&timeMin={Uri.EscapeDataString(Rfc3339(fromUtc))}&timeMax={Uri.EscapeDataString(Rfc3339(toUtc))}"
                + "&fields=" + Uri.EscapeDataString("items(id,status,transparency,start,end,extendedProperties/private),nextPageToken")
                + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            using var doc = await SendAsync(accessToken, HttpMethod.Get, url, null, ct);
            var root = doc!.RootElement;
            if (root.TryGetProperty("items", out var items))
                foreach (var it in items.EnumerateArray())
                    result.Add(Parse(it));
            page = root.TryGetProperty("nextPageToken", out var p) ? p.GetString() : null;
        } while (page is not null && ++guard < 20);
        return result;
    }

    public async Task<string> InsertAsync(string accessToken, GoogleEventWrite e, CancellationToken ct = default)
    {
        using var doc = await SendAsync(accessToken, HttpMethod.Post, Base, Body(e), ct);
        return doc!.RootElement.GetProperty("id").GetString()!;
    }

    public async Task<bool> PatchAsync(string accessToken, string eventId, GoogleEventWrite e, CancellationToken ct = default)
    {
        try
        {
            using var _ = await SendAsync(accessToken, HttpMethod.Patch, $"{Base}/{Uri.EscapeDataString(eventId)}", Body(e), ct);
            return true;
        }
        catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string accessToken, string eventId, CancellationToken ct = default)
    {
        try { using var _ = await SendAsync(accessToken, HttpMethod.Delete, $"{Base}/{Uri.EscapeDataString(eventId)}", null, ct); }
        catch (GoogleApiException ex) when (ex.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone) { }
    }

    private static object Body(GoogleEventWrite e) => new Dictionary<string, object>
    {
        ["summary"] = e.Summary,
        ["description"] = e.Description,
        ["start"] = new { dateTime = Rfc3339(e.StartUtc) },
        ["end"] = new { dateTime = Rfc3339(e.EndUtc) },
        // Przywraca wydarzenie, jeśli trener usunął je ręcznie, a wizyta nadal jest aktualna.
        ["status"] = "confirmed",
        ["transparency"] = "opaque",
        ["extendedProperties"] = new { @private = new Dictionary<string, string> { [AppKey] = e.App, [SessionKey] = e.SessionId.ToString(CultureInfo.InvariantCulture) } }
    };

    private static string Rfc3339(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static GoogleEvent Parse(JsonElement it)
    {
        static (DateTimeOffset? At, DateOnly? Date) When(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var w)) return (null, null);
            if (w.TryGetProperty("dateTime", out var dt) && DateTimeOffset.TryParse(dt.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                return (at, null);
            if (w.TryGetProperty("date", out var d) && DateOnly.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return (null, date);
            return (null, null);
        }

        var (start, startDate) = When(it, "start");
        var (end, endDate) = When(it, "end");
        string? app = null;
        int? sessionId = null;
        if (it.TryGetProperty("extendedProperties", out var ext) && ext.TryGetProperty("private", out var priv))
        {
            if (priv.TryGetProperty(AppKey, out var a)) app = a.GetString();
            if (priv.TryGetProperty(SessionKey, out var s) && int.TryParse(s.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid)) sessionId = sid;
        }
        return new GoogleEvent(
            it.GetProperty("id").GetString()!,
            it.TryGetProperty("status", out var st) ? st.GetString() : null,
            it.TryGetProperty("transparency", out var tr) ? tr.GetString() : null,
            start, end, startDate, endDate, app, sessionId);
    }

    private async Task<JsonDocument?> SendAsync(string accessToken, HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new GoogleApiException(resp.StatusCode, Describe(resp.StatusCode, text));
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }

    private static string Describe(HttpStatusCode status, string body)
    {
        var msg = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m)) msg = m.GetString() ?? "";
        }
        catch { /* odpowiedź nie-JSON */ }
        return status switch
        {
            HttpStatusCode.Unauthorized => "Google odrzucił token dostępu. Jeśli błąd się powtarza, połącz konto ponownie.",
            HttpStatusCode.Forbidden when msg.Contains("API has not been used", StringComparison.OrdinalIgnoreCase)
                => "Google Calendar API nie jest włączone w projekcie Google Cloud.",
            HttpStatusCode.Forbidden => "Brak uprawnień do kalendarza (" + msg + ").",
            HttpStatusCode.TooManyRequests => "Google chwilowo ogranicza liczbę zapytań — spróbujemy ponownie.",
            _ => $"Błąd Google Calendar ({(int)status}){(msg.Length > 0 ? ": " + msg : "")}."
        };
    }
}
