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

    // Szkic leży obok opublikowanej treści (trwały wolumen „branding”). Zawiera wyłącznie treść
    // strony publicznej; nie jest nigdzie linkowany.
    private string DraftPath =>
        Path.Combine(webRoot.WebRootPath, "branding", "site-content.draft.json");

    private static async Task<SiteContentDto?> ReadAsync(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SiteContentDto>(fs);
        }
        catch { return null; /* Uszkodzony plik — domyślne. */ }
    }

    public async Task<SiteContentDto> GetDraftAsync() => await ReadAsync(DraftPath) ?? await GetAsync();

    public async Task SaveDraftAsync(SiteContentDto dto) => await WriteAsync(DraftPath, dto);

    public async Task<bool> HasUnpublishedDraftAsync()
    {
        if (!File.Exists(DraftPath)) return false;
        if (!File.Exists(FilePath)) return true;
        return await File.ReadAllTextAsync(DraftPath) != await File.ReadAllTextAsync(FilePath);
    }

    public async Task PublishDraftAsync()
    {
        var draft = await ReadAsync(DraftPath);
        if (draft is null) return;
        await SaveAsync(draft);
        File.Delete(DraftPath);
    }

    public Task DiscardDraftAsync()
    {
        if (File.Exists(DraftPath)) File.Delete(DraftPath);
        return Task.CompletedTask;
    }

    private async Task WriteAsync(string path, SiteContentDto dto)
    {
        dto.AboutHtml = SafeHtml.SanitizeOrNull(dto.AboutHtml);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, dto, JsonOptions);
        File.Move(tmp, path, overwrite: true);
    }

    public async Task<SiteContentDto> GetAsync()
    {
        if (await ReadAsync(FilePath) is { } dto) return dto;

        // Nowa instalacja: od razu nowoczesna strona z widgetów (szablon „Trener personalny”).
        return SiteWidgets.BuildTemplate("personal");
    }

    public async Task<string> UploadImageAsync(Stream stream, string fileName)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var compressed = Photos.PhotoCompressor.Compress(buffer.ToArray(), 1920, 82);
        var dir = Path.Combine(webRoot.WebRootPath, "branding", "site");
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}.webp";
        await File.WriteAllBytesAsync(Path.Combine(dir, name), compressed.Data);
        return $"/branding/site/{name}";
    }

    public Task SaveAsync(SiteContentDto dto) => WriteAsync(FilePath, dto);
}
