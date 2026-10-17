using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class BrandingService(IDbContextFactory<ApplicationDbContext> dbFactory, IWebRootPathProvider webRoot) : IBrandingService
{
    public async Task<AppBrandingDto> GetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null)
        {
            b = new AppBranding();
            db.AppBrandings.Add(b);
            await db.SaveChangesAsync();
        }
        return Map(b);
    }

    public async Task SaveAsync(SaveBrandingDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync() ?? new AppBranding();
        b.ThemeName = NormalizeAccent(dto.ThemeName);
        b.ThemeMode = dto.ThemeMode is "light" or "dark" or "system" ? dto.ThemeMode : "light";
        b.CompanyName = dto.CompanyName;
        b.PwaShortName = string.IsNullOrWhiteSpace(dto.PwaShortName) ? null : dto.PwaShortName.Trim();
        b.PwaBannerEnabled = dto.PwaBannerEnabled;
        b.PwaBannerTitle = string.IsNullOrWhiteSpace(dto.PwaBannerTitle) ? null : dto.PwaBannerTitle.Trim();
        b.PwaBannerBody = string.IsNullOrWhiteSpace(dto.PwaBannerBody) ? null : dto.PwaBannerBody.Trim();
        b.PwaBannerButton = string.IsNullOrWhiteSpace(dto.PwaBannerButton) ? null : dto.PwaBannerButton.Trim();
        b.LoginTitle = string.IsNullOrWhiteSpace(dto.LoginTitle) ? null : dto.LoginTitle.Trim();
        b.LoginSubtitle = string.IsNullOrWhiteSpace(dto.LoginSubtitle) ? null : dto.LoginSubtitle.Trim();
        if (!db.AppBrandings.Local.Contains(b))
            db.AppBrandings.Add(b);
        await db.SaveChangesAsync();
    }

    private static string NormalizeAccent(string name) =>
        name.EndsWith("-dark") ? name[..^5] : name;

    public async Task<string> UploadLogoAsync(Stream stream, string fileName)
    {
        var path = await SaveFileAsync(stream, fileName, "logo");
        await using var db = dbFactory.CreateDbContext();
        await UpdatePath(db, b => b.LogoPath = path);
        return path;
    }

    public async Task<string> UploadFaviconAsync(Stream stream, string fileName)
    {
        var path = await SaveFileAsync(stream, fileName, "favicon");
        await using var db = dbFactory.CreateDbContext();
        await UpdatePath(db, b => b.FaviconPath = path);
        return path;
    }

    public async Task<string> UploadPwaIconAsync(Stream stream, string fileName)
    {
        // Obraz rastrowy przeskalowujemy do prawdziwych 512×512 i 192×192 (wymóg instalacji w Chrome);
        // SVG i inne formaty zapisujemy bez zmian.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        string path;
        var png512 = Services.Photos.PwaIconRenderer.RenderSquarePng(bytes, 512);
        var png192 = png512 is null ? null : Services.Photos.PwaIconRenderer.RenderSquarePng(bytes, 192);
        if (png512 is not null && png192 is not null)
        {
            var dir = Path.Combine(webRoot.WebRootPath, "branding");
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "pwa-icon*"))
                File.Delete(old);
            await File.WriteAllBytesAsync(Path.Combine(dir, "pwa-icon.png"), png512);
            await File.WriteAllBytesAsync(Path.Combine(dir, "pwa-icon-192.png"), png192);
            path = "/branding/pwa-icon.png";
        }
        else
        {
            var old192 = Path.Combine(webRoot.WebRootPath, "branding", "pwa-icon-192.png");
            if (File.Exists(old192)) File.Delete(old192);
            path = await SaveFileAsync(new MemoryStream(bytes), fileName, "pwa-icon");
        }
        await using var db = dbFactory.CreateDbContext();
        await UpdatePath(db, b => b.PwaIconPath = path);
        return path;
    }

    public async Task<string> UploadLoginBackgroundAsync(Stream stream, string fileName)
    {
        var path = await SaveFileAsync(stream, fileName, "login-bg");
        await using var db = dbFactory.CreateDbContext();
        await UpdatePath(db, b => b.LoginBackgroundPath = path);
        return path;
    }

    public async Task DeleteLoginBackgroundAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null) return;
        DeleteFile(b.LoginBackgroundPath);
        b.LoginBackgroundPath = null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteLogoAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null) return;
        DeleteFile(b.LogoPath);
        b.LogoPath = null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteFaviconAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null) return;
        DeleteFile(b.FaviconPath);
        b.FaviconPath = null;
        await db.SaveChangesAsync();
    }

    public async Task DeletePwaIconAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null) return;
        DeleteFile(b.PwaIconPath);
        DeleteFile("/branding/pwa-icon-192.png");
        b.PwaIconPath = null;
        await db.SaveChangesAsync();
    }

    private static async Task UpdatePath(ApplicationDbContext db, Action<AppBranding> update)
    {
        var b = await db.AppBrandings.FirstOrDefaultAsync() ?? new AppBranding();
        update(b);
        if (!db.AppBrandings.Local.Contains(b)) db.AppBrandings.Add(b);
        await db.SaveChangesAsync();
    }

    private async Task<string> SaveFileAsync(Stream stream, string fileName, string prefix)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var dir = Path.Combine(webRoot.WebRootPath, "branding");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, $"{prefix}.*"))
            File.Delete(old);
        var filePath = Path.Combine(dir, $"{prefix}{ext}");
        await using var fs = File.Create(filePath);
        await stream.CopyToAsync(fs);
        return $"/branding/{prefix}{ext}";
    }

    private void DeleteFile(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;
        var full = Path.Combine(webRoot.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full)) File.Delete(full);
    }

    private AppBrandingDto Map(AppBranding b) => new()
    {
        ThemeName = b.ThemeName,
        ThemeMode = b.ThemeMode,
        CompanyName = b.CompanyName,
        LogoPath = ResolveFilePath(b.LogoPath),
        FaviconPath = ResolveFilePath(b.FaviconPath),
        PwaShortName = b.PwaShortName,
        PwaBannerEnabled = b.PwaBannerEnabled,
        PwaBannerTitle = b.PwaBannerTitle,
        PwaBannerBody = b.PwaBannerBody,
        PwaBannerButton = b.PwaBannerButton,
        PwaIconPath = ResolveFilePath(b.PwaIconPath),
        LoginTitle = b.LoginTitle,
        LoginSubtitle = b.LoginSubtitle,
        LoginBackgroundPath = ResolveFilePath(b.LoginBackgroundPath),
        SetupCompleted = b.SetupCompleted,
        SetupMode = b.SetupMode,
        SetupCompletedAt = b.SetupCompletedAt
    };

    private string? ResolveFilePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        var full = Path.Combine(webRoot.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? relativePath : null;
    }
}
