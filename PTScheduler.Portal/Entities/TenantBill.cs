namespace PTScheduler.Portal.Entities;

/// <summary>
/// Miesięczny rachunek trenera: abonament, dodatki, opłaty z oferty i jednorazowe zakupy ze sklepu.
/// Portal wystawia go sam, wysyła e-mail z linkiem do płatności i przypomina o zaległościach.
/// </summary>
public class TenantBill
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    /// <summary>Numer rachunku, np. R/2026/10/0007.</summary>
    public string Number { get; set; } = "";
    /// <summary>Pierwszy dzień rozliczanego miesiąca.</summary>
    public DateOnly PeriodStart { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";

    public TenantBillStatus Status { get; set; } = TenantBillStatus.Issued;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateOnly DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>„autopay”, „payu”, „przelewy24”, „stripe” albo „manual” (przelew / gotówka oznaczone przez administratora).</summary>
    public string? PaidVia { get; set; }

    /// <summary>Losowy identyfikator linku do płatności (strona rachunku bez logowania).</summary>
    public string PayToken { get; set; } = "";
    public string? PaymentGateway { get; set; }
    public string? PaymentExternalId { get; set; }
    /// <summary>Nasz identyfikator płatności wysłany do bramki (sessionId / extOrderId / OrderID).</summary>
    public string? PaymentSessionId { get; set; }

    public DateTime? EmailSentAt { get; set; }
    public int ReminderCount { get; set; }
    public DateTime? LastReminderAt { get; set; }
    /// <summary>Kiedy administrator dostał informację o poważnej zaległości.</summary>
    public DateTime? EscalatedAt { get; set; }
    public string? AdminNote { get; set; }

    public List<TenantBillLine> Lines { get; set; } = [];
}

public class TenantBillLine
{
    public int Id { get; set; }
    public int BillId { get; set; }
    public TenantBill? Bill { get; set; }
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Jednorazowa pozycja oferty rozliczona tym rachunkiem (przy anulowaniu wraca do rozliczenia).</summary>
    public int? OfferItemId { get; set; }
}

public enum TenantBillStatus
{
    Issued = 0,
    Paid = 1,
    Cancelled = 2
}
