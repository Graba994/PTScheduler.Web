using System.Text.RegularExpressions;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// Ostatnie sito dla tekstów AI przed pokazaniem: bez znaczników, których nie było w danych („[imię]”),
/// i bez komentarzy o brakach („nie podałeś imienia”). Taki tekst zastępujemy wersją podstawową.
/// </summary>
public static partial class AiTextGuard
{
    private static readonly string[] MetaComments =
    [
        "nie podał", "nie podano", "brak imienia", "nie znam imienia", "bez imienia", "imię klienta",
        "w faktach", "z faktów", "nie mam informacji"
    ];

    public static bool IsClean(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (Bracket().Matches(text).Any(m => !source.Contains(m.Value, StringComparison.Ordinal))) return false;
        return !MetaComments.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\[[^\]\s]{1,30}\]")] private static partial Regex Bracket();
}
