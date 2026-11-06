namespace PTScheduler.Portal.Entities;

/// <summary>Cotygodniowa opinia testera z aplikacji trenera: ocena tygodnia, co przeszkadzało i czego brakuje.</summary>
public class TesterFeedback
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    /// <summary>Tydzień programu (1 = po pierwszych 7 dniach).</summary>
    public int Week { get; set; }
    /// <summary>1–5.</summary>
    public int Rating { get; set; }
    public string? Annoyed { get; set; }
    public string? Missing { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
