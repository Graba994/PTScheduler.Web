using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISmsSettingsService
{
    Task<SmsSettingsDto> GetAsync();
    Task SaveAsync(SmsSettingsDto dto);
}
