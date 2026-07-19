using System.Text.RegularExpressions;

namespace PTScheduler.Web.Components.Shared;

/// <summary>
/// Converts a pasted video URL (YouTube / Bunny Stream / Google Drive) into an
/// embeddable iframe src. Unknown URLs are returned as-is (assumed embeddable).
/// </summary>
public static partial class VideoEmbed
{
    public static string? ToEmbed(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();

        var yt = YouTube().Match(url);
        if (yt.Success) return $"https://www.youtube.com/embed/{yt.Groups[1].Value}";

        var gd = GoogleDrive().Match(url);
        if (gd.Success) return $"https://drive.google.com/file/d/{gd.Groups[1].Value}/preview";

        if (url.Contains("mediadelivery.net/embed/", StringComparison.OrdinalIgnoreCase))
            return url;

        var bunny = BunnyPlay().Match(url);
        if (bunny.Success) return $"https://iframe.mediadelivery.net/embed/{bunny.Groups[1].Value}/{bunny.Groups[2].Value}";

        return url;
    }

    [GeneratedRegex(@"(?:youtu\.be/|youtube\.com/(?:watch\?v=|embed/|shorts/|live/))([A-Za-z0-9_-]{6,})", RegexOptions.IgnoreCase)]
    private static partial Regex YouTube();

    [GeneratedRegex(@"drive\.google\.com/file/d/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GoogleDrive();

    [GeneratedRegex(@"mediadelivery\.net/play/(\d+)/([A-Za-z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BunnyPlay();
}
