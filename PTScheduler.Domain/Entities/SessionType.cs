namespace PTScheduler.Domain.Entities;

public class SessionType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public bool IsGroup { get; set; }
    public int? MaxParticipants { get; set; }
    /// <summary>Trening w parze (dwie osoby na jednym treningu).</summary>
    public bool IsPair { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Cena pojedynczego treningu płaconego online przy rezerwacji (poza pakietem). Null = nie można.</summary>
    public decimal? SinglePrice { get; set; }
    /// <summary>Tylko z pakietem — bez pakietu klient nie zarezerwuje (np. zajęcia z karnetem).</summary>
    public bool RequiresPackage { get; set; }

    public ICollection<Session> Sessions { get; set; } = [];
}
