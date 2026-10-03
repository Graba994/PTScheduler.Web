namespace PTScheduler.Domain.Entities;

/// <summary>
/// Dokument do akceptacji przez klienta w aplikacji: regulamin (wymagany) albo zgoda (np. na wizerunek),
/// na którą klient może się zgodzić albo nie. Zmiana treści podbija wersję — wtedy klienci akceptują ponownie.
/// </summary>
public class ClientDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Treść jako zwykły tekst (akapity oddzielone pustą linią).</summary>
    public string Content { get; set; } = string.Empty;
    /// <summary><see cref="Constants.DocumentKinds"/>: „required” (trzeba zaakceptować) albo „consent” (tak/nie).</summary>
    public string Kind { get; set; } = Constants.DocumentKinds.Required;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Decyzja klienta co do konkretnej wersji dokumentu (ostatnia decyzja się liczy).</summary>
public class DocumentAcceptance
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ClientDocument? Document { get; set; }
    public int Version { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    /// <summary>false = klient nie wyraził zgody (tylko dla zgód).</summary>
    public bool Accepted { get; set; } = true;
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
}
