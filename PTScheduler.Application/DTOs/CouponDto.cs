namespace PTScheduler.Application.DTOs;

public class CouponDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "percent";
    public decimal DiscountValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }
    public string Scope { get; set; } = "all";
    public bool IsActive { get; set; } = true;
}

public class SaveCouponDto
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "percent";
    public decimal DiscountValue { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int MaxUses { get; set; }
    public string Scope { get; set; } = "all";
    public bool IsActive { get; set; } = true;
}

public class CouponValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public int? CouponId { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string? Code { get; set; }
}
