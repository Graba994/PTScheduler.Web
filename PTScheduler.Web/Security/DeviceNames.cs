namespace PTScheduler.Web.Security;

/// <summary>Czytelna nazwa urządzenia z nagłówka przeglądarki, np. „iPhone · Safari”.</summary>
public static class DeviceNames
{
    public static (string Icon, string Label) Describe(string? userAgent)
    {
        var ua = userAgent ?? "";
        if (ua.Length == 0) return ("bi-question-circle", "Nieznane urządzenie");
        var browser = ua.Contains("Edg/") ? "Edge"
            : ua.Contains("OPR/") ? "Opera"
            : ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("SamsungBrowser/") ? "Samsung Internet"
            : ua.Contains("Chrome/") ? "Chrome"
            : ua.Contains("Safari/") ? "Safari"
            : "przeglądarka";
        var (icon, os) = ua.Contains("iPhone") ? ("bi-phone", "iPhone")
            : ua.Contains("iPad") ? ("bi-tablet", "iPad")
            : ua.Contains("Android") ? (ua.Contains("Mobile") ? ("bi-phone", "Telefon z Androidem") : ("bi-tablet", "Tablet z Androidem"))
            : ua.Contains("Mac OS X") ? ("bi-laptop", "Mac")
            : ua.Contains("Windows") ? ("bi-pc-display", "Komputer z Windows")
            : ua.Contains("Linux") ? ("bi-pc-display", "Komputer z Linuksem")
            : ("bi-globe", "Urządzenie");
        return (icon, $"{os} · {browser}");
    }
}
