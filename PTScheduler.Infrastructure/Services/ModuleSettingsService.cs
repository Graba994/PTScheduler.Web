using System.Text.Json;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Persists module on/off settings as a JSON file in the branding volume,
/// mirroring SiteContentService — no database migration required.
/// </summary>
public class ModuleSettingsService(IWebRootPathProvider webRoot) : IModuleSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string FilePath =>
        Path.Combine(webRoot.WebRootPath, "branding", "module-settings.json");

    public async Task<ModuleSettingsDto> GetAsync()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                await using var fs = File.OpenRead(FilePath);
                var dto = await JsonSerializer.DeserializeAsync<ModuleSettingsDto>(fs);
                if (dto is not null) return dto;
            }
        }
        catch { /* Corrupt or unreadable — fall back to defaults. */ }

        return new ModuleSettingsDto();
    }

    public async Task SaveAsync(ModuleSettingsDto dto)
    {
        var dir = Path.Combine(webRoot.WebRootPath, "branding");
        Directory.CreateDirectory(dir);
        await using var fs = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(fs, dto, JsonOptions);
    }
}
