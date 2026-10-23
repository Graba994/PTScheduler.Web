namespace PTScheduler.Domain.Entities;

/// <summary>
/// Zaproszenie klienta na wolne okienko trenera (wiadomość w aplikacji albo SMS). Służy też jako hamulec:
/// tego samego klienta nie zapraszamy częściej niż raz na kilka dni.
/// </summary>
public class SlotInvitation
{
    public int Id { get; set; }
    public string TrainerUserId { get; set; } = string.Empty;
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    /// <summary>Wolne okienko (zegar ścienny, jak Session.StartTime).</summary>
    public DateTime SlotStart { get; set; }
    public DateTime SlotEnd { get; set; }
    /// <summary>„app” (wiadomość w aplikacji z powiadomieniem) albo „sms”.</summary>
    public string Channel { get; set; } = "app";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public int? ShortLinkId { get; set; }
    public ShortLink? ShortLink { get; set; }
}
