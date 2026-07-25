using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Session
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    public int SessionTypeId { get; set; }
    public SessionType SessionType { get; set; } = null!;
    public string TrainerUserId { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }
    public SessionStatus Status { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public int? PackageId { get; set; }
    public SessionPackage? Package { get; set; }

    public int? SeriesId { get; set; }
    public SessionSeries? Series { get; set; }

    public string? MeetingUrl { get; set; }
    public string? CalendarEventId { get; set; }

    /// <summary>
    /// Set when the 24h reminder email was successfully delivered.
    /// Used as persistent dedup so a service restart doesn't double-send.
    /// </summary>
    public DateTime? ReminderSentAt { get; set; }

    public ICollection<SessionInvitation> Invitations { get; set; } = [];
}
