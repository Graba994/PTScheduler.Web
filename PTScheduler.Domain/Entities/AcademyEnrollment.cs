namespace PTScheduler.Domain.Entities;

public class AcademyEnrollment
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public AcademyCourse Course { get; set; } = null!;

    // Identity user id of the enrolled student (Client role).
    public string ApplicationUserId { get; set; } = string.Empty;

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    // Null = never expires (lifetime access).
    public DateTime? ExpiresAt { get; set; }

    // Owner can revoke without deleting history.
    public bool IsRevoked { get; set; } = false;

    public bool IsActive =>
        !IsRevoked && (ExpiresAt is null || ExpiresAt > DateTime.UtcNow);
}
