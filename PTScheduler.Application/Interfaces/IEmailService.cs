namespace PTScheduler.Application.Interfaces;

public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody);
    Task<(bool Success, string? Error)> TestAsync(string testAddress);
    Task<bool> IsEnabledAsync();

    /// <summary>„own” — własny SMTP trenera, „platform” — serwer platformy, „none” — brak wysyłki.</summary>
    Task<string> GetDeliveryModeAsync();

    /// <summary>Dzienny limit i dzisiejsze wykorzystanie poczty platformy; null przy własnym SMTP.</summary>
    Task<(int Limit, int SentToday)?> GetPlatformQuotaAsync();
}
