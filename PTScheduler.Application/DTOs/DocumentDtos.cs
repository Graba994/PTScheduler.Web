namespace PTScheduler.Application.DTOs;

public sealed class ClientDocumentDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Kind { get; set; } = "required";
    public int Version { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
    /// <summary>Ilu aktywnych klientów zaakceptowało (albo zdecydowało — dla zgód) bieżącą wersję.</summary>
    public int AcceptedCount { get; set; }
    public int DeclinedCount { get; set; }
    public int ClientCount { get; set; }
}

/// <summary>Zapis dokumentu; <see cref="AskAgain"/> — po zmianie treści poproś wszystkich o ponowną akceptację.</summary>
public sealed class SaveClientDocumentDto
{
    public int? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Kind { get; set; } = "required";
    public bool AskAgain { get; set; } = true;
}

/// <summary>Stan dokumentu dla jednego klienta.</summary>
public sealed record ClientDocumentStatusDto(int DocumentId, string Title, string Kind, int Version, string Content,
    bool? Accepted, int? DecidedVersion, DateTime? DecidedAtUtc)
{
    /// <summary>Klient jeszcze nie zdecydował w bieżącej wersji.</summary>
    public bool Pending => DecidedVersion != Version;
}

/// <summary>Decyzja klienta w zestawieniu „kto zaakceptował”.</summary>
public sealed record DocumentDecisionDto(int ClientId, string ClientName, bool? Accepted, int? Version, DateTime? DecidedAtUtc);
