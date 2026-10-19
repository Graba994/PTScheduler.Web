using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IFinanceService
{
    Task SetPackageHiddenAsync(int packageId, bool hidden);
    Task SetSessionHiddenAsync(int sessionId, bool hidden);
    /// <summary>Treningi poza pakietem opłacone u trenera (gotówka, przelew, karta) od <paramref name="fromUtc"/>.
    /// <paramref name="hidden"/>: false — zwykłe finanse, true — tylko ukryte, null — wszystkie.</summary>
    Task<List<OffPackagePaymentDto>> GetOffPackagePaymentsAsync(DateTime fromUtc, string? trainerUserId, bool? hidden);
    Task<bool> VerifyPinAsync(string pin);
    Task SetPinAsync(string pin);
    Task<bool> IsPinSetAsync();
    Task<FinanceTaxConfigDto> GetTaxConfigAsync(string module);
    Task SaveTaxConfigAsync(FinanceTaxConfigDto dto);
}
