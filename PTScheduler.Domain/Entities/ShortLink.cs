namespace PTScheduler.Domain.Entities;

/// <summary>
/// Krótki link do SMS-ów i wiadomości (np. /l/k7Pq2xMa → /my?termin=…). Prowadzi tylko do ścieżki
/// w tej aplikacji; liczymy kliknięcia, żeby trener widział, czy zaproszenie zadziałało.
/// </summary>
public class ShortLink
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>Ścieżka w aplikacji, zawsze zaczyna się od „/”.</summary>
    public string TargetPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public int Clicks { get; set; }
    public DateTime? LastClickAt { get; set; }
}
