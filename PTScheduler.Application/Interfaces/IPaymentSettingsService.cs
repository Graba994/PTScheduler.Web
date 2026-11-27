using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public interface IPaymentSettingsService
{
    Task<PaymentSettingsDto> GetAsync();
    Task SaveAsync(PaymentSettingsDto dto);
}
