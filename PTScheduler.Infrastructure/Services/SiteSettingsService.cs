using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SiteSettingsService(ApplicationDbContext db) : ISiteSettingsService
{
    private const int SingletonId = 1;

    public async Task<SiteSettingsDto> GetAsync()
    {
        var s = await GetOrCreateAsync();
        return new SiteSettingsDto
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
    }

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
