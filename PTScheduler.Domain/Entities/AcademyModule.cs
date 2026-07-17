namespace PTScheduler.Domain.Entities;

public class AcademyModule
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public AcademyCourse Course { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; } = 0;

    public ICollection<AcademyLesson> Lessons { get; set; } = [];
}
