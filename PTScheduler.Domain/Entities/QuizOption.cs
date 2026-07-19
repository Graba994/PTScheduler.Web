namespace PTScheduler.Domain.Entities;

public class QuizOption
{
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public QuizQuestion Question { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; } = 0;
}
