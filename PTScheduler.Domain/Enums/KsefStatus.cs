namespace PTScheduler.Domain.Enums;

/// <summary>Stan faktury w Krajowym Systemie e-Faktur.</summary>
public enum KsefStatus
{
    /// <summary>Nie wysyłana do KSeF.</summary>
    None = 0,
    /// <summary>Wysłana — czeka na przetworzenie przez KSeF.</summary>
    Sent = 1,
    /// <summary>Przyjęta, ma numer KSeF.</summary>
    Accepted = 2,
    /// <summary>Odrzucona (błąd walidacji lub wysyłki) — szczegóły w KsefError.</summary>
    Rejected = 3
}
