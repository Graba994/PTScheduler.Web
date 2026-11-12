namespace PTScheduler.Domain.Rules;

/// <summary>
/// Prosty kreator własnej skrzynki: z adresu e-mail zgadujemy dostawcę i podajemy serwer, port oraz
/// 2–3 kroki, które trzeba zrobić u dostawcy (zwykle: włączyć dostęp dla programów albo hasło aplikacji).
/// Poczta firmowa (własna domena) — serwer z hostingu, domyślnie mail.domena.
/// </summary>
public static class MailProviderRules
{
    public sealed record Provider(string Key, string Name, string Host, int Port, string PasswordLabel, IReadOnlyList<string> Steps, string? Note = null)
    {
        /// <summary>Serwer trzeba wpisać samemu (poczta firmowa).</summary>
        public bool CustomHost => Key is "hosting";
    }

    public static readonly Provider Gmail = new("Gmail", "Gmail", "smtp.gmail.com", 587, "Hasło aplikacji (16 znaków)",
    [
        "Na <b>myaccount.google.com</b> → Bezpieczeństwo włącz <b>weryfikację dwuetapową</b> (jeśli jeszcze nie działa).",
        "Wyszukaj <b>„Hasła do aplikacji”</b>, utwórz hasło o nazwie np. Hantly i skopiuj 16-znakowy kod.",
        "Wklej kod poniżej — to nie jest Twoje zwykłe hasło do Gmaila."
    ], "Prywatny Gmail wyśle do ok. 500 wiadomości dziennie — dla studia w zupełności wystarczy.");

    public static readonly Provider Workspace = new("GoogleWorkspace", "Google Workspace", "smtp.gmail.com", 587, "Hasło aplikacji (16 znaków)",
    [
        "Na <b>myaccount.google.com</b> (konto firmowe) włącz <b>weryfikację dwuetapową</b>.",
        "Wyszukaj <b>„Hasła do aplikacji”</b>, utwórz hasło o nazwie np. Hantly i skopiuj 16-znakowy kod.",
        "Wklej kod poniżej. Jeśli opcji nie ma — administrator domeny musi ją włączyć w admin.google.com."
    ]);

    public static readonly Provider Wp = new("Wp", "WP Poczta", "smtp.wp.pl", 465, "Hasło do poczty WP",
    [
        "Zaloguj się do <b>poczta.wp.pl</b> → Ustawienia → <b>Konto</b>.",
        "Włącz <b>„Dostęp przez programy pocztowe (POP3, IMAP, SMTP)”</b> i zapisz.",
        "Wpisz poniżej swoje hasło do poczty WP."
    ]);

    public static readonly Provider Onet = new("Onet", "Onet Poczta", "smtp.poczta.onet.pl", 465, "Hasło do poczty (albo hasło aplikacji)",
    [
        "Zaloguj się do <b>poczta.onet.pl</b> → Ustawienia → <b>Programy pocztowe</b>.",
        "Włącz <b>dostęp przez programy pocztowe</b> i zapisz.",
        "Wpisz poniżej hasło do poczty. Masz weryfikację dwuetapową? Utwórz w ustawieniach hasło aplikacji i wklej je."
    ]);

    public static readonly Provider Interia = new("Interia", "Interia Poczta", "poczta.interia.pl", 465, "Hasło do poczty Interia",
    [
        "Zaloguj się do <b>poczta.interia.pl</b> → Ustawienia → <b>Parametry</b>.",
        "Włącz <b>„Programy pocztowe”</b> (dostęp IMAP/SMTP) i zapisz.",
        "Wpisz poniżej swoje hasło do poczty Interia."
    ]);

    public static readonly Provider O2 = new("O2", "Poczta o2", "poczta.o2.pl", 465, "Hasło do poczty o2",
    [
        "Zaloguj się do <b>poczta.o2.pl</b> → Ustawienia → <b>Konta pocztowe / POP3, IMAP, SMTP</b>.",
        "Włącz dostęp przez programy pocztowe i zapisz.",
        "Wpisz poniżej swoje hasło do poczty o2."
    ]);

    public static Provider Hosting(string? domain) => new("hosting", "Poczta firmowa (hosting)",
        string.IsNullOrWhiteSpace(domain) ? "" : $"mail.{domain}", 465, "Hasło do skrzynki",
    [
        "W panelu hostingu (home.pl, OVH, cyber_Folks, nazwa.pl…) otwórz ustawienia skrzynki — znajdziesz tam <b>serwer poczty wychodzącej (SMTP)</b>.",
        "Wpisz go poniżej (często <b>mail.twojadomena.pl</b>) i hasło do skrzynki.",
        "Nie wiesz, gdzie to jest? Zamów podłączenie — zrobimy to za Ciebie."
    ]);

    private static readonly Dictionary<string, Provider> ByDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail.com"] = Gmail, ["googlemail.com"] = Gmail,
        ["wp.pl"] = Wp,
        ["onet.pl"] = Onet, ["op.pl"] = Onet, ["vp.pl"] = Onet, ["onet.eu"] = Onet, ["poczta.onet.pl"] = Onet,
        ["autograf.pl"] = Onet, ["buziaczek.pl"] = Onet, ["spoko.pl"] = Onet,
        ["interia.pl"] = Interia, ["interia.eu"] = Interia, ["poczta.fm"] = Interia, ["intmail.pl"] = Interia,
        ["o2.pl"] = O2, ["tlen.pl"] = O2, ["go2.pl"] = O2, ["prokonto.pl"] = O2,
    };

    /// <summary>Darmowe skrzynki, przez które nie wyślemy (bez SMTP z hasłem) — kierujemy do poczty platformy.</summary>
    private static readonly HashSet<string> Unsupported = new(StringComparer.OrdinalIgnoreCase)
    {
        "outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.pl", "icloud.com", "me.com", "yahoo.com"
    };

    public static string? DomainOf(string? email)
    {
        var at = email?.Trim().LastIndexOf('@') ?? -1;
        return at < 1 || at == email!.Trim().Length - 1 ? null : email.Trim()[(at + 1)..].ToLowerInvariant();
    }

    /// <summary>Dostawca po adresie; null — adres niepełny albo skrzynka, której nie podłączymy hasłem.</summary>
    public static Provider? Detect(string? email, bool workspace = false)
    {
        var domain = DomainOf(email);
        if (domain is null || Unsupported.Contains(domain)) return null;
        if (ByDomain.TryGetValue(domain, out var p)) return p;
        return workspace ? Workspace : Hosting(domain);
    }

    public static bool IsUnsupported(string? email) => DomainOf(email) is { } d && Unsupported.Contains(d);
}
