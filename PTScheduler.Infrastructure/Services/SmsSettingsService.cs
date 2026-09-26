using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class SmsSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory) : ISmsSettingsService
{
    public async Task<SmsSettingsDto> GetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        if (s is null) return new SmsSettingsDto();
        return new SmsSettingsDto
        {
            IsEnabled = s.IsEnabled,
            ApiToken = s.ApiToken,
            SenderName = s.SenderName
        };
    }

    public async Task SaveAsync(SmsSettingsDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.SmsSettings.FirstOrDefaultAsync();
        if (s is null)
        {
            s = new SmsSettings { Id = 1 };
            db.SmsSettings.Add(s);
        }
        s.IsEnabled = dto.IsEnabled;
        s.ApiToken = string.IsNullOrWhiteSpace(dto.ApiToken) ? s.ApiToken : dto.ApiToken;
        s.SenderName = dto.SenderName;
        await db.SaveChangesAsync();
    }
}
