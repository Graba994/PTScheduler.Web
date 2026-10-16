using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Wejście administratora Portalu do aplikacji trenera jednym kliknięciem: krótki, jednorazowy token
/// podpisany kluczem z sekretu instancji. Aplikacja trenera sprawdza go w /sso/portal
/// (PTScheduler.Application.Security.PortalSsoToken — ten sam format) i loguje jako konto techniczne
/// albo właściciela studia. Token żyje 2 minuty, a w Portalu powstaje dopiero po kliknięciu.
/// </summary>
public static class TenantSso
{
    public const int LifetimeSeconds = 120;

    public static string CreateToken(string slug, string adminEmail, string target, string secret, DateTimeOffset now)
    {
        var payload = JsonSerializer.Serialize(new
        {
            s = slug,
            a = adminEmail,
            t = target == "owner" ? "owner" : "root",
            e = now.AddSeconds(LifetimeSeconds).ToUnixTimeSeconds(),
            n = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
        });
        var p = B64Url(Encoding.UTF8.GetBytes(payload));
        var key = SHA256.HashData(Encoding.UTF8.GetBytes("pt-sso:" + secret));
        var sig = B64Url(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes("pt-sso." + p)));
        return p + "." + sig;
    }

    public static string Url(string publicBase, string token) => $"{publicBase.TrimEnd('/')}/sso/portal?t={Uri.EscapeDataString(token)}";

    private static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
