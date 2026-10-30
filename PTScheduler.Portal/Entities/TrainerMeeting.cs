namespace PTScheduler.Portal.Entities;

/// <summary>
/// Notatka ze spotkania z trenerem (Panel → Spotkania): jak pracuje dziś, co zainteresowało go w prezentacji,
/// obiekcje, wynik i następny krok. Pola testu nazwy zostają dla starszych notatek (nazwa Hantly jest już wybrana).
/// </summary>
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

    /// <summary>Zgoda na start z kodem zaproszenia.</summary>
    public bool Committed { get; set; }
    public string? InviteCode { get; set; }
    public string? Notes { get; set; }

    // ── Rozmowa sprzedażowa (od 2026-11) ─────────────────────────────────
    /// <summary>Jak pracuje: trener personalny, studio, online, fizjoterapeuta…</summary>
    public string? TrainerType { get; set; }
    /// <summary>Czego używa dziś do zapisów i rozliczeń (nazwy narzędzi).</summary>
    public string? CurrentTools { get; set; }
    /// <summary>Ile dziś płaci miesięcznie za narzędzia (zł).</summary>
    public int? CurrentCost { get; set; }
    /// <summary>Co zainteresowało go w prezentacji (klucze slajdów po przecinku).</summary>
    public string? InterestedFeatures { get; set; }
    /// <summary>Obiekcje, które padły (klucze po przecinku).</summary>
    public string? Objections { get; set; }
    /// <summary>Który plan pasuje (id planu).</summary>
    public string? PlanFit { get; set; }
    /// <summary>Wynik: start (zaczyna), followup (wracamy do tematu), no (nie teraz).</summary>
    public string? Outcome { get; set; }
    /// <summary>Kiedy się odezwać (dla „wracamy do tematu”).</summary>
    public DateTime? FollowUpAt { get; set; }
    /// <summary>Czy pokazano prezentację aplikacji.</summary>
    public bool Presented { get; set; }
}
