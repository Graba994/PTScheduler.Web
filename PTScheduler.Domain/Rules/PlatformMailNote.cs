using System.Net;

namespace PTScheduler.Domain.Rules;

/// <summary>
/// E-maile wysyłane serwerem platformy (adres Hantly, nie skrzynka trenera) zawsze mówią, kto pisze:
/// nazwa studia jako nadawca i krótka stopka „wiadomość od … przez Hantly — odpowiedz, trafi do …”.
/// </summary>
public static class PlatformMailNote
{
    public const string Marker = "data-pt-platform-note";

    public static string Footer(string studio, string? replyTo)
    {
        var name = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(studio) ? "Twój trener" : studio.Trim());
        var reply = string.IsNullOrWhiteSpace(replyTo)
            ? ""
            : $" Odpowiedz na tę wiadomość — trafi prosto do {name} ({WebUtility.HtmlEncode(replyTo.Trim())}).";
        return $"<div {Marker} style=\"margin:24px auto 0;max-width:560px;padding:12px 16px;border-top:1px solid #e5e7eb;" +
               $"font:13px/1.5 -apple-system,Segoe UI,Roboto,Arial,sans-serif;color:#6b7280;text-align:center\">" +
               $"Wiadomość od <b style=\"color:#374151\">{name}</b>, wysłana przez Hantly.{reply}</div>";
    }

    /// <summary>Dokleja stopkę przed &lt;/body&gt; (albo na końcu); drugi raz jej nie dodaje.</summary>
    public static string Append(string html, string studio, string? replyTo)
    {
        html ??= "";
        if (html.Contains(Marker, StringComparison.Ordinal)) return html;
        var footer = Footer(studio, replyTo);
        var at = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return at >= 0 ? html.Insert(at, footer) : html + footer;
    }
}
