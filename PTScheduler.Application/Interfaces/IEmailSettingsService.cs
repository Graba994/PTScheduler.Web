using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IEmailSettingsService
{
    Task<EmailSettingsDto> GetAsync();
    Task SaveAsync(EmailSettingsDto dto);
}
