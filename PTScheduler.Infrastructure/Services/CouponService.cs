using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Entities;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Infrastructure.Services;

public class CouponService(IDbContextFactory<ApplicationDbContext> dbFactory) : ICouponService
{
    public async Task<List<CouponDto>> GetAllAsync()
    {
        await using var db = dbFactory.CreateDbContext();
        return await db.Coupons
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => Map(c))
            .ToListAsync();
    }

    public async Task<CouponDto?> GetAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Coupons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return c is null ? null : Map(c);
    }

    public async Task<int> CreateAsync(SaveCouponDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = new Coupon();
        Apply(c, dto);
        db.Coupons.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    public async Task UpdateAsync(int id, SaveCouponDto dto)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Coupon not found.");
        Apply(c, dto);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return;
        db.Coupons.Remove(c);
        await db.SaveChangesAsync();
    }

    public async Task<CouponValidationResult> ValidateAsync(string code, decimal amount, string targetType)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new() { IsValid = false, Error = "Wpisz kod." };

        await using var db = dbFactory.CreateDbContext();
        var c = await db.Coupons.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code.ToUpper() == code.Trim().ToUpper());

        if (c is null) return new() { IsValid = false, Error = "Nieprawidłowy kod." };
        if (!c.IsActive) return new() { IsValid = false, Error = "Kod nieaktywny." };

        var now = DateTime.UtcNow;
        if (c.ValidFrom.HasValue && now < c.ValidFrom.Value)
            return new() { IsValid = false, Error = "Kod jeszcze nie obowiązuje." };
        if (c.ValidUntil.HasValue && now > c.ValidUntil.Value)
            return new() { IsValid = false, Error = "Kod wygasł." };
        if (c.MaxUses > 0 && c.UsedCount >= c.MaxUses)
            return new() { IsValid = false, Error = "Kod wyczerpany." };
        if (c.Scope != "all" && !string.Equals(c.Scope, targetType, StringComparison.OrdinalIgnoreCase))
            return new() { IsValid = false, Error = $"Kod nie obowiązuje na {targetType}." };

        decimal discount = c.DiscountType == "percent"
            ? Math.Round(amount * c.DiscountValue / 100m, 2)
            : Math.Min(c.DiscountValue, amount);
        var final = Math.Max(0, amount - discount);

        return new CouponValidationResult
        {
            IsValid = true,
            CouponId = c.Id,
            Code = c.Code,
            DiscountAmount = discount,
            FinalAmount = final
        };
    }

    public async Task RedeemAsync(int couponId, string? userId, string? userEmail,
        decimal originalAmount, decimal discountAmount, decimal finalAmount,
        string targetType, int? targetId)
    {
        await using var db = dbFactory.CreateDbContext();
        var c = await db.Coupons.FirstOrDefaultAsync(x => x.Id == couponId);
        if (c is null) return;
        c.UsedCount++;

        db.CouponRedemptions.Add(new CouponRedemption
        {
            CouponId = couponId,
            UserId = userId,
            UserEmail = userEmail,
            OriginalAmount = originalAmount,
            DiscountAmount = discountAmount,
            FinalAmount = finalAmount,
            TargetType = targetType,
            TargetId = targetId
        });
        await db.SaveChangesAsync();
    }

    private static CouponDto Map(Coupon c) => new()
    {
        Id = c.Id,
        Code = c.Code,
        Description = c.Description,
        DiscountType = c.DiscountType,
        DiscountValue = c.DiscountValue,
        ValidFrom = c.ValidFrom,
        ValidUntil = c.ValidUntil,
        MaxUses = c.MaxUses,
        UsedCount = c.UsedCount,
        Scope = c.Scope,
        IsActive = c.IsActive
    };

    private static void Apply(Coupon c, SaveCouponDto dto)
    {
        c.Code = dto.Code.Trim().ToUpperInvariant();
        c.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        c.DiscountType = dto.DiscountType;
        c.DiscountValue = dto.DiscountValue;
        c.ValidFrom = dto.ValidFrom;
        c.ValidUntil = dto.ValidUntil;
        c.MaxUses = Math.Max(0, dto.MaxUses);
        c.Scope = dto.Scope;
        c.IsActive = dto.IsActive;
    }
}
