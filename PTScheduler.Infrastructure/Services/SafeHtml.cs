using Ganss.Xss;

namespace PTScheduler.Infrastructure.Services;

/// <summary>
/// Sanityzacja HTML wpisywanego w edytorach (treść kursów, opis „O mnie”).
/// Treść lekcji mógł edytować trener, a była wyświetlana adminowi i klientom
/// jako surowy HTML — skrypt w lekcji uruchamiał się z uprawnieniami oglądającego.
/// Dopuszczamy zwykłe formatowanie oraz osadzenia wideo z zaufanych serwisów.
/// </summary>
public static class SafeHtml
{
    private static readonly string[] AllowedIframeHosts =
    [
        "www.youtube.com", "youtube.com", "www.youtube-nocookie.com", "player.vimeo.com",
        "iframe.mediadelivery.net", "video.bunnycdn.com"
    ];

    private static readonly HtmlSanitizer Sanitizer = Create();

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Add("iframe");
        foreach (var a in new[] { "allowfullscreen", "frameborder", "allow", "loading", "target", "rel" })
            s.AllowedAttributes.Add(a);
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement el && el.TagName.Equals("IFRAME", StringComparison.OrdinalIgnoreCase))
            {
                var src = el.GetAttribute("src");
                var ok = Uri.TryCreate(src, UriKind.Absolute, out var uri)
                         && uri.Scheme == Uri.UriSchemeHttps
                         && AllowedIframeHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
                if (!ok) el.Remove();
            }
        };
        return s;
    }

    public static string Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? string.Empty : Sanitizer.Sanitize(html);

    public static string? SanitizeOrNull(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : Sanitizer.Sanitize(html);
}
