using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PaymentSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory) : IPaymentSettingsService
{
    public async Task<PaymentSettingsDto> GetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.PaymentSettings.FirstOrDefaultAsync() ?? new PaymentSettings();
        return new PaymentSettingsDto
        {
            Enabled = s.Enabled,
            Sandbox = s.Sandbox,
            PosId = s.PosId,
            SecondKey = s.SecondKey,
            ClientId = s.ClientId,
            ClientSecret = s.ClientSecret,
            Currency = s.Currency
        };
    }

    public async Task SaveAsync(PaymentSettingsDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.PaymentSettings.FirstOrDefaultAsync() ?? new PaymentSettings();
        s.Enabled = dto.Enabled;
        s.Sandbox = dto.Sandbox;
        s.PosId = Trim(dto.PosId);
        s.SecondKey = Trim(dto.SecondKey);
        s.ClientId = Trim(dto.ClientId);
        s.ClientSecret = Trim(dto.ClientSecret);
        s.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "PLN" : dto.Currency.Trim().ToUpperInvariant();
        if (!db.PaymentSettings.Local.Contains(s))
            db.PaymentSettings.Add(s);
        await db.SaveChangesAsync();
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
