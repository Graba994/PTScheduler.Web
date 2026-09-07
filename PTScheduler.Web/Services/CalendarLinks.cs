using System.Text;

namespace PTScheduler.Web.Services;

/// <summary>
/// Builds "add to calendar" links/files for a session. Purely client-facing
/// formatting — no persistence, no external API calls (unlike the Google Meet
/// integration, which creates a real calendar event via OAuth).
/// </summary>
public static class CalendarLinks
{
    public static string GoogleUrl(string title, DateTime start, DateTime end, string details, string location)
    {
        var dates = $"{ToUtcStamp(start)}/{ToUtcStamp(end)}";
        var qs = $"action=TEMPLATE&text={Uri.EscapeDataString(title)}&dates={dates}" +
                 $"&details={Uri.EscapeDataString(details)}&location={Uri.EscapeDataString(location)}";
        return $"https://calendar.google.com/calendar/render?{qs}";
    }

    public static string OutlookUrl(string title, DateTime start, DateTime end, string details, string location)
    {
        var qs = $"path=/calendar/action/compose&rru=addevent&subject={Uri.EscapeDataString(title)}" +
                 $"&startdt={Uri.EscapeDataString(start.ToUniversalTime().ToString("O"))}" +
                 $"&enddt={Uri.EscapeDataString(end.ToUniversalTime().ToString("O"))}" +
                 $"&body={Uri.EscapeDataString(details)}&location={Uri.EscapeDataString(location)}";
        return $"https://outlook.live.com/calendar/0/deeplink/compose?{qs}";
    }

    public static byte[] BuildIcs(string title, DateTime start, DateTime end, string details, string location, string uid)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//PTScheduler//Sessions//PL\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:{uid}\r\n");
        sb.Append($"DTSTAMP:{ToUtcStamp(DateTime.UtcNow)}\r\n");
        sb.Append($"DTSTART:{ToUtcStamp(start)}\r\n");
        sb.Append($"DTEND:{ToUtcStamp(end)}\r\n");
        sb.Append($"SUMMARY:{Escape(title)}\r\n");
        if (!string.IsNullOrWhiteSpace(details)) sb.Append($"DESCRIPTION:{Escape(details)}\r\n");
        if (!string.IsNullOrWhiteSpace(location)) sb.Append($"LOCATION:{Escape(location)}\r\n");
        sb.Append("END:VEVENT\r\n");
        sb.Append("END:VCALENDAR\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string ToUtcStamp(DateTime dt) =>
        dt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
