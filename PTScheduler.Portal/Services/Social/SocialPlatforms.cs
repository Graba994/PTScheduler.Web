using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services.Social;

/// <summary>Opis platform: nazwa, ikona, limit tekstu, czy publikujemy przez API i dlaczego nie (kanały ręczne).</summary>
public static class SocialPlatforms
{
    public sealed record Info(SocialPlatform Platform, string Label, string Icon, string Color, bool Api, int MaxText, string UploadUrl,
        string Tone, string? WhyManual, bool NeedsMedia, bool VideoOnly);

    public static readonly Info[] All =
    [
        new(SocialPlatform.Facebook, "Facebook", "bi-facebook", "#1877F2", true, 63206, "https://www.facebook.com/",
            "Może być trochę dłużej niż na Instagramie; link w treści działa; mniej hashtagów (2–3).", null, false, false),
        new(SocialPlatform.Threads, "Threads", "bi-threads", "#000000", true, 500, "https://www.threads.com/",
            "Krótko i rozmownie, jak wpis do znajomych z branży; najwyżej 500 znaków; 0–1 hashtag.", null, false, false),
        new(SocialPlatform.TikTok, "TikTok", "bi-tiktok", "#010101", false, 2200, "https://www.tiktok.com/upload",
            "Krótki, lekki opis do filmu; 3–5 hashtagów; pierwsze słowa zaczepiają.",
            "API TikToka publikuje publicznie dopiero po audycie aplikacji — do tego czasu wyłącznie prywatnie. Na start: gotowa paczka i przypomnienie.", true, true),
        new(SocialPlatform.YouTube, "YouTube Shorts", "bi-youtube", "#FF0000", false, 5000, "https://studio.youtube.com/",
            "Pierwsza linia to tytuł (do 100 znaków), potem 2–3 zdania opisu i #shorts.",
            "Filmy wgrane przez niezweryfikowaną aplikację są prywatne, dopóki Google nie zatwierdzi aplikacji. Na start: gotowa paczka i przypomnienie.", true, true),
        new(SocialPlatform.LinkedIn, "LinkedIn", "bi-linkedin", "#0A66C2", false, 3000, "https://www.linkedin.com/feed/",
            "Ton biznesowy, konkret i liczby z wiedzy; krótkie akapity; 3 hashtagi branżowe.",
            "Publikowanie na stronie firmy przez API wymaga zgody LinkedIn na dostęp partnerski. Na start: gotowa paczka i przypomnienie.", false, false),
        new(SocialPlatform.X, "X (Twitter)", "bi-twitter-x", "#000000", false, 280, "https://x.com/compose/post",
            "Jedno zdanie z haczykiem, najwyżej 280 znaków razem z linkiem.",
            "Dostęp do API X jest płatny. Na start: gotowa paczka i przypomnienie.", false, false),
        new(SocialPlatform.Pinterest, "Pinterest", "bi-pinterest", "#E60023", false, 500, "https://www.pinterest.com/pin-creation-tool/",
            "Tytuł z korzyścią i opis z frazami, których szuka trener; link do strony.",
            "Na start ręcznie — publikację przez API można dodać później.", true, false),
    ];

    public static Info Get(SocialPlatform p) => All.First(x => x.Platform == p);

    public const string InstagramIcon = "bi-instagram";
    public const string InstagramColor = "#E1306C";

    public static string TargetLabel(SocialTargetStatus s) => s switch
    {
        SocialTargetStatus.Pending => "Czeka",
        SocialTargetStatus.Publishing => "Publikuję…",
        SocialTargetStatus.Published => "Opublikowano",
        SocialTargetStatus.Failed => "Błąd",
        SocialTargetStatus.ManualDue => "Do wrzucenia",
        SocialTargetStatus.Skipped => "Pominięty",
        _ => s.ToString()
    };

    public static string TargetTone(SocialTargetStatus s) => s switch
    {
        SocialTargetStatus.Published => "ok",
        SocialTargetStatus.Failed => "danger",
        SocialTargetStatus.ManualDue => "warn",
        SocialTargetStatus.Publishing => "info",
        _ => "muted"
    };

    /// <summary>
    /// Problemy posta dla danej platformy (puste = można publikować). Obrazy to JPEG z Portalu, filmy — publiczne adresy MP4.
    /// </summary>
    public static List<string> Problems(SocialPlatform p, InstagramPost post, string caption)
    {
        var list = new List<string>();
        var info = Get(p);
        var images = post.Assets.Count(a => a.Kind == "image");
        var videos = post.Assets.Count(a => a.Kind == "video");
        if (caption.Length > info.MaxText) list.Add($"{info.Label}: opis ma {caption.Length} znaków (najwięcej {info.MaxText}).");
        if (info.VideoOnly && videos == 0) list.Add($"{info.Label}: potrzebny film.");
        if (info.NeedsMedia && post.Assets.Count == 0) list.Add($"{info.Label}: dodaj grafikę albo film.");
        if (p == SocialPlatform.Facebook && videos > 0 && (images > 0 || videos > 1)) list.Add("Facebook: jeden film albo zdjęcia — nie oba naraz.");
        if (p == SocialPlatform.Threads && videos > 1) list.Add("Threads: najwyżej jeden film.");
        if (p == SocialPlatform.Threads && post.Assets.Count > 20) list.Add("Threads: najwyżej 20 grafik.");
        if (string.IsNullOrWhiteSpace(caption) && post.Assets.Count == 0) list.Add($"{info.Label}: pusty post.");
        return list;
    }
}
