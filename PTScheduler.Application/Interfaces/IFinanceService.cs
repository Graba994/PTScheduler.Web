using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IFinanceService
{
    Task SetPackageHiddenAsync(int packageId, bool hidden);
    Task<bool> VerifyPinAsync(string pin);
    Task SetPinAsync(string pin);
    Task<bool> IsPinSetAsync();
    Task<FinanceTaxConfigDto> GetTaxConfigAsync(string module);
    Task SaveTaxConfigAsync(FinanceTaxConfigDto dto);
}
