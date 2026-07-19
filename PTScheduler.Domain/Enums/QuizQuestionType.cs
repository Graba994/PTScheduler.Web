namespace PTScheduler.Domain.Enums;

public enum QuizQuestionType
{
    SingleChoice,   // exactly one correct option (also used for true/false)
    MultipleChoice  // one or more correct options; must match exactly
}
