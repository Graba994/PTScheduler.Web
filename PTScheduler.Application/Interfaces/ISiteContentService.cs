using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISiteContentService
{
    Task<SiteContentDto> GetAsync();
    Task SaveAsync(SiteContentDto dto);
    /// <summary>Szkic z kreatora (podgląd na żywo); gdy go nie ma — opublikowana wersja.</summary>
    Task<SiteContentDto> GetDraftAsync();
    Task SaveDraftAsync(SiteContentDto dto);
    /// <summary>Czy szkic różni się od opublikowanej strony.</summary>
    Task<bool> HasUnpublishedDraftAsync();
    /// <summary>Publikuje szkic (staje się stroną główną).</summary>
    Task PublishDraftAsync();
    Task DiscardDraftAsync();
    /// <summary>Zdjęcie do strony głównej: zmniejszone do WebP (bez metadanych). Zwraca adres pliku.</summary>
    Task<string> UploadImageAsync(Stream stream, string fileName);
}
