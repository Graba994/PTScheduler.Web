namespace PTScheduler.Application.DTOs;

public class ModuleDto
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<LessonDto> Lessons { get; set; } = [];
}

public class LessonDto
{
    public int Id { get; set; }
    public int ModuleId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? VideoUrl { get; set; }
    public string? ContentHtml { get; set; }
    // Student view: whether the current student has completed this lesson.
    public bool IsCompleted { get; set; }
}

public class SaveLessonDto
{
    public string Title { get; set; } = string.Empty;
    public string? VideoUrl { get; set; }
    public string? ContentHtml { get; set; }
}
