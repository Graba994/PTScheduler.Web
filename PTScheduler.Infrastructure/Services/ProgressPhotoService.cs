using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTScheduler.Application.Interfaces;
using PTScheduler.Application.Photos;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services.Photos;

namespace PTScheduler.Infrastructure.Services;

public class ProgressPhotoService(
    IDbContextFactory<ApplicationDbContext> dbFactory,
    IWebRootPathProvider webRoot,
    IAppClock clock,
    ILogger<ProgressPhotoService> logger) : IProgressPhotoService
{
    public const long MaxUploadBytes = 20 * 1024 * 1024;
    public const int MaxPhotosPerClient = 300;
    public const int FullEdge = 1600, FullQuality = 80;
    public const int ThumbEdge = 480, ThumbQuality = 70;

    private string ClientDir(int clientId) =>
        Path.Combine(PrivateStorage.Root(webRoot), "photos", clientId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string ThumbName(string fileName) => Path.GetFileNameWithoutExtension(fileName) + "_t.webp";

    public async Task<bool> CanAccessAsync(int clientId, string userId, bool isAdmin)
    {
        if (isAdmin) return true;
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = dbFactory.CreateDbContext();
        return await db.Clients.AnyAsync(c => c.Id == clientId
            && (c.ApplicationUserId == userId || c.TrainerUserId == userId));
    }

    public async Task<List<ProgressPhotoDto>> GetAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var clientUserId = await db.Clients.Where(c => c.Id == clientId).Select(c => c.ApplicationUserId).FirstOrDefaultAsync();
        return await db.ProgressPhotos.AsNoTracking()
            .Where(p => p.ClientId == clientId)
            .OrderByDescending(p => p.TakenOn).ThenBy(p => p.Pose).ThenByDescending(p => p.Id)
            .Select(p => new ProgressPhotoDto
            {
                Id = p.Id, ClientId = p.ClientId, TakenOn = p.TakenOn, Pose = p.Pose, Note = p.Note,
                Width = p.Width, Height = p.Height, SizeBytes = p.SizeBytes,
                UploadedByClient = p.UploadedByUserId == clientUserId, UploadedAt = p.UploadedAt
            })
            .ToListAsync();
    }

    public async Task<(bool Ok, string? Error)> UploadAsync(int clientId, string uploadedByUserId, Stream content,
        DateOnly takenOn, PhotoPose pose, string? note)
    {
        await using var db = dbFactory.CreateDbContext();
        if (!await db.Clients.AnyAsync(c => c.Id == clientId)) return (false, "Nie znaleziono klienta.");
        if (await db.ProgressPhotos.CountAsync(p => p.ClientId == clientId) >= MaxPhotosPerClient)
            return (false, $"Osiągnięto limit {MaxPhotosPerClient} zdjęć — usuń starsze, aby dodać nowe.");
        if (takenOn > clock.Today.AddDays(1)) return (false, "Data zdjęcia nie może być w przyszłości.");

        byte[] raw;
        using (var ms = new MemoryStream())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer)) > 0)
            {
                if (ms.Length + read > MaxUploadBytes) return (false, "Plik jest za duży (maks. 20 MB).");
                ms.Write(buffer, 0, read);
            }
            raw = ms.ToArray();
        }

        PhotoCompressor.Result full, thumb;
        try
        {
            full = PhotoCompressor.Compress(raw, FullEdge, FullQuality);
            thumb = PhotoCompressor.Compress(raw, ThumbEdge, ThumbQuality);
        }
        catch (InvalidDataException)
        {
            return (false, "Nie rozpoznano zdjęcia. Wyślij plik JPG, PNG lub WebP.");
        }

        var dir = ClientDir(clientId);
        Directory.CreateDirectory(dir);
        var fileName = Guid.NewGuid().ToString("N") + ".webp";
        await File.WriteAllBytesAsync(Path.Combine(dir, fileName), full.Data);
        await File.WriteAllBytesAsync(Path.Combine(dir, ThumbName(fileName)), thumb.Data);

        db.ProgressPhotos.Add(new ProgressPhoto
        {
            ClientId = clientId, TakenOn = takenOn, Pose = pose,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)],
            FileName = fileName, Width = full.Width, Height = full.Height, SizeBytes = full.Data.Length,
            UploadedByUserId = uploadedByUserId, UploadedAt = clock.UtcNow
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Progress photo for client {ClientId}: {In} KB → {Out} KB.",
            clientId, raw.Length / 1024, full.Data.Length / 1024);
        return (true, null);
    }

    public async Task<bool> DeleteAsync(int photoId, int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var photo = await db.ProgressPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.ClientId == clientId);
        if (photo is null) return false;
        db.ProgressPhotos.Remove(photo);
        await db.SaveChangesAsync();
        DeleteFiles(clientId, photo.FileName);
        return true;
    }

    public async Task<(string Path, int ClientId)?> GetFileAsync(int photoId, bool thumbnail)
    {
        await using var db = dbFactory.CreateDbContext();
        var photo = await db.ProgressPhotos.AsNoTracking()
            .Where(p => p.Id == photoId).Select(p => new { p.ClientId, p.FileName }).FirstOrDefaultAsync();
        if (photo is null) return null;
        var path = Path.Combine(ClientDir(photo.ClientId), thumbnail ? ThumbName(photo.FileName) : photo.FileName);
        return File.Exists(path) ? (path, photo.ClientId) : null;
    }

    public async Task DeleteAllForClientAsync(int clientId)
    {
        await using var db = dbFactory.CreateDbContext();
        var photos = await db.ProgressPhotos.Where(p => p.ClientId == clientId).ToListAsync();
        if (photos.Count > 0)
        {
            db.ProgressPhotos.RemoveRange(photos);
            await db.SaveChangesAsync();
        }
        try
        {
            var dir = ClientDir(clientId);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException ex) { logger.LogWarning(ex, "Could not remove photo folder of client {ClientId}.", clientId); }
    }

    private void DeleteFiles(int clientId, string fileName)
    {
        var dir = ClientDir(clientId);
        foreach (var name in new[] { fileName, ThumbName(fileName) })
        {
            try { File.Delete(Path.Combine(dir, name)); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete photo file {File}.", name); }
        }
    }
}
