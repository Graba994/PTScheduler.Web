using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IBodyMeasurementService
{
    Task<List<BodyMeasurementDto>> GetAsync(int clientId);
    Task<BodyMeasurementDto> AddAsync(CreateBodyMeasurementDto dto);
    Task DeleteAsync(int id);
}
