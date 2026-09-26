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

    /// <summary>
    /// Sekretny token adresu subskrypcji kalendarza (ICS) z wizytami trenera.
    /// Null = subskrypcja jeszcze niewłączona. Zmiana tokenu unieważnia stary link.
    /// </summary>
    public string? CalendarFeedToken { get; set; }
}
