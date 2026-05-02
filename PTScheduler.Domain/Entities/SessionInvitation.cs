using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Invitation sent to a client to join a group/duet session.
/// The trainer or primary client can create invitations;
/// the invited client (or trainer on their behalf) accepts or declines.
/// </summary>
public class SessionInvitation
{
    public int Id { get; set; }

    public int SessionId { get; set; }
    public Session Session { get; set; } = null!;

    public int InvitedClientId { get; set; }
    public Client InvitedClient { get; set; } = null!;

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
    public string? ResponseNote { get; set; }

    // Who created this invitation (trainer userId or primary client's userId)
    public string CreatedByUserId { get; set; } = string.Empty;
}
