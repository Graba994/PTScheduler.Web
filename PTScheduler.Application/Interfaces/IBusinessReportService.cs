using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

/// <summary>Raporty biznesowe: retencja, przychód na klienta, obłożenie grafiku, prognoza i odwołania.</summary>
public interface IBusinessReportService
{
    /// <param name="periodDays">Długość okresu (np. 30, 90, 180 dni), porównywana z poprzednim takim samym okresem.</param>
    /// <param name="trainerUserId">Trener, którego dotyczy raport; null — całe studio.</param>
    Task<BusinessReportDto> GetAsync(int periodDays, string? trainerUserId);
}
