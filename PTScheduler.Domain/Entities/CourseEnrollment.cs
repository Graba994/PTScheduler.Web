using System.ComponentModel.DataAnnotations.Schema;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class CourseEnrollment
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    // Identity user id of the enrolled student.
    public string ApplicationUserId { get; set; } = string.Empty;

    public CourseAccessType AccessType { get; set; } = CourseAccessType.Lifetime;
    public EnrollmentSource Source { get; set; } = EnrollmentSource.Manual;

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; } // null = never expires (lifetime)

    public bool IsRevoked { get; set; } = false;

    public string? GrantedByUserId { get; set; }
    public string? Notes { get; set; }

    /// <summary>True when the access is currently usable.</summary>
    [NotMapped]
    public bool IsActive =>
        !IsRevoked
        && (StartsAt is null || StartsAt <= DateTime.UtcNow)
        && (ExpiresAt is null || ExpiresAt > DateTime.UtcNow);
}
