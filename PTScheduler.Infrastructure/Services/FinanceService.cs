using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class FinanceService(IDbContextFactory<ApplicationDbContext> dbFactory) : IFinanceService
{
    public async Task SetPackageHiddenAsync(int packageId, bool hidden)
    {
        await using var db = dbFactory.CreateDbContext();
        var p = await db.SessionPackages.FindAsync(packageId);
        if (p is null) return;
        p.IsHidden = hidden;
        await db.SaveChangesAsync();
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        await using var db = dbFactory.CreateDbContext();
        var fp = await db.Set<FinancePin>().FirstOrDefaultAsync();
        if (fp is null || string.IsNullOrEmpty(fp.PinHash)) return false;
        return fp.PinHash == HashPin(pin);
    }

    public async Task SetPinAsync(string pin)
    {
        await using var db = dbFactory.CreateDbContext();
        var fp = await db.Set<FinancePin>().FirstOrDefaultAsync();
        if (fp is null)
        {
            fp = new FinancePin { PinHash = HashPin(pin) };
            db.Set<FinancePin>().Add(fp);
        }
        else
        {
            fp.PinHash = HashPin(pin);
        }
        await db.SaveChangesAsync();
    }

    public async Task<bool> IsPinSetAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        var fp = await db.Set<FinancePin>().FirstOrDefaultAsync();
        return fp is not null && !string.IsNullOrEmpty(fp.PinHash);
    }

    public async Task<FinanceTaxConfigDto> GetTaxConfigAsync(string module)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.Set<FinanceTaxConfig>().FirstOrDefaultAsync(c => c.Module == module);
        if (cfg is null) return new FinanceTaxConfigDto { Module = module };
        return new FinanceTaxConfigDto
        {
            Module = cfg.Module,
            VatEnabled = cfg.VatEnabled,
            VatRate = cfg.VatRate,
            IncomeTaxType = cfg.IncomeTaxType,
            FlatTaxRate = cfg.FlatTaxRate,
            LumpSumRate = cfg.LumpSumRate,
            ScaleTaxThreshold = cfg.ScaleTaxThreshold,
            ScaleTaxRateLow = cfg.ScaleTaxRateLow,
            ScaleTaxRateHigh = cfg.ScaleTaxRateHigh,
            ZusEnabled = cfg.ZusEnabled,
            ZusMonthlyAmount = cfg.ZusMonthlyAmount,
            HealthInsuranceEnabled = cfg.HealthInsuranceEnabled,
            HealthInsuranceMonthly = cfg.HealthInsuranceMonthly,
            CostDeductionsEnabled = cfg.CostDeductionsEnabled,
            MonthlyFixedCosts = cfg.MonthlyFixedCosts,
            InvoiceNumberingEnabled = cfg.InvoiceNumberingEnabled,
            InvoicePrefix = cfg.InvoicePrefix,
            InvoiceNextNumber = cfg.InvoiceNextNumber
        };
    }

    public async Task SaveTaxConfigAsync(FinanceTaxConfigDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var cfg = await db.Set<FinanceTaxConfig>().FirstOrDefaultAsync(c => c.Module == dto.Module);
        if (cfg is null)
        {
            cfg = new FinanceTaxConfig { Module = dto.Module };
            db.Set<FinanceTaxConfig>().Add(cfg);
        }
        cfg.VatEnabled = dto.VatEnabled;
        cfg.VatRate = dto.VatRate;
        cfg.IncomeTaxType = dto.IncomeTaxType;
        cfg.FlatTaxRate = dto.FlatTaxRate;
        cfg.LumpSumRate = dto.LumpSumRate;
        cfg.ScaleTaxThreshold = dto.ScaleTaxThreshold;
        cfg.ScaleTaxRateLow = dto.ScaleTaxRateLow;
        cfg.ScaleTaxRateHigh = dto.ScaleTaxRateHigh;
        cfg.ZusEnabled = dto.ZusEnabled;
        cfg.ZusMonthlyAmount = dto.ZusMonthlyAmount;
        cfg.HealthInsuranceEnabled = dto.HealthInsuranceEnabled;
        cfg.HealthInsuranceMonthly = dto.HealthInsuranceMonthly;
        cfg.CostDeductionsEnabled = dto.CostDeductionsEnabled;
        cfg.MonthlyFixedCosts = dto.MonthlyFixedCosts;
        cfg.InvoiceNumberingEnabled = dto.InvoiceNumberingEnabled;
        cfg.InvoicePrefix = dto.InvoicePrefix;
        cfg.InvoiceNextNumber = dto.InvoiceNextNumber;
        await db.SaveChangesAsync();
    }

    private static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
        return Convert.ToHexStringLower(bytes);
    }
}
