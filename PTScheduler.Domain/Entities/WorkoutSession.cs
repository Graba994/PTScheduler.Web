namespace PTScheduler.Domain.Entities;

/// <summary>
/// Trwający (niezakończony) trening klienta zapisany po stronie serwera —
/// backup i sync między urządzeniami dla widoku „Trenuj". Klient ma najwyżej
/// jeden otwarty draft naraz (unikat po ClientId). Treść to zserializowany
/// JSON modelu roboczego z przeglądarki; serwer nie interpretuje jego pól,
/// tylko przechowuje i oddaje nowszą wersję. Po „Zakończ"/„Odrzuć" wiersz jest
/// usuwany, a wykonanie ląduje w <see cref="WorkoutLog"/>.
/// </summary>
public class WorkoutSession
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>Zserializowany draft treningu (JSON modelu roboczego przeglądarki).</summary>
    public string DraftJson { get; set; } = string.Empty;

    /// <summary>Instant ostatniego zapisu (UTC) — do wyboru nowszej wersji przy sync.</summary>
    public DateTime UpdatedAt { get; set; }
}
