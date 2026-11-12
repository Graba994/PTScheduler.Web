using System.Text.RegularExpressions;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Sprawdzanie adresów e-mail przy zakładaniu kont: konkretny powód błędu po polsku
/// i podpowiedź przy typowej literówce w domenie („gmial.com” → „gmail.com”).
/// Adresy porównujemy po normalizacji (bez spacji, małymi literami) — „Jan@WP.pl” to ten sam klient co „jan@wp.pl”.
/// </summary>
public static partial class EmailRules
{
    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Co jest nie tak z adresem (null — adres wygląda poprawnie).</summary>
    public static string? Problem(string? email)
    {
        var e = Normalize(email);
        if (e.Length == 0) return "Podaj adres e-mail.";
        if (e.Length > 254) return "Adres e-mail jest za długi.";
        if (e.Any(char.IsWhiteSpace)) return "Adres e-mail nie może zawierać spacji.";
        var at = e.Count(c => c == '@');
        if (at == 0) return "Brakuje znaku „@” — np. jan.kowalski@gmail.com.";
        if (at > 1) return "Adres może mieć tylko jeden znak „@”.";
        var (local, domain) = (e[..e.IndexOf('@')], e[(e.IndexOf('@') + 1)..]);
        if (local.Length == 0) return "Brakuje części przed „@”.";
        if (domain.Length == 0) return "Brakuje domeny po „@” — np. gmail.com.";
        if (!domain.Contains('.')) return $"Domena „{domain}” jest niepełna — brakuje końcówki, np. „.pl” albo „.com”.";
        if (e.Contains("..") || local.StartsWith('.') || local.EndsWith('.') || domain.StartsWith('.') || domain.EndsWith('.'))
            return "Adres ma kropkę w złym miejscu (np. dwie kropki obok siebie).";
        if (!LocalPart().IsMatch(local) || !DomainPart().IsMatch(domain)) return "Adres zawiera niedozwolone znaki.";
        var tld = domain[(domain.LastIndexOf('.') + 1)..];
        if (tld.Length < 2 || tld.Any(char.IsDigit)) return $"Końcówka domeny „.{tld}” wygląda na błędną.";
        return null;
    }

    public static bool IsValid(string? email) => Problem(email) is null;

    private static readonly Dictionary<string, string> DomainTypos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmial.com"] = "gmail.com", ["gmai.com"] = "gmail.com", ["gmal.com"] = "gmail.com", ["gamil.com"] = "gmail.com",
        ["gmaill.com"] = "gmail.com", ["gmail.co"] = "gmail.com", ["gmail.con"] = "gmail.com", ["gmail.cm"] = "gmail.com",
        ["gmail.pl"] = "gmail.com", ["gnail.com"] = "gmail.com", ["gmail.om"] = "gmail.com",
        ["wp.p"] = "wp.pl", ["wpl.pl"] = "wp.pl", ["wp.ppl"] = "wp.pl",
        ["onet.p"] = "onet.pl", ["onet.ppl"] = "onet.pl", ["onte.pl"] = "onet.pl",
        ["o2.p"] = "o2.pl", ["interia.p"] = "interia.pl", ["interia.pll"] = "interia.pl", ["inetria.pl"] = "interia.pl",
        ["hotmial.com"] = "hotmail.com", ["hotmai.com"] = "hotmail.com", ["hotmail.co"] = "hotmail.com",
        ["outlok.com"] = "outlook.com", ["outlook.co"] = "outlook.com", ["otlook.com"] = "outlook.com",
        ["yahoo.co"] = "yahoo.com", ["yaho.com"] = "yahoo.com", ["icloud.co"] = "icloud.com", ["iclod.com"] = "icloud.com",
    };

    /// <summary>Poprawiony adres, gdy domena wygląda na literówkę popularnej skrzynki (null — brak podpowiedzi).</summary>
    public static string? Suggestion(string? email)
    {
        var e = Normalize(email);
        var at = e.IndexOf('@');
        if (at <= 0 || at != e.LastIndexOf('@')) return null;
        var domain = e[(at + 1)..];
        if (DomainTypos.TryGetValue(domain, out var fixedDomain)) return e[..at] + "@" + fixedDomain;
        if (domain.EndsWith(".con")) return e[..at] + "@" + domain[..^4] + ".com";
        return null;
    }

    [GeneratedRegex(@"^[a-z0-9!#$%&'*+/=?^_`{|}~.-]+$")] private static partial Regex LocalPart();
    [GeneratedRegex(@"^[a-z0-9.-]+$")] private static partial Regex DomainPart();
}
