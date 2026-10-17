namespace PTScheduler.Portal.Entities;

/// <summary>SMS-y wysłane przez instancję w danym miesiącu w ramach limitu planu (bez kredytów dokupionych).</summary>
public class TenantSmsCounter
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    /// <summary>Miesiąc jako rrrrmm, np. 202609.</summary>
    public int Month { get; set; }
    public int Count { get; set; }
}
