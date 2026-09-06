namespace PTScheduler.Portal.Entities;

public class Subscription
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string PlanId { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string BillingCycle { get; set; } = "monthly";
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public DateTime StartsAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndsAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Tenant? Tenant { get; set; }
    public Plan? Plan { get; set; }
}

public enum SubscriptionStatus
{
    Active,
    PastDue,
    Cancelled,
    Expired
}
