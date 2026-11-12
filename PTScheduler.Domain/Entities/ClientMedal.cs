namespace PTScheduler.Domain.Entities;

/// <summary>Medal zdobyty przez klienta (klucz z MedalRules). Zostaje na zawsze; SeenAt — klient zobaczył gratulacje.</summary>
public class ClientMedal
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTime EarnedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SeenAt { get; set; }
    /// <summary>Wysłano powiadomienie push o medalu.</summary>
    public bool Notified { get; set; }
}
