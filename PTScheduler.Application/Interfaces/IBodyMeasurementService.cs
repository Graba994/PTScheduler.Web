using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IBodyMeasurementService
{
    Task<List<BodyMeasurementDto>> GetAsync(int clientId);
    /// <summary>Zapisuje pomiar; <see cref="ArgumentException"/> z opisem, gdy wartości są niepoprawne.</summary>
    Task<BodyMeasurementDto> AddAsync(CreateBodyMeasurementDto dto);
    Task DeleteAsync(int id);
    /// <summary>Klient usuwa własny wpis (tylko dodany przez siebie). False, gdy to nie jego pomiar.</summary>
    Task<bool> DeleteOwnAsync(int id, int clientId);
}
