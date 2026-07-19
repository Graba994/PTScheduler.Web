using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public record PaymentInitResult(bool Ok, string? RedirectUrl, string? Error);

public interface IPaymentService
{
    /// <summary>
    /// Creates an order for a course purchase and returns the PayU redirect URL.
    /// </summary>
    Task<PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string appBaseUrl, string buyerEmail, string customerIp);

    /// <summary>
    /// Handles a PayU notify (webhook): verifies signature, updates the order and,
    /// when the payment is completed, grants course access. Returns true if accepted.
    /// </summary>
    Task<bool> HandleNotifyAsync(string rawBody, string? signatureHeader);

    Task<List<OrderDto>> GetMyOrdersAsync(string userId);
}
