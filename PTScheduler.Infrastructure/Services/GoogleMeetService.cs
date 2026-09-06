using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace PTScheduler.Infrastructure.Services;

public class GoogleMeetService(IWebRootPathProvider webRoot, IHttpClientFactory httpFactory, ILogger<GoogleMeetService> logger)
    : IGoogleMeetService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private string FilePath =>
        Path.Combine(webRoot.WebRootPath, "branding", "google-meet-settings.json");

    private GoogleMeetSettingsDto? _cached;

    public bool IsConfigured
    {
        get
        {
            var s = GetSettingsSync();
            return s.Enabled
                && !string.IsNullOrWhiteSpace(s.ClientId)
                && !string.IsNullOrWhiteSpace(s.ClientSecret)
                && !string.IsNullOrWhiteSpace(s.RefreshToken);
        }
    }

    public async Task<GoogleMeetSettingsDto> GetSettingsAsync()
    {
        if (_cached is not null) return _cached;
        try
        {
            if (File.Exists(FilePath))
            {
                await using var fs = File.OpenRead(FilePath);
                var dto = await JsonSerializer.DeserializeAsync<GoogleMeetSettingsDto>(fs, JsonOpts);
                if (dto is not null) { _cached = dto; return dto; }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nie udało się wczytać ustawień Google Meet z {Path} — używam pustych.", FilePath);
        }
        return new GoogleMeetSettingsDto();
    }

    public async Task SaveSettingsAsync(GoogleMeetSettingsDto dto)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        await using var fs = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(fs, dto, JsonOpts);
        _cached = dto;
    }

    public string BuildAuthorizationUrl(string clientId, string redirectUri)
    {
        var scopes = Uri.EscapeDataString("https://www.googleapis.com/auth/calendar.events");
        return $"https://accounts.google.com/o/oauth2/v2/auth"
             + $"?client_id={Uri.EscapeDataString(clientId)}"
             + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
             + $"&response_type=code"
             + $"&scope={scopes}"
             + $"&access_type=offline"
             + $"&prompt=consent";
    }

    public async Task<(bool Ok, string? Error)> ExchangeCodeAsync(string code, string redirectUri)
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            return (false, "Najpierw zapisz Client ID i Client Secret.");

        using var client = httpFactory.CreateClient();
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });

        var resp = await client.PostAsync("https://oauth2.googleapis.com/token", body);
        var json = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return (false, $"Google zwrócił błąd: {resp.StatusCode} — {json}");

        var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts);
        if (string.IsNullOrWhiteSpace(token?.RefreshToken))
            return (false, "Brak refresh_token w odpowiedzi. Spróbuj ponownie z prompt=consent.");

        settings.RefreshToken = token.RefreshToken;
        settings.Enabled = true;
        await SaveSettingsAsync(settings);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> TestConnectionAsync()
    {
        var accessToken = await GetAccessTokenAsync();
        if (accessToken is null) return (false, "Nie udało się uzyskać tokenu dostępu.");

        using var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await client.GetAsync("https://www.googleapis.com/calendar/v3/calendars/primary");
        if (resp.IsSuccessStatusCode) return (true, "Połączenie działa poprawnie.");
        var err = await resp.Content.ReadAsStringAsync();
        return (false, $"Błąd Google Calendar API: {resp.StatusCode} — {err}");
    }

    public async Task<GoogleMeetResult?> CreateMeetingAsync(string summary, string description,
        DateTime startUtc, int durationMinutes, string? attendeeEmail = null)
    {
        var accessToken = await GetAccessTokenAsync();
        if (accessToken is null) return null;

        var endUtc = startUtc.AddMinutes(durationMinutes);

        var eventObj = new Dictionary<string, object>
        {
            ["summary"] = summary,
            ["description"] = description,
            ["start"] = new { dateTime = startUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"), timeZone = "UTC" },
            ["end"] = new { dateTime = endUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"), timeZone = "UTC" },
            ["conferenceData"] = new
            {
                createRequest = new
                {
                    requestId = Guid.NewGuid().ToString(),
                    conferenceSolutionKey = new { type = "hangoutsMeet" }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(attendeeEmail))
        {
            eventObj["attendees"] = new[] { new { email = attendeeEmail } };
        }

        using var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(eventObj, JsonOpts),
            Encoding.UTF8, "application/json");

        var resp = await client.PostAsync(
            "https://www.googleapis.com/calendar/v3/calendars/primary/events?conferenceDataVersion=1",
            jsonContent);

        if (!resp.IsSuccessStatusCode) return null;

        var respJson = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(respJson);
        var root = doc.RootElement;

        var meetUrl = "";
        if (root.TryGetProperty("conferenceData", out var confData)
            && confData.TryGetProperty("entryPoints", out var entryPoints))
        {
            foreach (var ep in entryPoints.EnumerateArray())
            {
                if (ep.TryGetProperty("entryPointType", out var ept)
                    && ept.GetString() == "video"
                    && ep.TryGetProperty("uri", out var uri))
                {
                    meetUrl = uri.GetString() ?? "";
                    break;
                }
            }
        }

        var eventId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";

        return new GoogleMeetResult
        {
            MeetingUrl = meetUrl,
            CalendarEventId = eventId
        };
    }

    public async Task DeleteMeetingAsync(string calendarEventId)
    {
        if (string.IsNullOrWhiteSpace(calendarEventId)) return;
        var accessToken = await GetAccessTokenAsync();
        if (accessToken is null) return;

        using var client = httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await client.DeleteAsync(
            $"https://www.googleapis.com/calendar/v3/calendars/primary/events/{calendarEventId}");
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.ClientId)
            || string.IsNullOrWhiteSpace(settings.ClientSecret)
            || string.IsNullOrWhiteSpace(settings.RefreshToken))
            return null;

        using var client = httpFactory.CreateClient();
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["refresh_token"] = settings.RefreshToken,
            ["grant_type"] = "refresh_token"
        });

        var resp = await client.PostAsync("https://oauth2.googleapis.com/token", body);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts);
        return token?.AccessToken;
    }

    private GoogleMeetSettingsDto GetSettingsSync()
    {
        if (_cached is not null) return _cached;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var dto = JsonSerializer.Deserialize<GoogleMeetSettingsDto>(json, JsonOpts);
                if (dto is not null) { _cached = dto; return dto; }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nie udało się wczytać ustawień Google Meet z {Path} — używam pustych.", FilePath);
        }
        return new GoogleMeetSettingsDto();
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
