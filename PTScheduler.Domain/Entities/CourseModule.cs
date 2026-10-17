namespace PTScheduler.Domain.Entities;

public class CourseModule
{
    public int Id { get; set; }

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;

    public ICollection<Lesson> Lessons { get; set; } = [];
}
