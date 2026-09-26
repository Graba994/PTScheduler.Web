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
    /// <param name="excludeSessionId">
    /// Sesja pomijana w kontroli — używane przy przenoszeniu, żeby sesja nie
    /// kolidowała sama ze sobą (jej stary rekord wciąż jest w bazie).
    /// </param>
    Task<bool> IsSlotFreeAsync(string trainerUserId, DateTime start, int durationMinutes, int? excludeSessionId = null);

    /// <summary>
    /// Zwraca pierwszą sesję kolidującą z proponowanym terminem, albo null gdy
    /// termin jest wolny. Jak <see cref="IsSlotFreeAsync"/>, ale z detalami do
    /// komunikatu dla trenera.
    /// </summary>
    Task<SlotConflictDto?> FindConflictAsync(string trainerUserId, DateTime start, int durationMinutes, int? excludeSessionId = null);
}
