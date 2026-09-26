namespace PTScheduler.Application.DTOs;

/// <summary>A course as seen by a student who has access, with progress.</summary>
public class StudentCourseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? DurationText { get; set; }
    public string? Level { get; set; }
    public string? Author { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int ProgressPercent => TotalLessons == 0 ? 0 : (int)Math.Round(100.0 * CompletedLessons / TotalLessons);
}

public class StudentCourseDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? DescriptionHtml { get; set; }
    public List<ModuleDto> Modules { get; set; } = [];
    public int TotalLessons { get; set; }
    public int CompletedLessons { get; set; }
    public int ProgressPercent => TotalLessons == 0 ? 0 : (int)Math.Round(100.0 * CompletedLessons / TotalLessons);
}
