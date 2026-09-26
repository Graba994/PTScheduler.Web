using System.Text.Json;
using System.Text.Json.Serialization;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Surveys;

public enum SurveyQuestionType
{
    /// <summary>Skala liczbowa Min–Max (np. RPE 0–10, samopoczucie 1–5).</summary>
    Scale,
    YesNo,
    SingleChoice,
    MultiChoice,
    Number,
    Text
}

/// <summary>Pytanie ankiety. <see cref="Key"/> jest stały — po nim liczone są statystyki.</summary>
public sealed class SurveyQuestion
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Help { get; set; }
    public string? Section { get; set; }
    public SurveyQuestionType Type { get; set; } = SurveyQuestionType.Text;
    public bool Required { get; set; }

    // Skala / liczba
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 10;
    public string? MinLabel { get; set; }
    public string? MaxLabel { get; set; }
    public string? Unit { get; set; }

    // Wybór
    public List<string> Options { get; set; } = [];

    // Reguły „wymaga uwagi trenera”
    public bool FlagOnYes { get; set; }
    public decimal? FlagAtOrAbove { get; set; }
    public decimal? FlagAtOrBelow { get; set; }
    public List<string> FlagOptions { get; set; } = [];

    /// <summary>Pole zgody — wymagana odpowiedź „Tak”.</summary>
    public bool MustBeYes { get; set; }

    /// <summary>Pokazywać średnią/trend w statystykach trenera (pytania liczbowe).</summary>
    public bool ShowInStats { get; set; }
}

/// <summary>Odpowiedź z kopią treści pytania z chwili wypełnienia.</summary>
public sealed class SurveyAnswer
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Section { get; set; }
    public SurveyQuestionType Type { get; set; }
    public decimal? Number { get; set; }
    public bool? Bool { get; set; }
    public string? Text { get; set; }
    public List<string> Choices { get; set; } = [];
    public bool Flagged { get; set; }
    public int Max { get; set; }
    public string? Unit { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Number is null && Bool is null && string.IsNullOrWhiteSpace(Text) && Choices.Count == 0;

    /// <summary>Czytelna wartość: „7/10”, „Tak”, „Siła, Kondycja”.</summary>
    [JsonIgnore]
    public string Display => Type switch
    {
        SurveyQuestionType.Scale => Number is { } n ? $"{n:0.#}/{Max}" : "—",
        SurveyQuestionType.Number => Number is { } n ? $"{n:0.#}{(string.IsNullOrEmpty(Unit) ? "" : " " + Unit)}" : "—",
        SurveyQuestionType.YesNo => Bool is null ? "—" : Bool.Value ? "Tak" : "Nie",
        SurveyQuestionType.SingleChoice or SurveyQuestionType.MultiChoice => Choices.Count > 0 ? string.Join(", ", Choices) : "—",
        _ => string.IsNullOrWhiteSpace(Text) ? "—" : Text!
    };
}

public sealed class SurveyTemplateDto
{
    public int Id { get; set; }
    public SurveyKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Intro { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsCustomized { get; set; }
    public List<SurveyQuestion> Questions { get; set; } = [];
}

public sealed class SurveyResponseDto
{
    public int Id { get; set; }
    public SurveyKind Kind { get; set; }
    public int ClientId { get; set; }
    public DateOnly? WorkoutDate { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public List<SurveyAnswer> Answers { get; set; } = [];
    public int FlagCount { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    public SurveyAnswer? this[string key] => Answers.FirstOrDefault(a => a.Key == key);
}

public static class SurveyJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try { return JsonSerializer.Deserialize<T>(json, Options) ?? fallback; }
        catch (JsonException) { return fallback; }
    }
}
