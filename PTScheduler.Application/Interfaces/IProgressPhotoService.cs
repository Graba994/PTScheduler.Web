using PTScheduler.Application.Photos;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Interfaces;

public interface IProgressPhotoService
{
    /// <summary>Zdjęcia widzi wyłącznie klient, jego trener i admin studia.</summary>
    Task<bool> CanAccessAsync(int clientId, string userId, bool isAdmin);

    Task<List<ProgressPhotoDto>> GetAsync(int clientId);

    /// <summary>
    /// Kompresuje zdjęcie (WebP, dłuższy bok ≤ 1600 px, bez metadanych EXIF/GPS),
    /// zapisuje je z miniaturą w prywatnym katalogu i tworzy wpis.
    /// </summary>
    Task<(bool Ok, string? Error)> UploadAsync(int clientId, string uploadedByUserId, Stream content,
        DateOnly takenOn, PhotoPose pose, string? note);

    Task<bool> DeleteAsync(int photoId, int clientId);

    /// <summary>Ścieżka pliku do wysłania oraz klient, do którego należy zdjęcie.</summary>
    Task<(string Path, int ClientId)?> GetFileAsync(int photoId, bool thumbnail);

    /// <summary>Usuwa wszystkie zdjęcia klienta (pliki i wpisy) — np. przy usuwaniu konta.</summary>
    Task DeleteAllForClientAsync(int clientId);
}
