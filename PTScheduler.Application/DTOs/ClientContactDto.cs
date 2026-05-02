namespace PTScheduler.Application.DTOs;

public class ClientContactDto
{
    public int Id { get; set; }
    public int ContactClientId { get; set; }
    public string ContactClientName { get; set; } = string.Empty;
    public string? ContactClientEmail { get; set; }
}

public class SessionInvitationDto
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public DateTime SessionStartTime { get; set; }
    public string SessionTypeName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string TrainerName { get; set; } = string.Empty;
    public int InvitedClientId { get; set; }
    public string InvitedClientName { get; set; } = string.Empty;
    public PTScheduler.Domain.Enums.InvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? ResponseNote { get; set; }
}
