namespace PTScheduler.Domain.Entities;

public enum GiftVoucherKind
{
    /// <summary>Bon na kwotę — działa jak jednorazowy kupon kwotowy w sklepie.</summary>
    Amount = 0,
    /// <summary>Bon na pakiet — po wpisaniu kodu obdarowany dostaje opłacone sesje.</summary>
    Package = 1
}

public enum GiftVoucherStatus
{
    /// <summary>Zamówiony, czeka na płatność.</summary>
    Pending = 0,
    Active = 1,
    Redeemed = 2,
    Cancelled = 3
}

/// <summary>Bon podarunkowy — kupiony online albo wystawiony ręcznie przez trenera.</summary>
public class GiftVoucher
{
    public int Id { get; set; }
    /// <summary>Kod do wpisania, np. <c>BON-7KQ2-M9XA</c> (unikalny).</summary>
    public string Code { get; set; } = string.Empty;
    public GiftVoucherKind Kind { get; set; }
    public GiftVoucherStatus Status { get; set; } = GiftVoucherStatus.Pending;

    /// <summary>Kwota bonu (Amount) albo cena pakietu w chwili zakupu (Package).</summary>
    public decimal Value { get; set; }
    public string Currency { get; set; } = "PLN";
    /// <summary>Nazwa na bonie, np. „Bon 200 zł” albo „Pakiet 5 treningów”.</summary>
    public string Title { get; set; } = string.Empty;

    // Bon na pakiet — kopia oferty z chwili zakupu (oferta może się później zmienić).
    public int? PackageOfferId { get; set; }
    public int? SessionTypeId { get; set; }
    public int? SessionsCount { get; set; }
    public int? PackageValidDays { get; set; }

    public string? RecipientName { get; set; }
    public string? FromName { get; set; }
    public string? Message { get; set; }

    public string? BuyerUserId { get; set; }
    public bool IssuedManually { get; set; }
    public string? IssuedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public int? RedeemedByClientId { get; set; }
    /// <summary>Kupon utworzony dla bonu kwotowego (kod = kod bonu).</summary>
    public int? CouponId { get; set; }
}
