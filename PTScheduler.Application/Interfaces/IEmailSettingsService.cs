using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IEmailSettingsService
{
    Task<EmailSettingsDto> GetAsync();
    Task SaveAsync(EmailSettingsDto dto);

    /// <summary>Wynik wysyłki z własnej skrzynki: sukces zeruje licznik awaryjnych wysyłek, błąd go zwiększa.</summary>
    Task RecordOwnResultAsync(bool ok, string? error);
}
