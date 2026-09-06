using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

public sealed class AutoPayProvider(ILogger<AutoPayProvider> logger) : IPaymentProvider
{
    private const string SandboxGateway = "https://pay-accept.autopay.eu/payment";
    private const string ProdGateway = "https://pay.autopay.eu/payment";

    public string Key => PaymentProviders.AutoPay;

    public bool IsConfigured(ProviderRuntimeConfig cfg) =>
        cfg.Has("ServiceId", "SharedKey");

    public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg))
            return Task.FromResult(new ProviderCheckoutResult(false, null, "Brak kompletnej konfiguracji Autopay."));

        var gatewayUrl = cfg.Sandbox ? SandboxGateway : ProdGateway;
        var order = ctx.Order;
        var serviceId = cfg.Get("ServiceId");
        var sharedKey = cfg.Get("SharedKey");
        var amount = order.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        var baseApp = ctx.AppBaseUrl.TrimEnd('/');

        var hash = Sha256Hex($"{serviceId}|{order.ExtOrderId}|{amount}|{order.Currency}|{ctx.BuyerEmail}|{sharedKey}");

        var qs = new StringBuilder(gatewayUrl);
        qs.Append("?ServiceID=").Append(Uri.EscapeDataString(serviceId));
        qs.Append("&OrderID=").Append(Uri.EscapeDataString(order.ExtOrderId));
        qs.Append("&Amount=").Append(Uri.EscapeDataString(amount));
        qs.Append("&Description=").Append(Uri.EscapeDataString(ctx.ItemName));
        qs.Append("&GatewayID=0");
        qs.Append("&Currency=").Append(Uri.EscapeDataString(order.Currency));
        qs.Append("&CustomerEmail=").Append(Uri.EscapeDataString(ctx.BuyerEmail));
        qs.Append("&ReturnURL=").Append(Uri.EscapeDataString($"{baseApp}/payment/success?order={order.ExtOrderId}"));
        qs.Append("&NotificationURL=").Append(Uri.EscapeDataString($"{baseApp}/payments/autopay/notify"));
        qs.Append("&Hash=").Append(hash);

        return Task.FromResult(new ProviderCheckoutResult(true, qs.ToString(), null, null));
    }

    public Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
    {
        if (!cfg.Has("ServiceId", "SharedKey"))
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var serviceId = root.TryGetProperty("serviceID", out var sid) ? sid.GetString() ?? "" : "";
            var orderId = root.TryGetProperty("orderID", out var oid) ? oid.GetString() : null;
            var remoteId = root.TryGetProperty("remoteID", out var rid) ? rid.GetString() ?? "" : "";
            var amount = root.TryGetProperty("amount", out var amt) ? amt.GetString() ?? "" : "";
            var currency = root.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "" : "";
            var gatewayId = root.TryGetProperty("gatewayID", out var gid) ? gid.GetString() ?? "" : "";
            var paymentDate = root.TryGetProperty("paymentDate", out var pd) ? pd.GetString() ?? "" : "";
            var paymentStatus = root.TryGetProperty("paymentStatus", out var ps) ? ps.GetString() ?? "" : "";
            var paymentStatusDetail = root.TryGetProperty("paymentStatusDetail", out var psd) ? psd.GetString() ?? "" : "";
            var incomingHash = root.TryGetProperty("hash", out var h) ? h.GetString() : null;

            var sharedKey = cfg.Get("SharedKey");
            var expected = Sha256Hex($"{serviceId}|{orderId}|{remoteId}|{amount}|{currency}|{gatewayId}|{paymentDate}|{paymentStatus}|{paymentStatusDetail}|{sharedKey}");

            if (!string.Equals(incomingHash, expected, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("AutoPay ITN hash mismatch");
                return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
            }

            var outcome = paymentStatus switch
            {
                "SUCCESS" => PaymentOutcome.Paid,
                "FAILURE" => PaymentOutcome.Failed,
                _ => PaymentOutcome.Pending
            };

            return Task.FromResult(new ProviderNotifyResult(true, orderId, outcome));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AutoPay ITN parse failed");
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
        }
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
