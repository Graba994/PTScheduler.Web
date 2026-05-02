namespace PTScheduler.Application.DTOs;

public class EmailSettingsDto
{
    public bool IsEnabled { get; set; }
    public string Provider { get; set; } = "Custom";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "PTScheduler";
}
