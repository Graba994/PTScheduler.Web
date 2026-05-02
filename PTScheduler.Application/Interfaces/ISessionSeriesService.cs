using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISessionSeriesService
{
    /// <summary>
    /// Preview what dates a series would create, including conflict list and package credit breakdown.
    /// </summary>
    Task<SeriesPreviewDto> PreviewAsync(CreateSessionSeriesDto dto);

    /// <summary>
    /// Create the series and generate all session records.
    /// skipConflicts: if true, conflicting dates are skipped silently; if false throws on conflict.
    /// </summary>
    Task<SessionSeriesDto> CreateAsync(CreateSessionSeriesDto dto, bool skipConflicts = true);

    Task<List<SessionSeriesDto>> GetSeriesForClientAsync(int clientId);
    Task<List<SessionSeriesDto>> GetSeriesForTrainerAsync(string trainerUserId);
    Task CancelSeriesAsync(int seriesId, bool cancelFutureSessions = true);
}
