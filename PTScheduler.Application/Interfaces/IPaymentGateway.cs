namespace PTScheduler.Application.Interfaces;

public interface IPaymentGateway
{
    string ProviderName { get; }
    bool IsConfigured { get; }

    Task<PaymentRedirect> CreatePaymentAsync(PaymentRequest request);
    Task<PaymentNotification?> ParseNotificationAsync(Stream body, string? signatureHeader);
}

public class PaymentRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string CustomerIp { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
    public string ContinueUrl { get; set; } = string.Empty;
}

public class PaymentRedirect
{
    public string RedirectUrl { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
}

public class PaymentNotification
{
    public string ExternalOrderId { get; set; } = string.Empty;
    public string InternalOrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
