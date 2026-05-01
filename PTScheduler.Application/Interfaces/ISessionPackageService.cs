using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface ISessionPackageService
{
    Task<List<SessionPackageDto>> GetPackagesAsync(int clientId);
    Task<SessionPackageDto?> GetPackageAsync(int id);
    Task<SessionPackageDto> CreatePackageAsync(CreateSessionPackageDto dto);
    Task MarkPaidAsync(int packageId);
    Task CancelPackageAsync(int packageId);
    Task DeductCreditAsync(int packageId);
    Task ReturnCreditAsync(int packageId);
    Task<int> ExpireOldPackagesAsync();
    Task<List<ExpiringPackageDto>> GetExpiringAsync(int daysAhead = 14);
}
