using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class AcademyCourseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public bool IsPublished { get; set; }
    public int? DefaultAccessDays { get; set; }
    public int SortOrder { get; set; }
    public int ModuleCount { get; set; }
    public int LessonCount { get; set; }
    public int EnrollmentCount { get; set; }
}

public class AcademyModuleDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public List<AcademyLessonDto> Lessons { get; set; } = [];
}

public class AcademyLessonDto
{
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public VideoProvider VideoProvider { get; set; } = VideoProvider.None;
    public string? VideoRef { get; set; }
    public string? WorkbookUrl { get; set; }
    public int SortOrder { get; set; }
}

public class AcademyEnrollmentDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string ApplicationUserId { get; set; } = string.Empty;
    public string StudentEmail { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public DateTime EnrolledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public bool IsActive { get; set; }
}

// A student's view of one course they are enrolled in, with progress.
public class MyCourseDto
{
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int TotalLessons { get; set; }
    public int CompletedLessons { get; set; }
    public List<MyModuleDto> Modules { get; set; } = [];
}

public class MyModuleDto
{
    public int ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<MyLessonDto> Lessons { get; set; } = [];
}

public class MyLessonDto
{
    public int LessonId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

// Full lesson content for the student viewer (only served if access is verified).
public class MyLessonDetailDto
{
    public int LessonId { get; set; }
    public int CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public VideoProvider VideoProvider { get; set; } = VideoProvider.None;
    public string? VideoRef { get; set; }
    public string? WorkbookUrl { get; set; }
    public bool IsCompleted { get; set; }

    public int? PreviousLessonId { get; set; }
    public int? NextLessonId { get; set; }
}
