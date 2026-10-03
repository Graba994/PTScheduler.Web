using System.Security.Cryptography;
using System.Text;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Sekrety wywołań Portal ↔ instancja trenera. Każda instancja ma własny sekret
/// (<see cref="Tenant.InternalSecret"/>); wspólny sekret platformy zostaje tylko
/// dla instancji utworzonych przed tą zmianą — do czasu „Zsynchronizuj sekret”.
/// </summary>
public static class TenantSecrets
{
    public static string For(Tenant tenant, IConfiguration config) =>
        !string.IsNullOrEmpty(tenant.InternalSecret)
            ? tenant.InternalSecret
            : config.GetValue<string>("Portal:TenantInternalSecret") ?? "";

    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Porównanie w stałym czasie; pusty oczekiwany sekret zawsze odmawia.</summary>
    public static bool Matches(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided ?? ""), Encoding.UTF8.GetBytes(expected));
    }
}
