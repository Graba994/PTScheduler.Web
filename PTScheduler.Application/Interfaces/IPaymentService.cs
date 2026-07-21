using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public record PaymentInitResult(bool Ok, string? RedirectUrl, string? Error);

/// <summary>A gateway offered to the buyer at checkout.</summary>
public record PaymentOptionDto(string Key, string Name, string Icon);

public interface IPaymentService
{
    /// <summary>Creates a course order on the chosen gateway and returns a redirect URL.</summary>
    Task<PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp);

    /// <summary>Creates a session-package order on the chosen gateway and returns a redirect URL.</summary>
    Task<PaymentInitResult> StartPackageCheckoutAsync(string userId, int packageOfferId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp);

    /// <summary>Handles a gateway webhook for the given provider. Returns true if accepted.</summary>
    Task<bool> HandleNotifyAsync(string providerKey, string rawBody, IReadOnlyDictionary<string, string> headers);

    /// <summary>Completes (or cancels) a Simulator order from the internal test page.</summary>
    Task<bool> CompleteSimulatorAsync(string extOrderId, bool paid);

    /// <summary>Order summary by external id (used by the Simulator test page).</summary>
    Task<OrderDto?> GetOrderByExtAsync(string extOrderId);

    /// <summary>Enabled &amp; usable gateways for the checkout picker.</summary>
    Task<List<PaymentOptionDto>> GetEnabledOptionsAsync();

    Task<List<OrderDto>> GetMyOrdersAsync(string userId);
}
