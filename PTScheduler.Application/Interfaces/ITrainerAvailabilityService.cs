using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ITrainerAvailabilityService
{
    Task<List<TrainerAvailabilityDto>> GetAvailabilityRulesAsync(string trainerUserId);
    Task<TrainerAvailabilityDto> AddRuleAsync(CreateTrainerAvailabilityDto dto);
    Task DeleteRuleAsync(int id);
    Task SetActiveAsync(int id, bool isActive);

    Task<TrainerConfigDto> GetConfigAsync(string trainerUserId);
    Task SaveConfigAsync(string trainerUserId, TrainerConfigDto dto);

    /// <summary>
    /// Returns available booking slots for the given trainer, date, and session duration.
    /// Excludes already-booked slots (including break time).
    /// </summary>
    Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(string trainerUserId, DateOnly date, int sessionDurationMinutes);

    /// <summary>
    /// Returns true if the trainer has no conflicting session in [start, start+durationMin].
    /// </summary>
    Task<bool> IsSlotFreeAsync(string trainerUserId, DateTime start, int durationMinutes);
}
