using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.Interfaces;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services.Photos;

namespace PTScheduler.Web.Services;

/// <summary>
/// Zdjęcia i filmy własnych ćwiczeń trenera. Zdjęcie zmniejszamy tutaj (WebP, do 1600 px, bez EXIF/GPS) i wysyłamy
/// na platformę (Bunny Storage, ten sam limit co filmy); bez platformy zapisujemy je na serwerze aplikacji.
/// Plik usuwamy dopiero, gdy nie używa go już żadne inne ćwiczenie ani lekcja (np. po „Skopiuj jako moje”).
/// </summary>
public class ExerciseMediaService(
    IBunnyService bunny,
    IWebRootPathProvider webRoot,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<ExerciseMediaService> logger)
{
    public const int MaxEdge = 1600;
    public const int Quality = 82;
    /// <summary>Plik z telefonu przed zmniejszeniem — większy to raczej nie zdjęcie.</summary>
    public const long MaxSourceBytes = 25L * 1024 * 1024;
    public const int MaxImages = 8;
    private const string LocalPrefix = "/branding/exercises/";

    public async Task<(string? Url, long Size, string? Error)> UploadImageAsync(Stream source, CancellationToken ct = default)
    {
        byte[] data;
        try
        {
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            data = PhotoCompressor.Compress(buffer.ToArray(), MaxEdge, Quality).Data;
        }
        catch (InvalidDataException) { return (null, 0, "Tego pliku nie da się odczytać jako zdjęcia — użyj JPG, PNG albo WebP."); }

        var status = await bunny.GetStatusAsync();
        if (status.ImagesEnabled)
        {
            var (url, error) = await bunny.UploadImageAsync(VideoKinds.Exercises, data, ct);
            return (url, url is null ? 0 : data.Length, error);
        }

        var dir = Path.Combine(webRoot.WebRootPath, "branding", "exercises");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}.webp";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), data, ct);
        return (LocalPrefix + name, data.Length, null);
    }

    /// <summary>Usuwa zdjęcie, jeśli to nasz plik i nikt inny go nie używa. Obce adresy (wklejone linki) zostawiamy.</summary>
    public async Task DeleteImageIfUnusedAsync(string url, int? exceptExerciseId = null)
    {
        if (string.IsNullOrWhiteSpace(url) || await ImageUsedAsync(url, exceptExerciseId)) return;
        if (url.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            var name = Path.GetFileName(url);
            if (name.Length == 0 || name != url[LocalPrefix.Length..]) return;
            var path = Path.Combine(webRoot.WebRootPath, "branding", "exercises", name);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException ex) { logger.LogWarning(ex, "Nie usunięto zdjęcia ćwiczenia {Name}.", name); }
            return;
        }
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) await bunny.DeleteImageAsync(url);
    }

    /// <summary>Usuwa film z platformy, jeśli żadne inne ćwiczenie ani lekcja go nie osadza.</summary>
    public async Task DeleteVideoIfUnusedAsync(string? videoRef, int? exceptExerciseId = null)
    {
        if (string.IsNullOrWhiteSpace(videoRef) || !videoRef.Contains('/')) return;
        if (await VideoUsedAsync(videoRef, exceptExerciseId)) return;
        await bunny.DeleteAsync(videoRef);
    }

    public async Task<bool> ImageUsedAsync(string url, int? exceptExerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Exercises.AnyAsync(e => e.Id != (exceptExerciseId ?? 0) && e.ImageUrls.Contains(url));
    }

    public async Task<bool> VideoUsedAsync(string videoRef, int? exceptExerciseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var guid = VideoRef.Guid(videoRef);
        return await db.Exercises.AnyAsync(e => e.Id != (exceptExerciseId ?? 0) && e.VideoRef != null && e.VideoRef.EndsWith(guid))
            || await db.Lessons.AnyAsync(l => l.BunnyVideoId != null && l.BunnyVideoId.EndsWith(guid));
    }

    /// <summary>„1,2 GB” / „350 MB” — do licznika miejsca w oknie ćwiczenia.</summary>
    public static string Size(long bytes) => bytes == long.MaxValue ? "bez limitu"
        : bytes <= 0 ? "0 MB"
        : bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.#} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";
}
