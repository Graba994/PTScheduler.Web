namespace PTScheduler.Domain.Entities;

/// <summary>Latest quiz result for a student on a given lesson.</summary>
public class QuizAttempt
{
    public int Id { get; set; }

    public string ApplicationUserId { get; set; } = string.Empty;

    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public int ScorePercent { get; set; }
    public bool Passed { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}
