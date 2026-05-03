using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISessionTypeService
{
    Task<List<SessionTypeDto>> GetAllAsync(bool includeInactive = false);
    Task<SessionTypeDto> CreateAsync(CreateSessionTypeDto dto);
    Task UpdateAsync(int id, CreateSessionTypeDto dto);
    Task SetActiveAsync(int id, bool isActive);
}
