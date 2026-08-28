namespace PTScheduler.Portal.Entities;

public class PaymentRecord
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string? StripeInvoiceId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public PaymentRecordStatus Status { get; set; }
    public string? Description { get; set; }
    public string Source { get; set; } = "stripe";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
}

public enum PaymentRecordStatus
{
    Paid = 0,
    Failed = 1,
    Refunded = 2,
    Pending = 3
}
