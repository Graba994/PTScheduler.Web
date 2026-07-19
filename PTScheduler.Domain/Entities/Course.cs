using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class Course
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    // Short plain-text summary shown on cards / listings.
    public string? Description { get; set; }
    // Rich HTML/CSS description shown on the course page (authored by admin/trainer).
    public string? DescriptionHtml { get; set; }
    public string? CoverImageUrl { get; set; }

    public bool IsPublished { get; set; } = false;

    public decimal Price { get; set; } = 0m;

    // Default access granted on a normal enrollment/purchase.
    public CourseAccessType DefaultAccessType { get; set; } = CourseAccessType.Lifetime;
    // Used when DefaultAccessType is Timed/Trial/Promotional. Null = no fixed length.
    public int? DefaultAccessDays { get; set; }

    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CourseModule> Modules { get; set; } = [];
    public ICollection<CourseEnrollment> Enrollments { get; set; } = [];
}
