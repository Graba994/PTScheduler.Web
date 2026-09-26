using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISiteContentService
{
    Task<SiteContentDto> GetAsync();
    Task SaveAsync(SiteContentDto dto);
}
