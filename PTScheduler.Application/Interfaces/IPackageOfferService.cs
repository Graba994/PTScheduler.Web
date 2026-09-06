using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IPackageOfferService
{
    Task<List<PackageOfferDto>> GetAllAsync(bool includeInactive = false);
    Task<List<PackageOfferDto>> GetActiveOffersAsync();
    Task<PackageOfferDto?> GetAsync(int id);
    Task<int> CreateAsync(SavePackageOfferDto dto, string userId);
    Task UpdateAsync(int id, SavePackageOfferDto dto);
    Task SetActiveAsync(int id, bool active);
}
