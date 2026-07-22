namespace PTScheduler.Portal.Entities;

public class Tenant
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int Port { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string DbPassword { get; set; } = string.Empty;
    public TenantStatus Status { get; set; } = TenantStatus.Pending;
    public string PlanId { get; set; } = "start";
    public string? SetupMode { get; set; }
    public string? Notes { get; set; }
    public string? WebContainerName { get; set; }
    public string? DbContainerName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProvisionedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public Plan? Plan { get; set; }
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}

public enum TenantStatus
{
    Pending,
    Provisioning,
    Active,
    Suspended,
    Destroyed
}
