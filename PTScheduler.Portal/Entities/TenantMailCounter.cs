namespace PTScheduler.Portal.Entities;

/// <summary>Liczba e-maili wysłanych przez instancję danego dnia (UTC) przez SMTP platformy.</summary>
public class TenantMailCounter
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}
