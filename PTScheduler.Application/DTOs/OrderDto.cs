namespace PTScheduler.Application.DTOs;

public class OrderDto
{
    public int Id { get; set; }
    /// <summary>Human title of what was bought (course or package name).</summary>
    public string ItemTitle { get; set; } = string.Empty;
    /// <summary>"Course" or "Package".</summary>
    public string Kind { get; set; } = "Course";
    /// <summary>Gateway key that handled the order.</summary>
    public string Provider { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    public decimal? OriginalAmount { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? CouponCode { get; set; }

    public bool HasDiscount => DiscountAmount.HasValue && DiscountAmount > 0;

    // Faktura i KSeF
    public string? InvoiceNumber { get; set; }
    public string? BuyerNip { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerCity { get; set; }
    public PTScheduler.Domain.Enums.KsefStatus KsefStatus { get; set; }
    public string? KsefNumber { get; set; }
    public string? KsefError { get; set; }

    // Back-compat alias (older markup referenced CourseTitle).
    public string CourseTitle => ItemTitle;
}
