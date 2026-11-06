using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Poczta w Panelu — części bez bazy: odczyt webhooka Brevo (Inbound Parsing), łączenie w wątki po temacie,
/// tekst z HTML, maskowanie danych przed AI i bezpieczny podgląd HTML (ramka bez skryptów i obrazków z sieci).
/// </summary>
public static partial class InboxParsing
{
    public sealed record Attachment(string Name, string? Type, long Size, string? Token);

    public sealed record Incoming(string? MessageId, string? InReplyTo, IReadOnlyList<string> References, string FromAddress, string? FromName,
        string ToAddress, string Subject, string? Text, string? Html, double? SpamScore, IReadOnlyList<Attachment> Attachments);

    public const int MaxText = 100_000;
    public const int MaxHtml = 1_000_000;

    /// <summary>Wiadomości z webhooka Brevo: <c>{"items":[…]}</c> (toleruje też samą tablicę albo jeden obiekt).</summary>
    public static List<Incoming> ParseBrevo(JsonElement root)
    {
        IEnumerable<JsonElement> items = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray(),
            JsonValueKind.Object when Prop(root, "items") is { ValueKind: JsonValueKind.Array } arr => arr.EnumerateArray(),
            JsonValueKind.Object => [root],
            _ => []
        };
        var result = new List<Incoming>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var (fromAddr, fromName) = Address(Prop(item, "From"));
            if (string.IsNullOrEmpty(fromAddr)) continue;
            var to = Prop(item, "To") is { ValueKind: JsonValueKind.Array } toArr
                ? toArr.EnumerateArray().Select(t => Address(t).Address).FirstOrDefault(a => !string.IsNullOrEmpty(a))
                : Address(Prop(item, "To")).Address;
            var html = Str(item, "RawHtmlBody");
            var text = Str(item, "ExtractedMarkdownMessage");
            if (string.IsNullOrWhiteSpace(text)) text = Str(item, "RawTextBody");
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(html)) text = HtmlToText(html);
            double? spam = Prop(item, "SpamScore") is { ValueKind: JsonValueKind.Number } sc ? sc.GetDouble() : null;

            var refs = new List<string>();
            if (Prop(item, "Headers") is { ValueKind: JsonValueKind.Object } headers && Prop(headers, "References") is { } r)
            {
                var raw = r.ValueKind == JsonValueKind.Array ? string.Join(' ', r.EnumerateArray().Select(x => x.ToString())) : r.ToString();
                refs.AddRange(raw.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length is > 2 and < 500));
            }

            var attachments = new List<Attachment>();
            if (Prop(item, "Attachments") is { ValueKind: JsonValueKind.Array } atts)
                foreach (var a in atts.EnumerateArray().Take(30))
                    attachments.Add(new Attachment(Clip(Str(a, "Name"), 200) ?? "załącznik", Clip(Str(a, "ContentType"), 100),
                        Prop(a, "ContentLength") is { ValueKind: JsonValueKind.Number } len ? len.GetInt64() : 0, Clip(Str(a, "DownloadToken"), 500)));

            result.Add(new Incoming(
                Clip(Str(item, "MessageId"), 500), Clip(Str(item, "InReplyTo"), 500), refs,
                fromAddr.ToLowerInvariant(), Clip(fromName, 200), (to ?? "").ToLowerInvariant(),
                Clip(Str(item, "Subject"), 300) ?? "",
                Clip(text?.Trim(), MaxText), Clip(html, MaxHtml), spam, attachments));
        }
        return result;
    }

    private static JsonElement? Prop(JsonElement? e, string name)
    {
        if (e is not { ValueKind: JsonValueKind.Object } obj) return null;
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private static string? Str(JsonElement e, string name) =>
        Prop(e, name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    /// <summary>Adres z <c>{Name, Address}</c> albo z tekstu „Imię &lt;adres&gt;”.</summary>
    private static (string? Address, string? Name) Address(JsonElement? e)
    {
        if (e is null) return (null, null);
        var el = e.Value;
        if (el.ValueKind == JsonValueKind.Object) return (Str(el, "Address")?.Trim(), Str(el, "Name")?.Trim());
        if (el.ValueKind == JsonValueKind.Array) return el.EnumerateArray().Select(x => Address(x)).FirstOrDefault(x => x.Address is not null);
        if (el.ValueKind != JsonValueKind.String) return (null, null);
        var s = el.GetString() ?? "";
        var m = AngleAddress().Match(s);
        return m.Success ? (m.Groups[2].Value.Trim(), m.Groups[1].Value.Trim().Trim('"')) : (s.Trim(), null);
    }

    [GeneratedRegex(@"^\s*(.*?)\s*<([^>]+)>\s*$")]
    private static partial Regex AngleAddress();

    private static string? Clip(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    /// <summary>Temat bez „Re:”, „Odp:”, „Fwd:”, „PD:”, „AW:” (także wielokrotnych) — klucz wątku.</summary>
    public static string SubjectKey(string? subject)
    {
        var s = (subject ?? "").Trim();
        while (true)
        {
            var m = ReplyPrefix().Match(s);
            if (!m.Success) break;
            s = s[m.Length..].TrimStart();
        }
        s = Whitespace().Replace(s, " ").Trim().ToLowerInvariant();
        return s.Length <= 300 ? s : s[..300];
    }

    [GeneratedRegex(@"^(re|odp|odp\.|fw|fwd|pd|aw|wg|tr)(\[\d+\])?\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string ReplySubject(string subject) =>
        ReplyPrefix().IsMatch(subject.Trim()) ? subject.Trim() : $"Re: {subject.Trim()}";

    /// <summary>Pierwsze ~160 znaków w jednej linii — podgląd na liście wątków.</summary>
    public static string Snippet(string? text)
    {
        var s = Whitespace().Replace(text ?? "", " ").Trim();
        return s.Length <= 160 ? s : s[..157].TrimEnd() + "…";
    }

    /// <summary>Zwykły tekst z HTML: bez skryptów i stylów, akapity jako nowe linie.</summary>
    public static string HtmlToText(string html)
    {
        var s = ScriptOrStyle().Replace(html, " ");
        s = LineBreakTags().Replace(s, "\n");
        s = Tags().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]+", " ");
        s = Regex.Replace(s, @"\n\s*\n\s*\n+", "\n\n");
        return s.Trim();
    }

    [GeneratedRegex(@"<(script|style|head)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/tr|/h\d)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    /// <summary>Przed wysłaniem do AI: adresy e-mail i numery telefonów zamienione na znaczniki, najwyżej 2000 znaków.</summary>
    public static string MaskForAi(string? text)
    {
        var s = EmailPattern().Replace(text ?? "", "[e-mail]");
        // Telefon: co najmniej 9 cyfr (daty typu 2026-10-19 i kwoty zostają).
        s = PhonePattern().Replace(s, m => m.Value.Count(char.IsDigit) >= 9 ? "[telefon]" : m.Value);
        return s.Length <= 2000 ? s : s[..2000];
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\w)(\+?\d[\d \-]{7,}\d)(?!\w)")]
    private static partial Regex PhonePattern();

    /// <summary>Odpowiedź jako HTML: akapity z tekstu administratora i zacytowana poprzednia wiadomość.</summary>
    public static string ReplyHtml(string body, string? quoteHeader, string? quoted)
    {
        var sb = new StringBuilder("<div style=\"font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;font-size:15px;line-height:1.55;color:#1f2937\">");
        foreach (var p in body.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            sb.Append("<p style=\"margin:0 0 12px\">").Append(WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>")).Append("</p>");
        if (!string.IsNullOrWhiteSpace(quoted))
        {
            var q = quoted.Length > 4000 ? quoted[..4000] + "…" : quoted;
            sb.Append("<p style=\"margin:18px 0 6px;color:#6b7280;font-size:13px\">").Append(WebUtility.HtmlEncode(quoteHeader ?? "")).Append("</p>");
            sb.Append("<blockquote style=\"margin:0;padding:0 0 0 12px;border-left:3px solid #e5e7eb;color:#6b7280;font-size:14px\">")
              .Append(WebUtility.HtmlEncode(q.Trim()).Replace("\n", "<br>")).Append("</blockquote>");
        }
        return sb.Append("</div>").ToString();
    }

    /// <summary>
    /// HTML do ramki <c>&lt;iframe sandbox srcdoc&gt;</c>: polityka bezpieczeństwa blokuje skrypty, formularze, ramki,
    /// czcionki i — dopóki administrator nie kliknie „Pokaż obrazki” — obrazki z sieci (piksele śledzące).
    /// </summary>
    public static string SafeSrcDoc(string html, bool allowRemoteImages)
    {
        var img = allowRemoteImages ? "data: https:" : "data:";
        return $"<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src {img}; font-src data:; form-action 'none'\">"
             + "<base target=\"_blank\"><style>body{font-family:-apple-system,Segoe UI,Roboto,Arial,sans-serif;font-size:14px;margin:12px;word-break:break-word}</style>"
             + html;
    }

    /// <summary>Domena adresu (po @), małymi literami.</summary>
    public static string Domain(string? address) =>
        address?.Split('@') is [_, var d] ? d.Trim().ToLowerInvariant() : "";
}
