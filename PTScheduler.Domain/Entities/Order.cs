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

    /// <summary>Opłacany okres karnetu cyklicznego (Kind = Membership).</summary>
    public int? MembershipPeriodId { get; set; }

    /// <summary>Karnet kupowany w sklepie (nowa subskrypcja po opłaceniu).</summary>
    public int? MembershipPlanId { get; set; }

    /// <summary>Kupowany bon podarunkowy (Kind = GiftVoucher).</summary>
    public int? GiftVoucherId { get; set; }
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

    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceIssuedAt { get; set; }

    // Faktura na firmę (B2B) — dane nabywcy. Bez NIP faktura jest wystawiana na osobę prywatną.
    public string? BuyerNip { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPostalCode { get; set; }
    public string? BuyerCity { get; set; }

    // KSeF
    public KsefStatus KsefStatus { get; set; } = KsefStatus.None;
    public string? KsefNumber { get; set; }
    public string? KsefSessionReference { get; set; }
    public string? KsefInvoiceReference { get; set; }
    public string? KsefError { get; set; }
    public DateTime? KsefSentAt { get; set; }
}
