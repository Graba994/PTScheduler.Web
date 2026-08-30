namespace PTScheduler.Portal.Entities;

public class ServiceOrder
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ServiceItemId { get; set; }
    public decimal Price { get; set; }
    public ServiceOrderStatus Status { get; set; } = ServiceOrderStatus.Pending;
    public string? Notes { get; set; }
    public string? AdminNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? StripePaymentIntentId { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ServiceItem ServiceItem { get; set; } = null!;
}

public enum ServiceOrderStatus
{
    Pending,
    Accepted,
    InProgress,
    Completed,
    Cancelled
}
