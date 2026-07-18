using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SiteSettingsService(ApplicationDbContext db, IMemoryCache cache) : ISiteSettingsService
{
    private const int SingletonId = 1;
    private const string CacheKey = "SiteSettings";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<SiteSettingsDto> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out SiteSettingsDto? cached) && cached is not null)
            return cached;

        var s = await GetOrCreateAsync();
        var dto = MapToDto(s);
        cache.Set(CacheKey, dto, CacheDuration);
        return dto;
    }

    public async Task SaveAsync(SiteSettingsDto dto)
    {
        var s = await GetOrCreateAsync();
        s.WelcomeEnabled = dto.WelcomeEnabled;
        s.SchedulerEnabled = dto.SchedulerEnabled;
        s.AcademyEnabled = dto.AcademyEnabled;
        s.ShopEnabled = dto.ShopEnabled;
        s.HeroHeadline = dto.HeroHeadline;
        s.HeroSubheadline = dto.HeroSubheadline;
        s.HeroImageUrl = dto.HeroImageUrl;
        s.HeroCtaLabel = dto.HeroCtaLabel;
        s.HeroCtaUrl = dto.HeroCtaUrl;
        s.BodyHtml = dto.BodyHtml;
        s.ContactEmail = dto.ContactEmail;
        s.PayUIsSandbox = dto.PayUIsSandbox;
        s.PayUPosId = dto.PayUPosId;
        s.PayUClientId = dto.PayUClientId;
        s.PayUClientSecret = dto.PayUClientSecret;
        s.PayUSecondKey = dto.PayUSecondKey;
        await db.SaveChangesAsync();
        cache.Set(CacheKey, MapToDto(s), CacheDuration);
    }

    private static SiteSettingsDto MapToDto(SiteSettings s) => new()
    {
        WelcomeEnabled = s.WelcomeEnabled,
        SchedulerEnabled = s.SchedulerEnabled,
        AcademyEnabled = s.AcademyEnabled,
        ShopEnabled = s.ShopEnabled,
        HeroHeadline = s.HeroHeadline,
        HeroSubheadline = s.HeroSubheadline,
        HeroImageUrl = s.HeroImageUrl,
        HeroCtaLabel = s.HeroCtaLabel,
        HeroCtaUrl = s.HeroCtaUrl,
        BodyHtml = s.BodyHtml,
        ContactEmail = s.ContactEmail,
        PayUIsSandbox = s.PayUIsSandbox,
        PayUPosId = s.PayUPosId,
        PayUClientId = s.PayUClientId,
        PayUClientSecret = s.PayUClientSecret,
        PayUSecondKey = s.PayUSecondKey
    };

    private async Task<SiteSettings> GetOrCreateAsync()
    {
        var s = await db.SiteSettings.FirstOrDefaultAsync(x => x.Id == SingletonId);
        if (s is null)
        {
            s = new SiteSettings { Id = SingletonId };
            db.SiteSettings.Add(s);
            await db.SaveChangesAsync();
        }
        return s;
    }
}
