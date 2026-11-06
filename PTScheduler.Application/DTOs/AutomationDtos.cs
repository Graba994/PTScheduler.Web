namespace PTScheduler.Application.DTOs;

/// <summary>Reguła automatycznej wiadomości do edycji w panelu trenera.</summary>
public sealed class AutomationRuleDto
{
    public string Kind { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int DelayDays { get; set; }
    public int CooldownDays { get; set; }
    public bool ViaEmail { get; set; }
    public bool ViaPush { get; set; }
    public bool ViaSms { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ButtonText { get; set; } = string.Empty;
    public int CouponPercent { get; set; }
    public int CouponValidDays { get; set; }
}

/// <summary>Wysłana wiadomość w historii (z informacją, czy klient wrócił).</summary>
public sealed record AutomationLogDto(int Id, string Kind, int ClientId, string ClientName, DateTime SentAtUtc,
    string Channels, string? CouponCode, DateTime? ReturnedAtUtc);

/// <summary>Skuteczność reguły z ostatnich 90 dni.</summary>
public sealed record AutomationStatsDto(string Kind, int Sent, int Returned);

/// <summary>Czym aplikacja może dziś wysyłać (zależy od planu i konfiguracji — ustala warstwa Web).</summary>
public sealed record AutomationChannels(bool Email, bool Push, bool Sms, int MaxSmsPerMonth, string AppBaseUrl);
