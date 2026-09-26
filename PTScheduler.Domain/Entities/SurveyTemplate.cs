using PTScheduler.Domain.Enums;

namespace PTScheduler.Domain.Entities;

/// <summary>
/// Szablon ankiety (jeden na rodzaj). Pytania trzymane jako JSON (lista
/// SurveyQuestion z warstwy aplikacji) — edytowalne przez admina bez migracji.
/// Brak wiersza = domyślny szablon z kodu.
/// </summary>
public class SurveyTemplate
{
    public int Id { get; set; }
    public SurveyKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Intro { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string QuestionsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
