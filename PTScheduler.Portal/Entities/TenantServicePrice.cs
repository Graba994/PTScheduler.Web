namespace PTScheduler.Portal.Entities;

public class TenantServicePrice
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ServiceItemId { get; set; }
    public decimal CustomPrice { get; set; }
    public bool IsHidden { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ServiceItem ServiceItem { get; set; } = null!;
}
