using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IBrandingService
{
    Task<AppBrandingDto> GetAsync();
    Task SaveAsync(SaveBrandingDto dto);
    Task<string> UploadLogoAsync(Stream stream, string fileName);
    Task<string> UploadFaviconAsync(Stream stream, string fileName);
    Task DeleteLogoAsync();
    Task DeleteFaviconAsync();
}
