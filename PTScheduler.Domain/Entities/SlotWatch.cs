namespace PTScheduler.Domain.Entities;

/// <summary>
/// „Powiadom mnie o wolnym terminie”: klient wybiera dni i godziny, a gdy u jego trenera zwolni się termin
/// w tym zakresie (ktoś odwoła wizytę), dostaje powiadomienie z linkiem do rezerwacji. Jeden wpis na klienta.
/// </summary>
public class SlotWatch
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    /// <summary>Dni tygodnia jako bity: 1 = poniedziałek, 2 = wtorek, … 64 = niedziela.</summary>
    public int Days { get; set; }
    public TimeOnly FromTime { get; set; }
    public TimeOnly ToTime { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastNotifiedAt { get; set; }
}
