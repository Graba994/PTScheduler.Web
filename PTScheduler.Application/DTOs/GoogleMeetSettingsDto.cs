namespace PTScheduler.Application.DTOs;

public class GoogleMeetSettingsDto
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }
    public bool Enabled { get; set; }

    // Jednorazowy parametr „state” trwającej autoryzacji OAuth (ochrona przed
    // podpięciem cudzego konta Google) oraz adres powrotu użyty w żądaniu.
    public string? PendingOAuthState { get; set; }
    public DateTime? PendingOAuthStateExpiresUtc { get; set; }
    public string? PendingRedirectUri { get; set; }
}
