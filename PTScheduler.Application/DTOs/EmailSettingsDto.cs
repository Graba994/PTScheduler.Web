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
    public string? ReplyTo { get; set; }

    // Tylko do odczytu — stan własnej skrzynki (zapis w IEmailSettingsService.RecordOwnResultAsync).
    public DateTime? LastOkAt { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    public int FallbackCount { get; set; }
    public bool OwnFailing(DateTime utcNow) => Domain.Entities.EmailSettings.IsFailing(LastErrorAt, LastOkAt, utcNow);
}
