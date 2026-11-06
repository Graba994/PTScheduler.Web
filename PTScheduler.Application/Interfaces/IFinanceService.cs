using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IFinanceService
{
    Task SetPackageHiddenAsync(int packageId, bool hidden);
    Task SetSessionHiddenAsync(int sessionId, bool hidden);
    /// <summary>Treningi poza pakietem opłacone u trenera (gotówka, przelew, karta) od <paramref name="fromUtc"/>.
    /// <paramref name="hidden"/>: false — zwykłe finanse, true — tylko ukryte, null — wszystkie.</summary>
    Task<List<OffPackagePaymentDto>> GetOffPackagePaymentsAsync(DateTime fromUtc, string? trainerUserId, bool? hidden);
    /// <summary>Poprawka ręcznie wpisanej płatności pakietu (status, sposób, data, wartość). Płatności automatycznych nie rusza.</summary>
    Task UpdatePackagePaymentAsync(int packageId, ManualPaymentEdit edit);
    /// <summary>Poprawka ręcznie wpisanej płatności za trening poza pakietem (status, sposób, data).</summary>
    Task UpdateSessionPaymentAsync(int sessionId, ManualPaymentEdit edit);
    Task<bool> VerifyPinAsync(string pin);
    Task SetPinAsync(string pin);
    Task<bool> IsPinSetAsync();
    Task<FinanceTaxConfigDto> GetTaxConfigAsync(string module);
    Task SaveTaxConfigAsync(FinanceTaxConfigDto dto);
}
