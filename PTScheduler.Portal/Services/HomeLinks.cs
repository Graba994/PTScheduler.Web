using System.Text.RegularExpressions;

namespace PTScheduler.Portal.Services;

/// <summary>„Wyślij mi link” ze strony głównej: sprawdzenie adresu i link do kreatora z nazwą studia i kolorem.</summary>
public static partial class HomeLinks
{
    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsWhiteSpace)) return false;
        try
        {
            var a = new System.Net.Mail.MailAddress(value);
            return a.Address == value && a.Host.Contains('.');
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Tekst z formularza: przycięty, bez znaków sterujących, najwyżej <paramref name="max"/> znaków.</summary>
    public static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length == 0 ? null : s.Length > max ? s[..max] : s;
    }

    /// <summary>Adres kreatora (/register) z ?nazwa= i ?kolor= — kolor tylko jako prosty klucz, np. „violet”.</summary>
    public static string RegisterLink(string publicBase, string? company, string? color)
    {
        var q = new List<string>();
        if (!string.IsNullOrWhiteSpace(company)) q.Add("nazwa=" + Uri.EscapeDataString(company));
        if (!string.IsNullOrWhiteSpace(color) && ColorKey().IsMatch(color)) q.Add("kolor=" + color);
        q.Add("utm_source=link-email");
        return publicBase.TrimEnd('/') + "/register?" + string.Join("&", q);
    }

    [GeneratedRegex("^[a-z0-9-]{1,20}$")]
    private static partial Regex ColorKey();
}
