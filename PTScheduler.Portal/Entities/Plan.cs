namespace PTScheduler.Portal.Entities;

public class Plan
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public string Currency { get; set; } = "PLN";
    public int MaxClients { get; set; }
    public bool PaymentsEnabled { get; set; }
    public bool CoursesEnabled { get; set; }
    public bool CustomDomain { get; set; }
    public bool PrioritySupport { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    public ICollection<Tenant> Tenants { get; set; } = [];
}
