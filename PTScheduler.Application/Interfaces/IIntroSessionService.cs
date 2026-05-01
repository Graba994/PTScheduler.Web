using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IIntroSessionService
{
    Task<IntroSessionConfigDto?> GetConfigAsync(string trainerUserId);
    Task SaveConfigAsync(string trainerUserId, SaveIntroConfigDto dto);
    Task<bool> HasBookedIntroAsync(int clientId);
}
