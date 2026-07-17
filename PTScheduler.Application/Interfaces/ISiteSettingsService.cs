using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISiteSettingsService
{
    // Always returns the singleton settings (created with defaults on first call).
    Task<SiteSettingsDto> GetAsync();
    Task SaveAsync(SiteSettingsDto dto);
}
