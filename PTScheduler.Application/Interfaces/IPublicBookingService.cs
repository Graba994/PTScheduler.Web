using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IPublicBookingService
{
    /// <summary>
    /// Returns the active intro session offer. If no trainer has an active config,
    /// IsAvailable=false. Picks the first active config (lowest Id) for now — multi-trainer
    /// picker is planned but out of scope for MVP.
    /// </summary>
    Task<PublicIntroOfferDto> GetActiveOfferAsync();

    /// <summary>
    /// Returns up to <paramref name="daysAhead"/> days of available booking slots starting
    /// from <see cref="DateOnly"/> today + 1. Days with no free slots are excluded from the result.
    /// </summary>
    Task<List<BookingDayDto>> GetUpcomingSlotsAsync(string trainerUserId, int durationMinutes, int daysAhead = 14);

    /// <summary>
    /// Creates a Pending client + scheduled intro session. Sends confirmation email to the client
    /// (with a one-time set-password link) and a notification to the trainer.
    /// <paramref name="appBaseUrl"/> is the absolute URL root (e.g. "https://pt.example.com")
    /// used to build links inside the email.
    /// </summary>
    Task<BookingResultDto> CreateBookingAsync(CreatePublicBookingDto dto, string appBaseUrl);
}
