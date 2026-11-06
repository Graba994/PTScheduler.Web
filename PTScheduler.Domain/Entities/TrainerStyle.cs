namespace PTScheduler.Domain.Entities;

/// <summary>
/// Jak trener pisze do klientów — podstawa szkiców wiadomości od asystenta.
/// Profil (forma, emoji, długość, powitanie, podpis) plus przykłady: wpisane przez trenera
/// i zapamiętane z jego poprawek szkiców (<see cref="TrainerStyleExample"/>).
/// </summary>
public class TrainerStyle
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    /// <summary>„ty” (na Ty) albo „pan” (Pan/Pani — bez zgadywania płci: „Dzień dobry”, formy bezosobowe).</summary>
    public string Address { get; set; } = "ty";
    /// <summary>„brak”, „czasem”, „często”.</summary>
    public string Emoji { get; set; } = "czasem";
    /// <summary>„krotko” (1–2 zdania) albo „srednio” (3–4 zdania).</summary>
    public string Length { get; set; } = "krotko";
    /// <summary>Ulubione powitanie, np. „Hej”, „Cześć”, „Dzień dobry”.</summary>
    public string? Greeting { get; set; }
    /// <summary>Podpis, np. „Trener Janek”.</summary>
    public string? Signature { get; set; }
    /// <summary>Własny opis stylu, np. „luźno, z humorem, konkretnie”.</summary>
    public string? Notes { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Przykład wiadomości trenera (imię klienta zamienione na [klient]).</summary>
public class TrainerStyleExample
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Cel wiadomości (DraftPurpose) albo null dla przykładu ogólnego.</summary>
    public string? Purpose { get; set; }
    /// <summary>„wzór” — wpisany przez trenera; „poprawka” — szkic poprawiony i wysłany przez trenera.</summary>
    public string Source { get; set; } = "wzór";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
