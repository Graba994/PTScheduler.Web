using PTScheduler.Application.DTOs;

namespace PTScheduler.Application.Interfaces;

public record PaymentInitResult(bool Ok, string? RedirectUrl, string? Error);

/// <summary>A gateway offered to the buyer at checkout.</summary>
public record PaymentOptionDto(string Key, string Name, string Icon);

public interface IPaymentService
{
    /// <summary>Creates a course order on the chosen gateway and returns a redirect URL.</summary>
    Task<PaymentInitResult> StartCourseCheckoutAsync(string userId, int courseId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, InvoiceBuyerDto? invoiceBuyer = null);

    /// <summary>Creates a session-package order on the chosen gateway and returns a redirect URL.</summary>
    /// <summary>Opłata bieżącego okresu karnetu cyklicznego.</summary>
    Task<PaymentInitResult> StartMembershipPeriodCheckoutAsync(string userId, int periodId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp);

    /// <summary>Zakup karnetu cyklicznego w sklepie (pierwszy okres).</summary>
    Task<PaymentInitResult> StartMembershipPlanCheckoutAsync(string userId, int planId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, InvoiceBuyerDto? invoiceBuyer = null);

    Task<PaymentInitResult> StartPackageCheckoutAsync(string userId, int packageOfferId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp, string? couponCode = null, InvoiceBuyerDto? invoiceBuyer = null, int? partnerClientId = null);

    /// <summary>Zakup bonu podarunkowego (bon utworzony wcześniej jako oczekujący).</summary>
    /// <summary>Opłata online za pojedynczy trening zarezerwowany poza pakietem (termin trzymany 15 min).</summary>
    Task<PaymentInitResult> StartSessionCheckoutAsync(string userId, int sessionId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp);
    Task<PaymentInitResult> StartGiftVoucherCheckoutAsync(string userId, int voucherId, string providerKey, string appBaseUrl, string buyerEmail, string customerIp);

    /// <summary>Handles a gateway webhook for the given provider (Ok = accepted; Body = odpowiedź wymagana przez bramkę).</summary>
    Task<PaymentNotifyResponse> HandleNotifyAsync(string providerKey, string rawBody, IReadOnlyDictionary<string, string> headers);

    /// <summary>Completes (or cancels) a Simulator order from the internal test page.</summary>
    Task<bool> CompleteSimulatorAsync(string extOrderId, bool paid);

    /// <summary>Order summary by external id (used by the Simulator test page).</summary>
    Task<OrderDto?> GetOrderByExtAsync(string extOrderId);

    /// <summary>Enabled &amp; usable gateways for the checkout picker.</summary>
    Task<List<PaymentOptionDto>> GetEnabledOptionsAsync();

    Task<List<OrderDto>> GetMyOrdersAsync(string userId);

    Task<List<OrderDto>> GetPaidOrdersAsync(DateTime from);
}

/// <summary>Wynik obsługi powiadomienia bramki; Body — treść, której bramka oczekuje w odpowiedzi (np. XML Autopay).</summary>
public sealed record PaymentNotifyResponse(bool Ok, string? Body = null, string? ContentType = null);
