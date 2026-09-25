namespace PTScheduler.Application.DTOs;

public enum AttentionKind
{
    NoVisit,
    PackageEmpty,
    LowCredits,
    PackageExpiring,
    NotTraining,
    NewWorkout
}

public enum AttentionSeverity
{
    Info = 0,
    Warning = 1,
    Danger = 2
}

public sealed record AttentionReason(AttentionKind Kind, AttentionSeverity Severity, string Text, string? Link = null);

/// <summary>Klient z listy „Wymaga uwagi” na pulpicie trenera wraz z powodami.</summary>
public sealed class ClientAttentionDto
{
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public List<AttentionReason> Reasons { get; set; } = [];
    public AttentionSeverity Severity => Reasons.Count == 0 ? AttentionSeverity.Info : Reasons.Max(r => r.Severity);
}
