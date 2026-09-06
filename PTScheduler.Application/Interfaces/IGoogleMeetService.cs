using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IGoogleMeetService
{
    Task<GoogleMeetSettingsDto> GetSettingsAsync();
    Task SaveSettingsAsync(GoogleMeetSettingsDto dto);

    string BuildAuthorizationUrl(string clientId, string redirectUri);
    Task<(bool Ok, string? Error)> ExchangeCodeAsync(string code, string redirectUri);

    Task<(bool Ok, string? Error)> TestConnectionAsync();

    Task<GoogleMeetResult?> CreateMeetingAsync(string summary, string description,
        DateTime startUtc, int durationMinutes, string? attendeeEmail = null);

    Task DeleteMeetingAsync(string calendarEventId);

    bool IsConfigured { get; }
}

public class GoogleMeetResult
{
    public string MeetingUrl { get; set; } = "";
    public string CalendarEventId { get; set; } = "";
}
