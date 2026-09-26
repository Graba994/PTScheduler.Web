using PTScheduler.Domain.Entities;

namespace PTScheduler.Application.Marketing;

public class GiftVoucherDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public GiftVoucherKind Kind { get; set; }
    public GiftVoucherStatus Status { get; set; }
    public decimal Value { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Title { get; set; } = "";
    public int? SessionsCount { get; set; }
    public string? RecipientName { get; set; }
    public string? FromName { get; set; }
    public string? Message { get; set; }
    public bool IssuedManually { get; set; }
    public string? BuyerEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public string? RedeemedByName { get; set; }
    /// <summary>Bon kwotowy wykorzystany w sklepie (kupon zużyty).</summary>
    public bool CouponUsed { get; set; }

    public bool IsUsable => Status == GiftVoucherStatus.Active && !CouponUsed && (ExpiresAt is null || ExpiresAt > DateTime.UtcNow);
}

/// <summary>Zamówienie bonu (zakup online albo wystawienie ręczne).</summary>
public class GiftVoucherRequest
{
    public GiftVoucherKind Kind { get; set; } = GiftVoucherKind.Amount;
    public decimal Amount { get; set; }
    public int? PackageOfferId { get; set; }
    public string? RecipientName { get; set; }
    public string? FromName { get; set; }
    public string? Message { get; set; }
}
