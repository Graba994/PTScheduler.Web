using PTScheduler.Domain.Entities;

namespace PTScheduler.Infrastructure.Services.Payments;

public enum PaymentOutcome { Pending, Paid, Canceled, Failed }

/// <summary>Runtime config handed to a provider for one operation.</summary>
public sealed class ProviderRuntimeConfig
{
    public bool Sandbox { get; init; } = true;
    public string Currency { get; init; } = "PLN";
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    public string Get(string key) => Fields.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;
    public bool Has(params string[] keys) => keys.All(k => !string.IsNullOrWhiteSpace(Get(k)));
}

public sealed record ProviderCheckoutContext(
    Order Order,
    string ItemName,
    string BuyerEmail,
    string CustomerIp,
    string AppBaseUrl);

public sealed record ProviderCheckoutResult(
    bool Ok,
    string? RedirectUrl,
    string? Error,
    string? ProviderOrderId = null);

public sealed record ProviderNotifyResult(
    bool Valid,
    string? ExtOrderId,
    PaymentOutcome Outcome);

/// <summary>One payment gateway. Implementations are stateless and resolved by <see cref="Key"/>.</summary>
public interface IPaymentProvider
{
    string Key { get; }

    /// <summary>Whether the given config has everything required to run.</summary>
    bool IsConfigured(ProviderRuntimeConfig cfg);

    /// <summary>Create a payment and return a redirect URL for the buyer.</summary>
    Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg);

    /// <summary>Verify and interpret a gateway webhook. Returns the outcome to apply.</summary>
    Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg);
}
