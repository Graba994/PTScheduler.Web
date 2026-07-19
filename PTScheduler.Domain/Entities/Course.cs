using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Course
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }

    public bool IsPublished { get; set; } = false;

    public decimal Price { get; set; } = 0m;

    // Default access granted on a normal enrollment/purchase.
    public CourseAccessType DefaultAccessType { get; set; } = CourseAccessType.Lifetime;
    // Used when DefaultAccessType is Timed/Trial/Promotional. Null = no fixed length.
    public int? DefaultAccessDays { get; set; }

    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CourseEnrollment> Enrollments { get; set; } = [];
}
