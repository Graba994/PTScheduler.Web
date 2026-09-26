using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

public class QuizQuestion
{
    public int Id { get; set; }

    public int LessonId { get; set; }
    public Lesson Lesson { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public QuizQuestionType Type { get; set; } = QuizQuestionType.SingleChoice;
    public int SortOrder { get; set; } = 0;

    public ICollection<QuizOption> Options { get; set; } = [];
}
