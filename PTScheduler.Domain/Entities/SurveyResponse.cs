using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Wypełniona ankieta. Odpowiedzi jako JSON razem z treścią pytań z chwili
/// wypełnienia — późniejsza edycja szablonu nie zmienia historii.
/// </summary>
public class SurveyResponse
{
    public int Id { get; set; }
    public SurveyKind Kind { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }

    /// <summary>Dzień treningu (ankieta po treningu); null dla ankiety zdrowotnej.</summary>
    public DateOnly? WorkoutDate { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public string AnswersJson { get; set; } = "[]";

    /// <summary>Liczba odpowiedzi oznaczonych jako wymagające uwagi trenera.</summary>
    public int FlagCount { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
}
