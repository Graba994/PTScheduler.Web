namespace PTScheduler.Domain.Entities;

/// <summary>
/// Opinia klienta o trenerze/studiu (jedna na klienta, można ją edytować).
/// Na publicznej stronie pojawia się tylko za zgodą klienta i po akceptacji trenera.
/// </summary>
public class ClientReview
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    /// <summary>Ocena 1–5.</summary>
    public int Rating { get; set; }
    public string? Text { get; set; }

    /// <summary>Podpis na stronie, np. „Marek N.” — ustalany przy zapisie.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Zgoda klienta na publikację opinii (imię i inicjał) na stronie trenera.</summary>
    public bool PublishConsent { get; set; }
    /// <summary>Trener zaakceptował opinię do wyświetlania na stronie.</summary>
    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Kiedy klient kliknął „Dodaj opinię w Google”.</summary>
    public DateTime? GoogleClickedAt { get; set; }
}
