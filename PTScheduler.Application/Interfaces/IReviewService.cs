using PTScheduler.Application.Marketing;

namespace PTScheduler.Application.Interfaces;

public interface IReviewService
{
    Task<ReviewPromptDto> GetPromptAsync(int clientId);
    Task<(bool Ok, string? Error)> SubmitAsync(int clientId, int rating, string? text, bool publishConsent);
    Task SnoozeAsync(int clientId, int days = 30);
    Task MarkGoogleClickedAsync(int clientId);

    Task<List<ClientReviewDto>> GetAllAsync(string? trainerUserId = null);
    /// <summary>Publikacja na stronie — tylko opinie ze zgodą klienta i z treścią.</summary>
    Task<bool> SetPublishedAsync(int reviewId, bool published);
    Task<bool> DeleteAsync(int reviewId);

    Task<PublicReviewsDto> GetPublicAsync(int take = 6);
}

public interface IAppFeedbackService
{
    /// <summary>Czy pokazać trenerowi prośbę o ocenę aplikacji (raz na pół roku).</summary>
    Task<bool> ShouldAskAsync();
    /// <summary>Wysyła ocenę do portalu właściciela platformy.</summary>
    Task<(bool Ok, string? Error)> SendAsync(AppFeedbackDto dto, string authorEmail);
    Task SnoozeAsync();
}
