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
}
