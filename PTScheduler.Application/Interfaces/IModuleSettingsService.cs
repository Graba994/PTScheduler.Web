using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IModuleSettingsService
{
    Task<ModuleSettingsDto> GetAsync();
    Task SaveAsync(ModuleSettingsDto dto);
}
