using System.Text.RegularExpressions;
using PTScheduler.Domain.Rules;

namespace PTScheduler.Application.Messaging;

/// <summary>
/// Szablony wiadomości do klienta (tekst zapasowy, gdy AI jest wyłączone) dopasowane do stylu trenera:
/// forma (na Ty / grzecznościowa bez zgadywania płci), emoji, powitanie i podpis. Imię klienta to znacznik [klient].
/// </summary>
public static partial class MessageTemplates
{
    public const string ClientTag = "[klient]";

    public sealed record Style(string Address, string Emoji, string? Greeting, string? Signature);

    public static string Label(DraftPurpose purpose) => purpose switch
    {
        DraftPurpose.Comeback => "Powrót po przerwie",
        DraftPurpose.Package => "Kolejny pakiet",
        DraftPurpose.Motivation => "Regularność i grafik",
        DraftPurpose.Wellbeing => "Samopoczucie i ból",
        DraftPurpose.Congrats => "Gratulacje postępów",
        DraftPurpose.PlanReminder => "Plan w aplikacji",
        _ => purpose.ToString(),
    };

    /// <param name="facts">Liczby z danych: dni_od_treningu, pozostale_wejscia, liczba_treningow.</param>
    public static string Build(DraftPurpose purpose, Style style, IReadOnlyDictionary<string, string> facts)
    {
        var formal = style.Address == "pan";
        var greeting = string.IsNullOrWhiteSpace(style.Greeting) ? (formal ? "Dzień dobry" : "Cześć") : style.Greeting.Trim().TrimEnd('!', ',');
        var hello = formal ? $"{greeting}!" : $"{greeting} {ClientTag}!";
        facts.TryGetValue("pozostale_wejscia", out var left);
        facts.TryGetValue("liczba_treningow", out var count);

        var body = (purpose, formal) switch
        {
            (DraftPurpose.Comeback, false) => "Dawno się nie widzieliśmy — jak się masz? Mam wolne terminy w tym tygodniu. Daj znać, który pasuje, i wracamy do formy 💪",
            (DraftPurpose.Comeback, true) => "Dawno się nie widzieliśmy. Mam wolne terminy w tym tygodniu — proszę dać znać, który pasuje, chętnie wrócimy do treningów 💪",
            (DraftPurpose.Package, false) => (left == "1" ? "W pakiecie zostało ostatnie wejście." : "Pakiet właśnie się skończył.")
                + " Szkoda tracić rozpęd — chcesz kontynuować? Przygotuję kolejny pakiet 🙂",
            (DraftPurpose.Package, true) => (left == "1" ? "W pakiecie zostało ostatnie wejście." : "Pakiet właśnie się skończył.")
                + " Szkoda tracić rozpęd — czy kontynuujemy? Chętnie przygotuję kolejny pakiet 🙂",
            (DraftPurpose.Motivation, false) => "Ostatnio trudniej zgrać nam terminy. Ustalmy stały dzień i godzinę, które na pewno pasują — łatwiej będzie trzymać regularność. Co Ty na to?",
            (DraftPurpose.Motivation, true) => "Ostatnio trudniej zgrać terminy. Proponuję ustalić stały dzień i godzinę, które na pewno pasują — łatwiej będzie o regularność.",
            (DraftPurpose.Wellbeing, false) => "Widzę w ankietach, że ostatnio bywa ciężej. Jak się czujesz? Dopasuję trening, żeby był skuteczny i bezpieczny.",
            (DraftPurpose.Wellbeing, true) => "Z ankiet wynika, że ostatnio bywa ciężej. Jak samopoczucie? Dopasuję trening, żeby był skuteczny i bezpieczny.",
            (DraftPurpose.Congrats, false) => (string.IsNullOrEmpty(count) ? "Świetna robota na ostatnich treningach!" : $"Masz za sobą już {count} treningów — świetna robota!")
                + " Widać postęp, tak trzymaj 💪",
            (DraftPurpose.Congrats, true) => (string.IsNullOrEmpty(count) ? "Gratuluję ostatnich treningów!" : $"Za nami już {count} treningów — gratuluję!")
                + " Widać postęp, tak trzymamy 💪",
            (DraftPurpose.PlanReminder, false) => "Plan czeka w aplikacji — nawet krótki trening się liczy. Daj znać, jeśli coś trzeba w nim zmienić.",
            (DraftPurpose.PlanReminder, true) => "Plan czeka w aplikacji — nawet krótki trening się liczy. Proszę dać znać, jeśli coś trzeba w nim zmienić.",
            _ => "Jak się masz? Daj znać, kiedy się widzimy.",
        };

        var text = $"{hello} {body}";
        if (style.Emoji == "brak") text = StripEmoji(text);
        if (!string.IsNullOrWhiteSpace(style.Signature)) text += $"\n{style.Signature.Trim()}";
        return text;
    }

    /// <summary>Usuwa emoji (i zbędne spacje po nich).</summary>
    public static string StripEmoji(string text) => Spaces().Replace(Emoji().Replace(text, ""), " ").Trim();

    /// <summary>
    /// Przygotowuje wysłaną wiadomość na przykład stylu: imię i nazwisko klienta → [klient], bez telefonów i e-maili.
    /// </summary>
    public static string ToExample(string text, string firstName, string? lastName)
    {
        var t = Mail().Replace(text.Trim(), "[e-mail]");
        t = Phone().Replace(t, "[telefon]");
        var full = $"{firstName} {lastName}".Trim();
        if (full.Contains(' ')) t = Regex.Replace(t, $@"\b{Regex.Escape(full)}\w*", ClientTag, RegexOptions.IgnoreCase);
        // Imię w odmianie: Marek → Marku/Markiem, Anna → Anno/Ani, Paweł → Pawle/Pawłem.
        foreach (var stem in Stems(firstName.Trim()))
            t = Regex.Replace(t, $@"\b{Regex.Escape(stem)}\w{{0,4}}\b", ClientTag, RegexOptions.IgnoreCase);
        return t.Length <= 1000 ? t : t[..1000];
    }

    private static IEnumerable<string> Stems(string name)
    {
        if (name.Length < 2) yield break;
        yield return name;
        if (name.Length >= 4)
        {
            yield return name[..^1];
            if (name[^2] == 'e') yield return name[..^2] + name[^1];
        }
    }

    /// <summary>Czy trener istotnie poprawił szkic (inaczej nie ma się czego uczyć).</summary>
    public static bool IsEdited(string generated, string final)
    {
        static string Norm(string s) => Spaces().Replace(s.Trim().ToLowerInvariant(), " ");
        var a = Norm(generated);
        var b = Norm(final);
        if (a == b) return false;
        // Drobne literówki to nie zmiana stylu: wymagamy różnicy co najmniej 8 znaków albo 10% długości.
        return Math.Abs(a.Length - b.Length) >= Math.Max(8, a.Length / 10) || Distance(a, b) >= Math.Max(8, a.Length / 10);
    }

    private static int Distance(string a, string b)
    {
        if (a.Length > 600 || b.Length > 600) return Math.Abs(a.Length - b.Length) + 8;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Numer do linku WhatsApp: same cyfry, polskie 9 cyfr z prefiksem 48.</summary>
    public static string? WhatsAppNumber(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.Length == 9) digits = "48" + digits;
        return digits.Length is >= 10 and <= 15 ? digits : null;
    }

    [GeneratedRegex(@"[☀-➿️‍]|\uD83C[\uDF00-\uDFFF]|\uD83D[\uDC00-\uDEFF]|\uD83E[\uDD00-\uDEFF]")] private static partial Regex Emoji();
    [GeneratedRegex(@"[ \t]{2,}")] private static partial Regex Spaces();
    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+")] private static partial Regex Mail();
    [GeneratedRegex(@"(?<!\d)(?:\+?48[ -]?)?\d{3}[ -]?\d{3}[ -]?\d{3}(?!\d)")] private static partial Regex Phone();
}
