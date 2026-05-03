using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ITrainerConfigService
{
    Task<TrainerConfigDto> GetAsync(string trainerUserId);
    Task SaveAsync(string trainerUserId, TrainerConfigDto dto);
}
