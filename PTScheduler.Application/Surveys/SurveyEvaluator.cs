namespace PTScheduler.Application.Surveys;

/// <summary>Surowa odpowiedź z formularza (przed walidacją).</summary>
public sealed class SurveyAnswerInput
{
    public decimal? Number { get; set; }
    public bool? Bool { get; set; }
    public string? Text { get; set; }
    public List<string> Choices { get; set; } = [];
}

/// <summary>Walidacja odpowiedzi i oznaczanie tych, które wymagają uwagi trenera.</summary>
public static class SurveyEvaluator
{
    public const int MaxTextLength = 2000;

    /// <returns>Lista błędów (pusta = poprawne) oraz odpowiedzi z flagami.</returns>
    public static (List<string> Errors, List<SurveyAnswer> Answers) Evaluate(
        IReadOnlyList<SurveyQuestion> questions, IReadOnlyDictionary<string, SurveyAnswerInput> inputs)
    {
        var errors = new List<string>();
        var answers = new List<SurveyAnswer>();

        foreach (var q in questions)
        {
            inputs.TryGetValue(q.Key, out var input);
            input ??= new SurveyAnswerInput();

            var a = new SurveyAnswer
            {
                Key = q.Key, Label = q.Label, Section = q.Section, Type = q.Type,
                Max = q.Max, Unit = q.Unit
            };

            switch (q.Type)
            {
                case SurveyQuestionType.Scale:
                case SurveyQuestionType.Number:
                    if (input.Number is { } n)
                    {
                        if (n < q.Min || n > q.Max)
                            errors.Add($"„{q.Label}” — podaj wartość od {q.Min} do {q.Max}.");
                        else
                            a.Number = n;
                    }
                    break;
                case SurveyQuestionType.YesNo:
                    a.Bool = input.Bool;
                    if (q.MustBeYes && input.Bool != true)
                        errors.Add($"„{q.Label}” — wymagane potwierdzenie.");
                    break;
                case SurveyQuestionType.SingleChoice:
                    var single = input.Choices.FirstOrDefault(c => q.Options.Contains(c));
                    if (single is not null) a.Choices = [single];
                    break;
                case SurveyQuestionType.MultiChoice:
                    a.Choices = input.Choices.Where(c => q.Options.Contains(c)).Distinct().ToList();
                    break;
                default:
                    var text = input.Text?.Trim();
                    a.Text = string.IsNullOrEmpty(text) ? null : text.Length > MaxTextLength ? text[..MaxTextLength] : text;
                    break;
            }

            if (q.Required && !q.MustBeYes && a.IsEmpty)
                errors.Add($"„{q.Label}” — to pytanie jest wymagane.");

            a.Flagged = IsFlagged(q, a);
            answers.Add(a);
        }

        return (errors, answers);
    }

    public static bool IsFlagged(SurveyQuestion q, SurveyAnswer a) =>
        (q.FlagOnYes && a.Bool == true)
        || (q.FlagAtOrAbove is { } hi && a.Number is { } n1 && n1 >= hi)
        || (q.FlagAtOrBelow is { } lo && a.Number is { } n2 && n2 <= lo)
        || (q.FlagOptions.Count > 0 && a.Choices.Any(q.FlagOptions.Contains));

    /// <summary>Sprawdzenie szablonu przed zapisem w edytorze.</summary>
    public static List<string> ValidateTemplate(SurveyTemplateDto t)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Name)) errors.Add("Podaj nazwę ankiety.");
        if (t.Questions.Count == 0) errors.Add("Ankieta musi mieć co najmniej jedno pytanie.");
        var keys = new HashSet<string>();
        foreach (var q in t.Questions)
        {
            if (string.IsNullOrWhiteSpace(q.Label)) errors.Add("Każde pytanie musi mieć treść.");
            if (string.IsNullOrWhiteSpace(q.Key) || !keys.Add(q.Key)) errors.Add($"Powtórzony lub pusty klucz pytania „{q.Label}”.");
            if (q.Type is SurveyQuestionType.SingleChoice or SurveyQuestionType.MultiChoice && q.Options.Count(o => !string.IsNullOrWhiteSpace(o)) < 2)
                errors.Add($"Pytanie „{q.Label}” potrzebuje co najmniej dwóch opcji.");
            if (q.Type is SurveyQuestionType.Scale or SurveyQuestionType.Number && q.Min >= q.Max)
                errors.Add($"Pytanie „{q.Label}”: minimum musi być mniejsze od maksimum.");
        }
        return errors.Distinct().ToList();
    }
}
