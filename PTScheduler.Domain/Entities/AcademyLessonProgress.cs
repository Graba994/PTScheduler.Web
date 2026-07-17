namespace PTScheduler.Domain.Entities;

public class AcademyLessonProgress
{
    public int Id { get; set; }

    public int LessonId { get; set; }
    public AcademyLesson Lesson { get; set; } = null!;

    // Identity user id of the student.
    public string ApplicationUserId { get; set; } = string.Empty;

    public bool IsCompleted { get; set; } = false;
    public DateTime? CompletedAt { get; set; }
}
