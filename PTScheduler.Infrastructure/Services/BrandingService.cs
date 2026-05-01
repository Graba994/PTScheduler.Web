using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class BrandingService(ApplicationDbContext db, IWebRootPathProvider webRoot) : IBrandingService
{
    public async Task<AppBrandingDto> GetAsync()
    {
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
        var b = await db.AppBrandings.FirstOrDefaultAsync() ?? new AppBranding();
        b.ThemeName = dto.ThemeName;
        b.CompanyName = dto.CompanyName;
        if (!db.AppBrandings.Local.Contains(b))
            db.AppBrandings.Add(b);
        await db.SaveChangesAsync();
    }

    public async Task<string> UploadLogoAsync(Stream stream, string fileName)
    {
        var path = await SaveFileAsync(stream, fileName, "logo");
        await UpdatePath(b => b.LogoPath = path);
        return path;
    }

    public async Task<string> UploadFaviconAsync(Stream stream, string fileName)
    {
        var path = await SaveFileAsync(stream, fileName, "favicon");
        await UpdatePath(b => b.FaviconPath = path);
        return path;
    }

    public async Task DeleteLogoAsync()
    {
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null) return;
        DeleteFile(b.LogoPath);
        b.LogoPath = null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteFaviconAsync()
    {
        var b = await db.AppBrandings.FirstOrDefaultAsync();
        if (b is null) return;
        DeleteFile(b.FaviconPath);
        b.FaviconPath = null;
        await db.SaveChangesAsync();
    }

    private async Task UpdatePath(Action<AppBranding> update)
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

    private static AppBrandingDto Map(AppBranding b) => new()
    {
        ThemeName = b.ThemeName,
        CompanyName = b.CompanyName,
        LogoPath = b.LogoPath,
        FaviconPath = b.FaviconPath
    };
}
