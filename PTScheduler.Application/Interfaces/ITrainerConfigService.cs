using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ITrainerConfigService
{
    Task<TrainerConfigDto> GetAsync(string trainerUserId);
    Task SaveAsync(string trainerUserId, TrainerConfigDto dto);

    /// <summary>Token subskrypcji kalendarza (tworzony przy pierwszym użyciu).</summary>
    Task<string> GetOrCreateCalendarFeedTokenAsync(string trainerUserId);

    /// <summary>Nowy token — stary link przestaje działać.</summary>
    Task<string> RegenerateCalendarFeedTokenAsync(string trainerUserId);

    /// <summary>Trener, do którego należy token; null gdy nieznany.</summary>
    Task<string?> FindTrainerByCalendarFeedTokenAsync(string token);
}
