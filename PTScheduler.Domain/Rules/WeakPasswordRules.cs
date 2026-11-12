namespace PTScheduler.Domain.Rules;

/// <summary>Reguły sprawdzane lokalnie, bez sieci (testowalne).</summary>
public static class WeakPasswordRules
{
    // Rdzenie najczęstszych haseł; „Haslo123!”, „Qwerty2024” czy „Trening1” to rdzeń + cyfry/znaki.
    private static readonly HashSet<string> Bases = new(StringComparer.Ordinal)
    {
        "password", "passw0rd", "haslo", "hasło", "haselko", "qwerty", "qwertyui", "qwertyuiop", "asdfgh", "asdfghjkl",
        "zxcvbn", "zxcvbnm", "zaq12wsx", "1qaz2wsx", "qazwsx", "abc", "abcd", "abcdef", "abcdefgh", "admin", "administrator",
        "root", "user", "login", "welcome", "witaj", "witam", "polska", "poland", "kochanie", "kocham", "kochamcie", "misiek",
        "myszka", "monika", "kasia", "agnieszka", "marcin", "michal", "michał", "tomek", "piotr", "pawel", "paweł", "kamil",
        "lukasz", "łukasz", "mateusz", "magda", "natalia", "zuzia", "bartek", "dragon", "monkey", "master", "letmein",
        "football", "pilkanozna", "legia", "lech", "wisla", "barcelona", "realmadrid", "superman", "batman", "iloveyou",
        "sunshine", "princess", "starwars", "trening", "trener", "trenerka", "silownia", "siłownia", "fitness", "fit",
        "gym", "sport", "bieganie", "crossfit", "kulturystyka", "dieta", "zdrowie", "personal", "ptscheduler", "studio",
        "test", "tester", "testowe", "demo", "default", "changeme", "zmienmnie", "nowehaslo", "noweh", "secret", "tajne",
        "lato", "zima", "wiosna", "jesien", "styczen", "luty", "marzec", "kwiecien", "maj", "czerwiec", "lipiec", "sierpien",
        "wrzesien", "pazdziernik", "listopad", "grudzien", "poniedzialek", "niedziela", "dupa", "kurwa", "cipka", "kutas"
    };

    /// <summary>Powód odrzucenia albo null, gdy hasło jest w porządku.</summary>
    public static string? LocalReason(string password, string? email, string? firstName, string? lastName)
    {
        var lower = password.ToLowerInvariant();

        if (lower.Distinct().Count() <= 2)
            return "Hasło składa się z jednego–dwóch powtarzanych znaków — dodaj różne litery i cyfry.";
        if (IsSequence(lower))
            return "Hasło to prosty ciąg znaków (np. 12345678, abcdefgh) — łatwo je zgadnąć.";

        var core = lower.TrimEnd("0123456789!@#$%^&*.,?_-+= ".ToCharArray()).TrimStart("0123456789!@#$%^&*.,?_-+= ".ToCharArray());
        if (core.Length == 0)
            return "Hasło nie może składać się z samych cyfr i znaków — dodaj litery.";
        if (Bases.Contains(core) || Bases.Contains(Deleet(core)))
            return "To hasło jest jednym z najczęściej używanych (np. Haslo123, Qwerty1) — wybierz mniej oczywiste.";

        foreach (var personal in new[] { email?.Split('@')[0], firstName, lastName })
        {
            var p = personal?.Trim().ToLowerInvariant();
            if (p is { Length: >= 4 } && (core == p || core.Contains(p) && core.Length - p.Length <= 2))
                return "Hasło nie powinno opierać się na Twoim imieniu, nazwisku ani adresie e-mail.";
        }
        return null;
    }

    private static string Deleet(string s) => s.Replace('0', 'o').Replace('1', 'i').Replace('3', 'e').Replace('4', 'a').Replace('5', 's').Replace('@', 'a').Replace('$', 's');

    private static bool IsSequence(string s)
    {
        if (s.Length < 6) return false;
        bool asc = true, desc = true;
        for (var i = 1; i < s.Length; i++)
        {
            if (s[i] != s[i - 1] + 1) asc = false;
            if (s[i] != s[i - 1] - 1) desc = false;
        }
        return asc || desc;
    }
}
