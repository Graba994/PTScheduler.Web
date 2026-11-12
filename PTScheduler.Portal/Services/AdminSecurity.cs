using System.Text;

namespace PTScheduler.Portal.Services;

/// <summary>Pomocnicze do weryfikacji dwuetapowej administratorów Portalu (TOTP — Google Authenticator i podobne).</summary>
public static class AdminSecurity
{
    public const string Issuer = "Hantly";

    /// <summary>Adres do kodu QR: aplikacja uwierzytelniająca dodaje konto „Hantly (e-mail)”.</summary>
    public static string AuthenticatorUri(string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    /// <summary>Klucz do przepisania ręcznie: grupy po 4 znaki, małymi literami (tak pokazuje go też Google).</summary>
    public static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(char.ToLowerInvariant(key[i]));
        }
        return sb.ToString();
    }

    /// <summary>Kod wpisany z odstępami albo myślnikami („123 456”) — zostają same cyfry i litery.</summary>
    public static string NormalizeCode(string? code) => new((code ?? "").Where(char.IsLetterOrDigit).ToArray());
}
