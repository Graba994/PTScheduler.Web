namespace PTScheduler.Application.Interfaces;

public record SmsResult(bool Success, bool QuotaExceeded, string? Error);
public record CentralizedSmsStatus(bool PlatformSmsEnabled, decimal SmsCredits);

public interface ISmsService
{
    Task<bool> IsEnabledAsync();

    Task<(bool Success, string? Error)> TestAsync(string phone);

    Task<SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth);

    Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth);

    Task<CentralizedSmsStatus?> GetCentralizedStatusAsync();
}
