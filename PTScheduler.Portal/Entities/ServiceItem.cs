namespace PTScheduler.Portal.Entities;

public class ServiceItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "support";
    public decimal DefaultPrice { get; set; }
    public string PriceType { get; set; } = "one_time";
    public string? Unit { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Icon { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
