using PTScheduler.Application.Surveys;
using PTScheduler.Domain.Enums;

namespace PTScheduler.Application.Interfaces;

public interface ISurveyService
{
    /// <summary>Szablon (zapisany albo domyślny z kodu).</summary>
    Task<SurveyTemplateDto> GetTemplateAsync(SurveyKind kind);
    Task<List<string>> SaveTemplateAsync(SurveyTemplateDto dto);
    Task ResetTemplateAsync(SurveyKind kind);

    /// <summary>Zapisuje odpowiedzi; zwraca błędy walidacji (pusta lista = zapisano). Powiadamia trenera.</summary>
    /// <param name="healthDataConsent">Wyraźna zgoda na dane o zdrowiu — wymagana dla ankiety zdrowotnej.</param>
    Task<(List<string> Errors, SurveyResponseDto? Response)> SubmitAsync(
        int clientId, SurveyKind kind, IReadOnlyDictionary<string, SurveyAnswerInput> answers, DateOnly? workoutDate = null,
        bool healthDataConsent = false);

    /// <summary>Wycofanie zgody: usuwa ankiety zdrowotne klienta (art. 7 ust. 3 i art. 17 RODO).</summary>
    Task WithdrawHealthConsentAsync(int clientId);

    Task<List<SurveyResponseDto>> GetResponsesAsync(int clientId, SurveyKind kind, int take = 60);
    Task<SurveyResponseDto?> GetLatestAsync(int clientId, SurveyKind kind);

    /// <summary>Czy klient powinien teraz wypełnić ankietę zdrowotną (włączona i jeszcze niewypełniona).</summary>
    Task<bool> IsHealthSurveyPendingAsync(int clientId);

    /// <summary>Dni treningowe klienta, dla których jest już ankieta po treningu.</summary>
    Task<Dictionary<DateOnly, SurveyResponseDto>> GetPostWorkoutByDateAsync(int clientId, int days = 90);

    Task MarkReviewedAsync(int responseId, string reviewerUserId);

    Task<PostWorkoutStats> GetPostWorkoutStatsAsync(int clientId);
}
