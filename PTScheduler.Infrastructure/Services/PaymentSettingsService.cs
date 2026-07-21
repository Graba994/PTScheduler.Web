using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class PaymentSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory) : IPaymentSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<PaymentSettingsDto> GetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.PaymentSettings.FirstOrDefaultAsync() ?? new PaymentSettings();

        var dto = new PaymentSettingsDto
        {
            Enabled = s.Enabled,
            Currency = string.IsNullOrWhiteSpace(s.Currency) ? "PLN" : s.Currency
        };

        // Load stored providers (if any).
        var stored = new List<PaymentProviderConfigDto>();
        if (!string.IsNullOrWhiteSpace(s.ProvidersJson))
        {
            try { stored = JsonSerializer.Deserialize<List<PaymentProviderConfigDto>>(s.ProvidersJson, JsonOpts) ?? []; }
            catch { stored = []; }
        }

        // Ensure exactly one entry per catalogued provider, in catalogue order.
        foreach (var meta in PaymentProviderCatalog.All)
        {
            var existing = stored.FirstOrDefault(p => p.Key == meta.Key);
            if (existing is null)
            {
                existing = new PaymentProviderConfigDto { Key = meta.Key, Enabled = false, Sandbox = true };
                // One-time migration: seed PayU from legacy columns.
                if (meta.Key == PaymentProviders.PayU && !string.IsNullOrWhiteSpace(s.PosId))
                {
                    existing.Enabled = s.Enabled;
                    existing.Sandbox = s.Sandbox;
                    existing.Fields["PosId"] = s.PosId ?? "";
                    existing.Fields["SecondKey"] = s.SecondKey ?? "";
                    existing.Fields["ClientId"] = s.ClientId ?? "";
                    existing.Fields["ClientSecret"] = s.ClientSecret ?? "";
                }
            }
            existing.Fields ??= new();
            dto.Providers.Add(existing);
        }

        return dto;
    }

    public async Task SaveAsync(PaymentSettingsDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var s = await db.PaymentSettings.FirstOrDefaultAsync() ?? new PaymentSettings();

        s.Enabled = dto.Enabled;
        s.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "PLN" : dto.Currency.Trim().ToUpperInvariant();

        // Normalise & persist all providers as JSON.
        foreach (var p in dto.Providers)
        {
            p.Key = p.Key.Trim();
            p.Fields = p.Fields.ToDictionary(kv => kv.Key, kv => (kv.Value ?? string.Empty).Trim());
        }
        s.ProvidersJson = JsonSerializer.Serialize(dto.Providers, JsonOpts);

        // Keep legacy PayU columns in sync so any older reader still works.
        var payu = dto.Provider(PaymentProviders.PayU);
        if (payu is not null)
        {
            s.Sandbox = payu.Sandbox;
            s.PosId = Trim(payu.Get("PosId"));
            s.SecondKey = Trim(payu.Get("SecondKey"));
            s.ClientId = Trim(payu.Get("ClientId"));
            s.ClientSecret = Trim(payu.Get("ClientSecret"));
        }

        if (!db.PaymentSettings.Local.Contains(s))
            db.PaymentSettings.Add(s);
        await db.SaveChangesAsync();
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
