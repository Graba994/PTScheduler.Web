using System.Text.Json;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Persists the editable Welcome-page content as a JSON file inside the
/// branding volume (mounted, so it survives container recreation). This
/// avoids a database migration while keeping the content admin-editable.
/// </summary>
public class SiteContentService(IWebRootPathProvider webRoot) : ISiteContentService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string FilePath =>
        Path.Combine(webRoot.WebRootPath, "branding", "site-content.json");

    public async Task<SiteContentDto> GetAsync()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                await using var fs = File.OpenRead(FilePath);
                var dto = await JsonSerializer.DeserializeAsync<SiteContentDto>(fs);
                if (dto is not null) return dto;
            }
        }
        catch { /* Corrupt or unreadable file — fall back to defaults. */ }

        return new SiteContentDto();
    }

    public async Task SaveAsync(SiteContentDto dto)
    {
        var dir = Path.Combine(webRoot.WebRootPath, "branding");
        Directory.CreateDirectory(dir);
        await using var fs = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(fs, dto, JsonOptions);
    }
}
