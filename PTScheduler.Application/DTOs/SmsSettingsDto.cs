namespace PTScheduler.Application.DTOs;

public class SmsSettingsDto
{
    public bool IsEnabled { get; set; }
    public string ApiToken { get; set; } = "";
    public string SenderName { get; set; } = "";
}
