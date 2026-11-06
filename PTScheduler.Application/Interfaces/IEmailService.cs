namespace PTScheduler.Application.Interfaces;

/// <summary>Obrazek w środku wiadomości (w HTML: <c>src="cid:{ContentId}"</c>) — widać go bez pobierania z sieci.</summary>
public sealed record EmailInlineImage(string ContentId, byte[] Data, string ContentType);

public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody);

    /// <summary>Wysyłka z obrazkami dołączonymi do wiadomości (np. maskotka w porannym briefie).</summary>
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody, IReadOnlyList<EmailInlineImage>? images);
    Task<(bool Success, string? Error)> TestAsync(string testAddress);

    /// <summary>Test własnej skrzynki z danymi z formularza — bez zapisywania i bez awaryjnej wysyłki przez platformę.</summary>
    Task<(bool Success, string? Error)> TestOwnAsync(Application.DTOs.EmailSettingsDto settings, string testAddress);
    Task<bool> IsEnabledAsync();

    /// <summary>„own” — własny SMTP trenera, „platform” — serwer platformy, „none” — brak wysyłki.</summary>
    Task<string> GetDeliveryModeAsync();

    /// <summary>Dzienny limit i dzisiejsze wykorzystanie poczty platformy; null przy własnym SMTP.</summary>
    Task<(int Limit, int SentToday)?> GetPlatformQuotaAsync();
}
