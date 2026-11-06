using Microsoft.AspNetCore.DataProtection;

namespace PTScheduler.Portal.Services;

/// <summary>Linki „Zarządzaj subskrypcją” dla trenerów rozliczanych rachunkami (bez Stripe) — ważne 30 minut.</summary>
public static class SubscriptionLinks
{
    public const string Purpose = "PTScheduler.Portal.Subscription.v1";

    public static int? TenantIdFrom(IDataProtectionProvider provider, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var raw = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector().Unprotect(token);
            return int.TryParse(raw, out var id) ? id : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}
