namespace PTScheduler.Portal.Entities;

/// <summary>Notatka ze spotkania z trenerem (Panel → Spotkania): test nazwy, jak pracuje dziś, ceny i decyzja.</summary>
public class TrainerMeeting
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string TrainerName { get; set; } = "";
    /// <summary>Telefon albo e-mail — do wysłania zaproszenia i SMS-a z testem pamięci nazwy.</summary>
    public string? Contact { get; set; }
    public string? City { get; set; }

    /// <summary>Jak zapisali nazwy po usłyszeniu (dosłownie).</summary>
    public string? NameWritten { get; set; }
    /// <summary>Która nazwa najbardziej kojarzy się z aplikacją dla trenera.</summary>
    public string? NamePick { get; set; }
    /// <summary>Czy następnego dnia pamiętali nazwę (null = jeszcze nie sprawdzone).</summary>
    public bool? NameRecalled { get; set; }

    /// <summary>Jak dziś zapisują klientów (lista po przecinku).</summary>
    public string? BookingToday { get; set; }
    public int? Clients { get; set; }
    public int? NoShowsPerMonth { get; set; }
    public string? Pains { get; set; }
    /// <summary>Gdzie zatrzymali się w kreatorze (obserwacja z demo).</summary>
    public string? DemoNotes { get; set; }

    // Cztery pytania o cenę (zł miesięcznie).
    public int? PriceTooCheap { get; set; }
    public int? PriceBargain { get; set; }
    public int? PriceExpensive { get; set; }
    public int? PriceTooExpensive { get; set; }

    /// <summary>Zgodził(a) się wystartować z kodem zaproszenia.</summary>
    public bool Committed { get; set; }
    public string? InviteCode { get; set; }
    public string? Notes { get; set; }
}
