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

    public sealed record FeedEvent(string Uid, DateTime StartUtc, DateTime EndUtc, string Title,
        string? Description, string? Location, string? Url, bool Cancelled);

    /// <summary>
    /// Kalendarz do subskrypcji (webcal/ICS) — wszystkie wizyty trenera w jednym pliku.
    /// Czasy w UTC („Z”), więc nie potrzeba VTIMEZONE; linie zawijane co 75 bajtów (RFC 5545).
    /// </summary>
    public static byte[] BuildFeed(string calendarName, IEnumerable<FeedEvent> events)
    {
        var sb = new StringBuilder();
        void Line(string text) => AppendFolded(sb, text);

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//PTScheduler//Trainer feed//PL");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line($"X-WR-CALNAME:{Escape(calendarName)}");
        Line("X-WR-TIMEZONE:Europe/Warsaw");
        // Podpowiedź częstotliwości odświeżania (Apple/Outlook; Google i tak odświeża po swojemu).
        Line("REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        Line("X-PUBLISHED-TTL:PT1H");
        var stamp = ToUtcStamp(DateTime.UtcNow);
        foreach (var e in events)
        {
            Line("BEGIN:VEVENT");
            Line($"UID:{e.Uid}");
            Line($"DTSTAMP:{stamp}");
            Line($"DTSTART:{ToUtcStamp(e.StartUtc)}");
            Line($"DTEND:{ToUtcStamp(e.EndUtc)}");
            Line($"SUMMARY:{Escape(e.Title)}");
            if (!string.IsNullOrWhiteSpace(e.Description)) Line($"DESCRIPTION:{Escape(e.Description)}");
            if (!string.IsNullOrWhiteSpace(e.Location)) Line($"LOCATION:{Escape(e.Location)}");
            if (!string.IsNullOrWhiteSpace(e.Url)) Line($"URL:{e.Url}");
            Line(e.Cancelled ? "STATUS:CANCELLED" : "STATUS:CONFIRMED");
            Line("END:VEVENT");
        }
        Line("END:VCALENDAR");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // RFC 5545 §3.1: linie dłuższe niż 75 bajtów zawijamy (CRLF + spacja), nie tnąc znaków UTF-8.
    private static void AppendFolded(StringBuilder sb, string line)
    {
        const int limit = 75;
        var bytes = 0;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(line);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            var size = Encoding.UTF8.GetByteCount(element);
            if (bytes + size > limit)
            {
                sb.Append("\r\n ");
                bytes = 1;
            }
            sb.Append(element);
            bytes += size;
        }
        sb.Append("\r\n");
    }

    private static string ToUtcStamp(DateTime dt) =>
        dt.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
}
