namespace PTScheduler.Application.Interfaces;

public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody);
    Task<(bool Success, string? Error)> TestAsync(string testAddress);
    Task<bool> IsEnabledAsync();
}
