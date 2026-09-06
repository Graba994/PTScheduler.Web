namespace PTScheduler.Domain.Entities;

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }

    // "percent" (0-100) or "amount" (zł off)
    public string DiscountType { get; set; } = "percent";
    public decimal DiscountValue { get; set; }

    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }

    // 0 = unlimited
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }

    // Scope: "all" | "packages" | "courses"
    public string Scope { get; set; } = "all";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CouponRedemption> Redemptions { get; set; } = [];
}

public class CouponRedemption
{
    public int Id { get; set; }
    public int CouponId { get; set; }
    public Coupon Coupon { get; set; } = null!;

    public string? UserId { get; set; }
    public string? UserEmail { get; set; }

    public decimal OriginalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string TargetType { get; set; } = "";  // "package" | "course"
    public int? TargetId { get; set; }

    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
}
