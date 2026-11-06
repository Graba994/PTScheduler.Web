using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.DTOs;

public class QuizOptionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; } // filled for admin; stripped for students
    public int SortOrder { get; set; }
}

public class QuizQuestionDto
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuizQuestionType Type { get; set; } = QuizQuestionType.SingleChoice;
    public int SortOrder { get; set; }
    public List<QuizOptionDto> Options { get; set; } = [];
}

public class QuizDto
{
    public int LessonId { get; set; }
    public int PassThreshold { get; set; } = 70;
    public List<QuizQuestionDto> Questions { get; set; } = [];
}

public class QuizAnswerDto
{
    public int QuestionId { get; set; }
    public List<int> SelectedOptionIds { get; set; } = [];
}

public class QuizResultDto
{
    public int ScorePercent { get; set; }
    public bool Passed { get; set; }
    public int PassThreshold { get; set; }
    public int CorrectCount { get; set; }
    public int TotalCount { get; set; }
    public HashSet<int> CorrectQuestionIds { get; set; } = [];
}

public class QuizAttemptDto
{
    public int ScorePercent { get; set; }
    public bool Passed { get; set; }
    public DateTime AttemptedAt { get; set; }
}
