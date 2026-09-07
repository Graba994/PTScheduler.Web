using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ICouponService
{
    Task<List<CouponDto>> GetAllAsync();
    Task<CouponDto?> GetAsync(int id);
    Task<int> CreateAsync(SaveCouponDto dto);
    Task UpdateAsync(int id, SaveCouponDto dto);
    Task DeleteAsync(int id);

    // Called from checkout: checks scope, expiry, uses, active. Returns
    // the discount + final amount if OK.
    Task<CouponValidationResult> ValidateAsync(string code, decimal amount, string targetType);

    // Registers a successful redemption and increments UsedCount.
    Task RedeemAsync(int couponId, string? userId, string? userEmail,
        decimal originalAmount, decimal discountAmount, decimal finalAmount,
        string targetType, int? targetId);
}
