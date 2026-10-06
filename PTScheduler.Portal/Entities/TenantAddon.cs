namespace PTScheduler.Portal.Entities;

/// <summary>
/// Miesięczny dodatek do abonamentu trenera (np. +100 GB transferu wideo).
/// Dopóki jest aktywny, podnosi limity w uprawnieniach instancji. Rozliczany
/// jako pozycja subskrypcji Stripe (automatycznie co miesiąc) albo — bez
/// Stripe — zamówieniem w sklepie usług.
/// </summary>
public class TenantAddon
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public int ServiceItemId { get; set; }
    public ServiceItem? ServiceItem { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>Pozycja subskrypcji Stripe; null = rozliczane zamówieniem w sklepie.</summary>
    public string? StripeSubscriptionItemId { get; set; }
    public string Status { get; set; } = TenantAddonStatus.Active;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
}

public static class TenantAddonStatus
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
}
