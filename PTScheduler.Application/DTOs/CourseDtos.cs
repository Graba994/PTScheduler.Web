using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class CourseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DescriptionHtml { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? DurationText { get; set; }
    public string? Level { get; set; }
    public string? Author { get; set; }
    public bool IsPublished { get; set; }
    public decimal Price { get; set; }
    public CourseAccessType DefaultAccessType { get; set; }
    public int? DefaultAccessDays { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public int EnrollmentCount { get; set; }
    public int ActiveEnrollmentCount { get; set; }
}

public class SaveCourseDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DescriptionHtml { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? DurationText { get; set; }
    public string? Level { get; set; }
    public string? Author { get; set; }
    public bool IsPublished { get; set; }
    public decimal Price { get; set; }
    public CourseAccessType DefaultAccessType { get; set; } = CourseAccessType.Lifetime;
    public int? DefaultAccessDays { get; set; }
    public int SortOrder { get; set; }
}

public class CourseEnrollmentDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string ApplicationUserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public CourseAccessType AccessType { get; set; }
    public EnrollmentSource Source { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
}

public class GrantEnrollmentDto
{
    public int CourseId { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public CourseAccessType AccessType { get; set; } = CourseAccessType.Lifetime;
    // Optional explicit expiry; if null and AccessDays is set, expiry = now + AccessDays.
    public DateTime? ExpiresAt { get; set; }
    public int? AccessDays { get; set; }
    public string? Notes { get; set; }
}
