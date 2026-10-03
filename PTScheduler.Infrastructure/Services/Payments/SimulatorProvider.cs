using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

/// <summary>
/// Built-in test gateway. Always "configured" and always sandbox. Redirects the
/// buyer to an internal confirmation page where they can simulate paying or
/// cancelling — a full end-to-end flow without any real money or credentials.
/// </summary>
public sealed class SimulatorProvider : IPaymentProvider
{
    public string Key => PaymentProviders.Simulator;

    public bool IsConfigured(ProviderRuntimeConfig cfg) => true;

    public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
    {
        var baseApp = ctx.AppBaseUrl.TrimEnd('/');
        var url = $"{baseApp}/payments/sim/{ctx.Order.ExtOrderId}";
        return Task.FromResult(new ProviderCheckoutResult(true, url, null));
    }

    // The simulator is completed via the internal page (PaymentService.CompleteSimulatorAsync),
    // not through a webhook, so notify is a no-op.
    public Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
        => Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
}
