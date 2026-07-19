namespace PTScheduler.Domain.Entities;

public class Lesson
{
    public int Id { get; set; }

    public int ModuleId { get; set; }
    public CourseModule Module { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;

    // Video source URL (YouTube / Bunny / Google Drive). Rendered as an embed.
    public string? VideoUrl { get; set; }

    // Rich lesson materials (HTML/CSS).
    public string? ContentHtml { get; set; }

    // Auto-graded quiz: minimum percent of correct questions to pass.
    public int QuizPassThreshold { get; set; } = 70;

    public ICollection<QuizQuestion> QuizQuestions { get; set; } = [];
}
