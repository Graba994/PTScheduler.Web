using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using PTScheduler.Domain.Rules;
using PTScheduler.Infrastructure.Data;

namespace PTScheduler.Web.Security;

/// <summary>
/// Odrzuca hasła łatwe do zgadnięcia: z listy najpopularniejszych (także polskich, np. „Haslo123”),
/// zbudowane z e-maila albo imienia, ciągi typu „12345678” oraz — gdy serwis jest osiągalny —
/// hasła z wycieków danych (Have I Been Pwned). Do serwisu wysyłamy tylko 5 pierwszych znaków
/// skrótu SHA-1 (k-anonimowość) — samo hasło nigdy nie opuszcza aplikacji. Gdy serwis nie odpowiada,
/// nie blokujemy użytkownika.
/// </summary>
public sealed class WeakPasswordValidator(IHttpClientFactory httpFactory, IConfiguration config, ILogger<WeakPasswordValidator> logger)
    : IPasswordValidator<ApplicationUser>
{
    public const string HttpClientName = "pwned-passwords";
    /// <summary>Od tylu wystąpień w wyciekach hasło uznajemy za spalone.</summary>
    public const int BreachThreshold = 3;

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return IdentityResult.Success;

        var reason = WeakPasswordRules.LocalReason(password, user.Email, user.FirstName, user.LastName);
        if (reason is not null) return Fail(reason);

        if (config.GetValue("Security:PwnedPasswordsCheck", true))
        {
            var count = await BreachCountAsync(password);
            if (count >= BreachThreshold)
                return Fail("To hasło pojawiło się w wyciekach danych z innych serwisów — wybierz inne, którego nigdzie nie używasz.");
        }
        return IdentityResult.Success;
    }

    private async Task<int> BreachCountAsync(string password)
    {
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            var (prefix, suffix) = (hash[..5], hash[5..]);
            using var http = httpFactory.CreateClient(HttpClientName);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var body = await http.GetStringAsync($"https://api.pwnedpasswords.com/range/{prefix}", cts.Token);
            foreach (var line in body.Split('\n'))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length == 2 && parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase))
                    return int.TryParse(parts[1], out var n) ? n : 0;
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Sprawdzenie hasła w bazie wycieków nie powiodło się — pomijam.");
            return 0;
        }
    }

    private static IdentityResult Fail(string message) =>
        IdentityResult.Failed(new IdentityError { Code = "WeakPassword", Description = message });
}
