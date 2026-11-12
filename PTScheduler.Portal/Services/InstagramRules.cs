using System.Text.RegularExpressions;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Reguły Instagrama bez sieci i bazy (łatwe do testów): limity opisu, hashtagi, terminy publikacji,
/// zaangażowanie, najlepsze godziny, odnawianie tokenu, rozmiary grafik i proste komentarze do autoodpowiedzi.
/// </summary>
public static partial class InstagramRules
{
    public const int MaxCaption = 2200;
    public const int MaxHashtags = 30;
    public const int MaxMentions = 20;
    public const int MaxCarousel = 10;
    /// <summary>Token Instagrama żyje 60 dni; odnawiamy, gdy zostało mniej niż tyle dni (i ma co najmniej dobę).</summary>
    public const int RefreshWhenDaysLeft = 20;
    public const int FeedWidth = 1080;

    public static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public sealed record Pillar(string Key, string Label, string Icon, string Hint);

    /// <summary>Filary treści marki — żeby profil nie był samą reklamą (zasada: najwyżej co piąty post sprzedaje).</summary>
    public static readonly Pillar[] Pillars =
    [
        new("porada", "Porada dla trenera", "bi-lightbulb", "biznes trenera: pozyskanie i utrzymanie klientów, ceny, organizacja czasu"),
        new("funkcja", "Funkcja Hantly", "bi-phone", "jedna funkcja aplikacji i jaki problem trenera rozwiązuje"),
        new("mit", "Mit czy fakt", "bi-patch-question", "popularny mit treningowy albo biznesowy i fakt, który go obala"),
        new("pytanie", "Pytanie do trenerów", "bi-chat-heart", "pytanie, na które trenerzy chętnie odpowiedzą w komentarzu"),
        new("oferta", "Oferta i program testerów", "bi-gift", "okres próbny, program testerów (3 miesiące gratis za cotygodniową opinię)"),
        new("kulisy", "Kulisy i nowości", "bi-stars", "co nowego w aplikacji, jak powstaje, plany na przyszłość"),
    ];

    public static Pillar? FindPillar(string? key) => Pillars.FirstOrDefault(p => p.Key == key);
    public static string PillarLabel(string? key) => FindPillar(key)?.Label ?? "Inne";

    public static string StatusLabel(InstagramPostStatus s) => s switch
    {
        InstagramPostStatus.Idea => "Pomysł",
        InstagramPostStatus.Draft => "Szkic",
        InstagramPostStatus.ToApprove => "Do akceptacji",
        InstagramPostStatus.Scheduled => "Zaplanowany",
        InstagramPostStatus.Publishing => "Publikuję…",
        InstagramPostStatus.Published => "Opublikowany",
        InstagramPostStatus.Failed => "Błąd",
        _ => s.ToString()
    };

    public static string StatusTone(InstagramPostStatus s) => s switch
    {
        InstagramPostStatus.ToApprove => "warn",
        InstagramPostStatus.Scheduled => "info",
        InstagramPostStatus.Publishing => "info",
        InstagramPostStatus.Published => "ok",
        InstagramPostStatus.Failed => "danger",
        _ => "muted"
    };

    public static string KindLabel(InstagramPostKind k) => k switch
    {
        InstagramPostKind.Image => "Zdjęcie",
        InstagramPostKind.Carousel => "Karuzela",
        InstagramPostKind.Reel => "Rolka",
        InstagramPostKind.Story => "Relacja",
        _ => k.ToString()
    };

    public static string KindIcon(InstagramPostKind k) => k switch
    {
        InstagramPostKind.Carousel => "bi-collection",
        InstagramPostKind.Reel => "bi-film",
        InstagramPostKind.Story => "bi-phone-vibrate",
        _ => "bi-image"
    };

    // ── Opis ──

    [GeneratedRegex(@"(?<![\w#])#([\p{L}\p{N}_]+)")]
    private static partial Regex HashtagRx();

    [GeneratedRegex(@"(?<![\w@])@([A-Za-z0-9._]+)")]
    private static partial Regex MentionRx();

    public static List<string> Hashtags(string? caption) =>
        HashtagRx().Matches(caption ?? "").Select(m => "#" + m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Problemy opisu, które zablokowałyby publikację (puste = w porządku).</summary>
    public static List<string> CaptionProblems(string? caption)
    {
        var c = caption ?? "";
        var list = new List<string>();
        if (c.Length > MaxCaption) list.Add($"Opis ma {c.Length} znaków — Instagram przyjmie najwyżej {MaxCaption}.");
        var tags = HashtagRx().Matches(c).Count;
        if (tags > MaxHashtags) list.Add($"{tags} hashtagów — najwyżej {MaxHashtags}.");
        var mentions = MentionRx().Matches(c).Count;
        if (mentions > MaxMentions) list.Add($"{mentions} oznaczeń @ — najwyżej {MaxMentions}.");
        return list;
    }

    /// <summary>Dokleja brakujące hashtagi (bez duplikatów, z limitem) w osobnym akapicie na końcu.</summary>
    public static string AddHashtags(string? caption, IEnumerable<string> tags, int max = 12)
    {
        var c = (caption ?? "").TrimEnd();
        var have = Hashtags(c).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var room = Math.Max(0, Math.Min(max, MaxHashtags) - have.Count);
        var add = tags.Select(NormalizeTag).Where(t => t is not null && have.Add(t!)).Take(room).ToList();
        if (add.Count == 0) return c;
        return c + (c.Length == 0 ? "" : "\n\n") + string.Join(" ", add);
    }

    public static string? NormalizeTag(string? t)
    {
        var s = (t ?? "").Trim().TrimStart('#').Replace(" ", "");
        s = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        return s.Length == 0 ? null : "#" + s.ToLowerInvariant();
    }

    public static List<string> ParseTags(string? text) =>
        (text ?? "").Split([' ', ',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries).Select(NormalizeTag).Where(t => t is not null).Distinct().ToList()!;

    // ── Terminy ──

    public static HashSet<int> ParseDays(string? csv)
    {
        var days = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x.Trim(), out var d) ? d : 0).Where(d => d is >= 1 and <= 7).ToHashSet();
        return days.Count == 0 ? [1, 3, 5] : days;
    }

    public static TimeOnly ParseTime(string? hhmm) => TimeOnly.TryParse(hhmm, out var t) ? t : new TimeOnly(18, 0);

    /// <summary>Dzień tygodnia 1 (pon) … 7 (niedz).</summary>
    public static int IsoDay(DateTime d) => d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;

    public static DateTime ToUtc(DateTime warsawLocal) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(warsawLocal, DateTimeKind.Unspecified), Warsaw);
    public static DateTime ToWarsaw(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Warsaw);

    /// <summary>
    /// Wolne terminy (UTC) w wybrane dni o stałej godzinie, od „teraz + 30 min”, z pominięciem dni,
    /// w które już coś jest zaplanowane. <paramref name="horizonDays"/> — jak daleko szukać.
    /// </summary>
    public static List<DateTime> FreeSlots(DateTime nowUtc, IReadOnlySet<int> days, TimeOnly time, IEnumerable<DateTime> takenUtc, int count, int horizonDays = 21)
    {
        var taken = takenUtc.Select(t => DateOnly.FromDateTime(ToWarsaw(t))).ToHashSet();
        var local = ToWarsaw(nowUtc);
        var list = new List<DateTime>();
        for (var i = 0; i <= horizonDays && list.Count < count; i++)
        {
            var day = local.Date.AddDays(i);
            if (!days.Contains(IsoDay(day)) || taken.Contains(DateOnly.FromDateTime(day))) continue;
            var at = ToUtc(day + time.ToTimeSpan());
            if (at < nowUtc.AddMinutes(30)) continue;
            list.Add(at);
        }
        return list;
    }

    // ── Statystyki ──

    public static int Interactions(InstagramMedia m) => m.Interactions ?? (m.Likes + m.Comments + (m.Saved ?? 0) + (m.Shares ?? 0));

    /// <summary>Zaangażowanie w % zasięgu (gdy brak zasięgu — w % obserwujących).</summary>
    public static double? EngagementRate(InstagramMedia m, int followers)
    {
        var basis = m.Reach is > 0 ? m.Reach.Value : followers;
        return basis <= 0 ? null : Math.Round(100.0 * Interactions(m) / basis, 1);
    }

    public sealed record Slot(int Day, int Hour, double Score, int Posts);

    /// <summary>Najlepsze pory (dzień tygodnia × godzina w Polsce) ze średniego zaangażowania własnych postów.</summary>
    public static List<Slot> BestSlots(IEnumerable<InstagramMedia> media, int followers, int minPosts = 1, int take = 3) =>
        media.Where(m => m.ProductType != "STORY")
            .Select(m => (L: ToWarsaw(m.PostedAt), Rate: EngagementRate(m, followers) ?? 0))
            .GroupBy(x => (Day: IsoDay(x.L), x.L.Hour))
            .Where(g => g.Count() >= minPosts)
            .Select(g => new Slot(g.Key.Day, g.Key.Hour, Math.Round(g.Average(x => x.Rate), 1), g.Count()))
            .OrderByDescending(s => s.Score).ThenByDescending(s => s.Posts).Take(take).ToList();

    public static string DayName(int isoDay) => isoDay switch
    {
        1 => "poniedziałek", 2 => "wtorek", 3 => "środa", 4 => "czwartek", 5 => "piątek", 6 => "sobota", _ => "niedziela"
    };

    public static string DayShort(int isoDay) => isoDay switch
    {
        1 => "pn", 2 => "wt", 3 => "śr", 4 => "cz", 5 => "pt", 6 => "sb", _ => "nd"
    };

    // ── Token ──

    public static bool TokenNeedsRefresh(DateTime expiresAt, DateTime refreshedAt, DateTime now) =>
        expiresAt - now < TimeSpan.FromDays(RefreshWhenDaysLeft) && now - refreshedAt > TimeSpan.FromHours(25) && expiresAt > now;

    // ── Grafiki ──

    /// <summary>Rozmiar grafiki w pikselach: relacja 1080×1920, post 1080 szerokości i proporcje 4:5 … 1,91:1 (jak przyjmuje Instagram).</summary>
    public static (int W, int H, bool Framed) TargetSize(int w, int h, bool story)
    {
        if (story) return (1080, 1920, w <= 0 || h <= 0 || Math.Abs((double)w / h - 1080.0 / 1920) > 0.02);
        if (w <= 0 || h <= 0) return (1080, 1350, true);
        var ratio = (double)w / h;
        if (ratio < 0.8) return (1080, 1350, true);      // za wysokie (np. zrzut z telefonu) — na tle marki
        if (ratio > 1.91) return (1080, 566, true);      // za szerokie
        return (1080, (int)Math.Round(1080 / ratio), false);
    }

    /// <summary>Szerokość i wysokość z nagłówka JPEG / PNG / WebP / GIF (bez bibliotek). (0,0) = nie rozpoznano.</summary>
    public static (int W, int H) ImageSize(ReadOnlySpan<byte> b)
    {
        if (b.Length > 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return (BE32(b[16..]), BE32(b[20..]));
        if (b.Length > 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
            return (b[6] | b[7] << 8, b[8] | b[9] << 8);
        if (b.Length > 30 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
        {
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == ' ') return ((b[26] | b[27] << 8) & 0x3FFF, (b[28] | b[29] << 8) & 0x3FFF);
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L') { var v = b[21] | b[22] << 8 | b[23] << 16 | b[24] << 24; return ((v & 0x3FFF) + 1, ((v >> 14) & 0x3FFF) + 1); }
            if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'X') return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
        }
        if (b.Length > 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                var marker = b[i + 1];
                if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                var len = b[i + 2] << 8 | b[i + 3];
                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                    return (b[i + 7] << 8 | b[i + 8], b[i + 5] << 8 | b[i + 6]);
                i += 2 + len;
            }
        }
        return (0, 0);
    }

    private static int BE32(ReadOnlySpan<byte> b) => b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3];

    public static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    // ── Komentarze ──

    [GeneratedRegex(@"^[\s\p{So}\p{Sk}\p{Cs}‍️!.❤♥]*$")]
    private static partial Regex OnlyEmojiRx();

    private static readonly string[] Thanks =
        ["super", "świetne", "swietne", "ekstra", "dzięki", "dzieki", "dziękuję", "dziekuje", "fajne", "fajnie", "mega", "sztos", "petarda", "brawo", "kocham", "genialne", "wow", "top"];

    /// <summary>
    /// Krótki, pozytywny komentarz bez pytania (emotki, „super!”, „dzięki”) — na taki autopilot może odpowiedzieć sam.
    /// Pytania, skargi i dłuższe wypowiedzi zawsze czekają na człowieka.
    /// </summary>
    public static bool IsSimplePraise(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Length > 60 || t.Contains('?') || t.Contains("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (OnlyEmojiRx().IsMatch(t)) return true;
        var lower = t.ToLowerInvariant();
        var words = lower.Split([' ', ',', '!', '.', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 6 && words.Any(w => Thanks.Any(x => w.StartsWith(x, StringComparison.Ordinal)));
    }

    /// <summary>Wyciąga JSON z odpowiedzi modelu (bez ```json i tekstu dookoła).</summary>
    public static string? JsonPart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0) return null;
        var close = text[start] == '{' ? '}' : ']';
        var end = text.LastIndexOf(close);
        return end > start ? text[start..(end + 1)] : null;
    }
}
