namespace PTScheduler.Application.Interfaces;

public record SmsResult(bool Success, bool QuotaExceeded, string? Error);
/// <summary>SMS z Portalu: czy platforma wysyła, kredyty (nie wygasają) oraz miesięczny limit planu i jego zużycie.</summary>
public record CentralizedSmsStatus(bool PlatformSmsEnabled, decimal SmsCredits, int MonthlyLimit = 0, int MonthlyUsed = 0)
{
    public bool Unlimited => MonthlyLimit == int.MaxValue;
    public int MonthlyLeft => Unlimited ? int.MaxValue : Math.Max(0, MonthlyLimit - MonthlyUsed);
    public bool CanSend => PlatformSmsEnabled && (MonthlyLeft > 0 || SmsCredits >= 1);
}

public interface ISmsService
{
    Task<bool> IsEnabledAsync();

    Task<(bool Success, string? Error)> TestAsync(string phone);

    Task<SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth);

    Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth);

    Task<CentralizedSmsStatus?> GetCentralizedStatusAsync();
}
