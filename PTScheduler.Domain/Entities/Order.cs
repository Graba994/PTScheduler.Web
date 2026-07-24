using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Order
{
    public int Id { get; set; }

    public string ApplicationUserId { get; set; } = string.Empty;

    /// <summary>What this order pays for.</summary>
    public OrderKind Kind { get; set; } = OrderKind.Course;

    /// <summary>Payment gateway key that handles this order (see <see cref="Constants.PaymentProviders"/>).</summary>
    public string Provider { get; set; } = Constants.PaymentProviders.PayU;

    // Course order target (null for package orders).
    public int? CourseId { get; set; }
    public Course? Course { get; set; }

    // Package order target (null for course orders).
    public int? PackageOfferId { get; set; }
    public PackageOffer? PackageOffer { get; set; }

    // Our unique order reference sent to the gateway as extOrderId.
    public string ExtOrderId { get; set; } = string.Empty;
    // The gateway's own order id, filled after the order is created.
    public string? PayUOrderId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";

    // Coupon applied at checkout (optional). Amount above is the FINAL charged amount;
    // OriginalAmount + DiscountAmount describe the discount applied.
    public int? CouponId { get; set; }
    public string? CouponCode { get; set; }
    public decimal? OriginalAmount { get; set; }
    public decimal? DiscountAmount { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
}
