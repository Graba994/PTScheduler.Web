namespace PTScheduler.Application.Interfaces;

public record SmsResult(bool Success, bool QuotaExceeded, string? Error);

public interface ISmsService
{
    Task<bool> IsEnabledAsync();

    /// <summary>Sends a test SMS to verify the SMSAPI.pl configuration. Does not count against the monthly quota.</summary>
    Task<(bool Success, string? Error)> TestAsync(string phone);

    /// <summary>Sends a reminder SMS, enforcing the plan's monthly quota (int.MaxValue = unlimited).</summary>
    Task<SmsResult> SendReminderAsync(string phone, string message, int maxPerMonth);

    /// <summary>Current month's usage against the given quota, for display in the admin panel.</summary>
    Task<(int Sent, int Max)> GetQuotaStatusAsync(int maxPerMonth);
}
