namespace PTScheduler.Domain.Entities;

/// <summary>Marks a lesson as completed by a given student.</summary>
public class LessonProgress
{
    public int Id { get; set; }

    public string ApplicationUserId { get; set; } = string.Empty;

    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}
