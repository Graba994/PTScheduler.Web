namespace PTScheduler.Application.DTOs;

public class GoogleMeetSettingsDto
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }
    public bool Enabled { get; set; }
}
