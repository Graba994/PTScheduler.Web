using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class EmailSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory) : IEmailSettingsService
{
    public async Task<EmailSettingsDto> GetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var e = await db.EmailSettings.FirstOrDefaultAsync();
        if (e is null) return new EmailSettingsDto();
        return new EmailSettingsDto
        {
            IsEnabled    = e.IsEnabled,
            Provider     = e.Provider,
            SmtpHost     = e.SmtpHost,
            SmtpPort     = e.SmtpPort,
            UseTls       = e.UseTls,
            Login        = e.Login,
            Password     = e.Password,
            FromAddress  = e.FromAddress,
            FromName     = e.FromName
        };
    }

    public async Task SaveAsync(EmailSettingsDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var e = await db.EmailSettings.FirstOrDefaultAsync();
        if (e is null)
        {
            e = new EmailSettings { Id = 1 };
            db.EmailSettings.Add(e);
        }
        e.IsEnabled   = dto.IsEnabled;
        e.Provider    = dto.Provider;
        e.SmtpHost    = dto.SmtpHost;
        e.SmtpPort    = dto.SmtpPort;
        e.UseTls      = dto.UseTls;
        e.Login       = dto.Login;
        e.Password    = string.IsNullOrWhiteSpace(dto.Password) ? e.Password : dto.Password;
        e.FromAddress = dto.FromAddress;
        e.FromName    = dto.FromName;
        await db.SaveChangesAsync();
    }
}
