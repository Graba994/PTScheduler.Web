namespace PTScheduler.Domain.Entities;

public class AcademyCourse
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }

    public bool IsPublished { get; set; } = false;

    // Default access length granted on enrollment, in days (e.g. 6 months = 183).
    // Null = lifetime access.
    public int? DefaultAccessDays { get; set; }

    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AcademyModule> Modules { get; set; } = [];
    public ICollection<AcademyEnrollment> Enrollments { get; set; } = [];
}
