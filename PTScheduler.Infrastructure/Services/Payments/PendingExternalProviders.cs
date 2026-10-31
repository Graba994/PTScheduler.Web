using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

/// <summary>
/// Base for external gateways whose configuration is fully supported (stored,
/// toggleable) but whose live redirect/notify wiring is being finalised. Selecting
/// one at checkout returns a clear localized message instead of silently failing.
/// Replace with a concrete integration when going live with that gateway.
/// </summary>
public abstract class PendingExternalProvider(string key, string displayName) : IPaymentProvider
{
    public string Key => key;

    public bool IsConfigured(ProviderRuntimeConfig cfg) => true;

    public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
        => Task.FromResult(new ProviderCheckoutResult(false, null,
            $"Bramka {displayName} jest skonfigurowana, ale integracja płatności jest w trakcie wdrażania. " +
            "Wybierz PayU lub Symulator (test)."));

    public Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
        => Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
}
