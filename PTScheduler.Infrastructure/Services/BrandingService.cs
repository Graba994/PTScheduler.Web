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
        var path = await SaveFileAsync(stream, fileName, "pwa-icon");
        await using var db = dbFactory.CreateDbContext();
        await UpdatePath(db, b => b.PwaIconPath = path);
        return path;
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
        PwaIconPath = ResolveFilePath(b.PwaIconPath)
    };

    private string? ResolveFilePath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return null;
        var full = Path.Combine(webRoot.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? relativePath : null;
    }
}
