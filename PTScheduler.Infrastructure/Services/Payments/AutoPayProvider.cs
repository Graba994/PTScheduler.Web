using Microsoft.Extensions.Logging;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Infrastructure.Services.Payments;

/// <summary>
/// Autopay (dawniej Blue Media): klient przechodzi na bramkę Autopay, a wynik przychodzi w ITN
/// na /payments/autopay/notify. Adres ITN i powrotu (/payments/autopay/return) trener wpisuje
/// raz w panelu Autopay — pokazujemy je w ustawieniach płatności.
/// </summary>
public sealed class AutoPayProvider(ILogger<AutoPayProvider> logger) : IPaymentProvider
{
    public string Key => PaymentProviders.AutoPay;

    public bool IsConfigured(ProviderRuntimeConfig cfg) =>
        cfg.Has("ServiceId", "SharedKey");

    public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutContext ctx, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg))
            return Task.FromResult(new ProviderCheckoutResult(false, null, "Brak kompletnej konfiguracji Autopay."));

        var order = ctx.Order;
        var url = AutopayProtocol.StartUrl(cfg.Sandbox, cfg.Get("ServiceId").Trim(), cfg.Get("SharedKey").Trim(),
            order.ExtOrderId, order.Amount, ctx.ItemName, order.Currency, ctx.BuyerEmail);
        return Task.FromResult(new ProviderCheckoutResult(true, url, null, null));
    }

    public Task<ProviderNotifyResult> HandleNotifyAsync(string rawBody, IReadOnlyDictionary<string, string> headers, ProviderRuntimeConfig cfg)
    {
        if (!IsConfigured(cfg))
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));

        var serviceId = cfg.Get("ServiceId").Trim();
        var sharedKey = cfg.Get("SharedKey").Trim();
        var payload = AutopayProtocol.TransactionsFromForm(rawBody);
        var itn = payload is null ? null : AutopayProtocol.ParseItn(payload, sharedKey);
        if (itn is null || itn.Transactions.Count == 0)
        {
            logger.LogWarning("Autopay ITN: brak poprawnych danych transakcji.");
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending));
        }

        var tx = itn.Transactions[0];
        if (!itn.HashValid || itn.ServiceId != serviceId)
        {
            logger.LogWarning("Autopay ITN: niezgodny hash albo ServiceID dla zamówienia {Order}.", tx.OrderId);
            // Odpowiadamy NOTCONFIRMED — Autopay ponowi powiadomienie, a my niczego nie zmieniamy.
            return Task.FromResult(new ProviderNotifyResult(false, null, PaymentOutcome.Pending,
                _ => AutopayProtocol.ConfirmationXml(itn.ServiceId, itn.Transactions.Select(t => (t.OrderId, false)), sharedKey)));
        }

        var outcome = tx.PaymentStatus.ToUpperInvariant() switch
        {
            "SUCCESS" => PaymentOutcome.Paid,
            "FAILURE" => PaymentOutcome.Failed,
            _ => PaymentOutcome.Pending
        };

        // Jedna transakcja na ITN (tak wysyła Autopay); ewentualne kolejne potwierdzamy dopiero przy następnym ITN.
        return Task.FromResult(new ProviderNotifyResult(true, tx.OrderId, outcome,
            handled => AutopayProtocol.ConfirmationXml(serviceId,
                itn.Transactions.Select((t, i) => (t.OrderId, i == 0 && handled)), sharedKey)));
    }
}
