using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PTScheduler.Application.Training;

/// <summary>
/// Opis lekcji kursu jako zwykły tekst (bez edytora HTML/CSS): akapity oddzielone pustą linią, nowe linie i linki
/// zamieniamy na bezpieczny HTML. Starsze lekcje z HTML-em odczytujemy z powrotem jako tekst.
/// </summary>
public static partial class LessonText
{
    public static string? ToHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var sb = new StringBuilder();
        foreach (var para in ParagraphSplit().Split(text.Replace("\r\n", "\n").Trim()))
        {
            if (string.IsNullOrWhiteSpace(para)) continue;
            var lines = para.Trim().Split('\n').Select(l => Linkify(WebUtility.HtmlEncode(l.TrimEnd())));
            sb.Append("<p>").Append(string.Join("<br>", lines)).Append("</p>");
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    public static string FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = BreakTag().Replace(html, "\n");
        s = ParagraphEnd().Replace(s, "\n\n");
        s = AnyTag().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        s = ManyNewlines().Replace(s.Replace("\r\n", "\n"), "\n\n");
        return string.Join("\n", s.Split('\n').Select(l => l.TrimEnd())).Trim();
    }

    private static string Linkify(string encoded) =>
        Url().Replace(encoded, m => $"<a href=\"{m.Value}\" target=\"_blank\" rel=\"noopener\">{m.Value}</a>");

    [GeneratedRegex(@"\n\s*\n")] private static partial Regex ParagraphSplit();
    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)] private static partial Regex BreakTag();
    [GeneratedRegex(@"</(p|div|li|h[1-6])>", RegexOptions.IgnoreCase)] private static partial Regex ParagraphEnd();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex AnyTag();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex ManyNewlines();
    [GeneratedRegex(@"https://[^\s<>""']+")] private static partial Regex Url();
}
