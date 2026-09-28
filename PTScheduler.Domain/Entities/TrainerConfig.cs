using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Per-trainer global settings: breaks, contact visibility, etc.
/// One record per trainer, created on first save.
/// </summary>
public class TrainerConfig
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;

    // Minutes of technical break appended after every session
    public int BreakAfterSessionMinutes { get; set; } = 0;

    // Slot granularity for client booking picker (minutes, e.g. 15 or 30)
    public int SlotGranularityMinutes { get; set; } = 30;

    // If true: every client of this trainer can see every other client in contacts
    public bool AllowClientsDiscoverPeers { get; set; } = false;
    public int CancellationWindowHours { get; set; } = 24;

    /// <summary>Zasada dla odwołań klienta później niż CancellationWindowHours.</summary>
    public LateCancellationPolicy LateCancellationPolicy { get; set; } = LateCancellationPolicy.Block;

    /// <summary>Nieobecność bez odwołania (No-show) pobiera sesję z pakietu.</summary>
    public bool NoShowChargesSession { get; set; } = true;

    // ── Rezerwacja przez klienta bez pakietu ──
    /// <summary>Klient może zapłacić online za pojedynczy trening (gdy rodzaj treningu ma cenę).</summary>
    public bool OffPackageOnline { get; set; } = true;
    /// <summary>Klient może wybrać „Zapłacę u trenera” (gotówka, przelew).</summary>
    public bool OffPackageAtTrainer { get; set; } = true;
    /// <summary>„Zapłacę u trenera” zawsze wymaga akceptacji trenera (poza zaufanymi klientami).</summary>
    public bool OffPackageNeedsApproval { get; set; } = true;
    /// <summary>Bez akceptacji: najwyżej tyle nieopłaconych wizyt naraz (0 = bez limitu); powyżej — akceptacja.</summary>
    public int OffPackageUnpaidLimit { get; set; } = 1;

    /// <summary>
    /// Sekretny token adresu subskrypcji kalendarza (ICS) z wizytami trenera.
    /// Null = subskrypcja jeszcze niewłączona. Zmiana tokenu unieważnia stary link.
    /// </summary>
    public string? CalendarFeedToken { get; set; }
}
