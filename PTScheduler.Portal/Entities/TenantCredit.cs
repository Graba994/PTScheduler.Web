namespace PTScheduler.Portal.Entities;

public class TenantCredit
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string CreditType { get; set; } = "";  // "sms" | "cdn_storage_gb" | "cdn_bandwidth_gb"
    public decimal Balance { get; set; }
    public decimal TotalPurchased { get; set; }
    public decimal TotalUsed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
}
