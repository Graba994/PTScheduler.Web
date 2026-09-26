namespace PTScheduler.Domain.Constants;

/// <summary>Stable keys for supported payment gateways. Stored on orders and in provider config.</summary>
public static class PaymentProviders
{
    public const string Simulator = "sim";
    public const string PayU = "payu";
    public const string Przelewy24 = "p24";
    public const string Klarna = "klarna";
    public const string AutoPay = "autopay";

    public static readonly string[] All = [Simulator, PayU, Przelewy24, Klarna, AutoPay];
}
