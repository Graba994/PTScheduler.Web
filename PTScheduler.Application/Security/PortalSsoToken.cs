using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PTScheduler.Application.Security;

/// <summary>Kto i jako kto wchodzi z Portalu (po sprawdzeniu podpisu i ważności).</summary>
/// <param name="Target">„root” — konto techniczne, „owner” — właściciel studia (przełączenie z konta technicznego).</param>
public sealed record PortalSso(string Slug, string AdminEmail, string Target, string Nonce, DateTimeOffset ExpiresAt);

/// <summary>
/// Jednorazowe wejście administratora Portalu (Hantly) do aplikacji trenera, bez hasła.
/// Portal podpisuje krótki token (HMAC-SHA256) kluczem wyprowadzonym z sekretu instancji
/// (TENANT_INTERNAL_SECRET), który znają tylko Portal i ta instancja. Token jest ważny 2 minuty,
/// przypisany do jednej instancji (slug), a aplikacja przyjmuje każdy tylko raz (nonce).
/// Ten sam format tworzy PTScheduler.Portal (TenantSso) — test pilnuje zgodności.
/// </summary>
public static class PortalSsoToken
{
    public const int MaxLifetimeSeconds = 300;
    public static readonly string[] Targets = ["root", "owner"];

    public static byte[] Key(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes("pt-sso:" + secret));

    public static PortalSso? Validate(string token, string secret, string expectedSlug, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(expectedSlug)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;
        byte[] payload, sig;
        try { payload = FromB64Url(parts[0]); sig = FromB64Url(parts[1]); }
        catch (FormatException) { return null; }

        var expected = HMACSHA256.HashData(Key(secret), Encoding.ASCII.GetBytes("pt-sso." + parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(expected, sig)) return null;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var r = doc.RootElement;
            var slug = r.GetProperty("s").GetString() ?? "";
            var admin = r.GetProperty("a").GetString() ?? "";
            var target = r.GetProperty("t").GetString() ?? "";
            var nonce = r.GetProperty("n").GetString() ?? "";
            var exp = DateTimeOffset.FromUnixTimeSeconds(r.GetProperty("e").GetInt64());
            if (!string.Equals(slug, expectedSlug, StringComparison.OrdinalIgnoreCase)) return null;
            if (exp <= now || exp - now > TimeSpan.FromSeconds(MaxLifetimeSeconds)) return null;
            if (!Targets.Contains(target) || nonce.Length < 16 || admin.Length == 0) return null;
            return new(slug, admin, target, nonce, exp);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] FromB64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
