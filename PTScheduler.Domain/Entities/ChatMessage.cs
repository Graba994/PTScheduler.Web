namespace PTScheduler.Domain.Entities;

/// <summary>
/// Wiadomość w rozmowie trenera z klientem. Rozmowa jest przypisana do klienta —
/// uczestniczą w niej klient i jego trener (oraz admin studia).
/// </summary>
public class ChatMessage
{
    public long Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public string SenderUserId { get; set; } = string.Empty;
    /// <summary>Wiadomość od strony trenera (trener/admin), a nie klienta.</summary>
    public bool FromStaff { get; set; }

    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>Kiedy odbiorca przeczytał wiadomość (null = nieprzeczytana).</summary>
    public DateTime? ReadAt { get; set; }
}
