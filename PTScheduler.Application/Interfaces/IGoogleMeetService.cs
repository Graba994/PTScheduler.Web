using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IGoogleMeetService
{
    Task<GoogleMeetSettingsDto> GetSettingsAsync();
    Task SaveSettingsAsync(GoogleMeetSettingsDto dto);

    /// <summary>Generuje jednorazowy state, zapisuje go i zwraca adres zgody Google.</summary>
    Task<string> StartAuthorizationAsync(string redirectUri);
    Task<(bool Ok, string? Error)> ExchangeCodeAsync(string code, string state);

    Task<(bool Ok, string? Error)> TestConnectionAsync();

    /// <summary>
    /// Tworzy wydarzenie z linkiem Meet. Konto: własne z ustawień tej instalacji,
    /// a w instancji platformy — kalendarz trenera połączony przez „Kalendarz Google”.
    /// </summary>
    Task<GoogleMeetResult?> CreateMeetingAsync(string summary, string description,
        DateTime startUtc, int durationMinutes, string? attendeeEmail = null,
        string? trainerUserId = null, int? sessionId = null);

    Task DeleteMeetingAsync(string calendarEventId, string? trainerUserId = null);

    /// <summary>Czy nowe wizyty tego trenera mają dostawać link Meet.</summary>
    Task<bool> CanCreateMeetingsAsync(string trainerUserId);

    /// <summary>Token konta Google z ustawień tej instalacji (tryb samodzielny); null gdy nieskonfigurowane.</summary>
    Task<string?> GetOwnAccessTokenAsync();

    /// <summary>Własny klient OAuth (Client ID/Secret + autoryzacja) w ustawieniach tej instalacji.</summary>
    bool IsConfigured { get; }
}

public class GoogleMeetResult
{
    public string MeetingUrl { get; set; } = "";
    public string CalendarEventId { get; set; } = "";
}
