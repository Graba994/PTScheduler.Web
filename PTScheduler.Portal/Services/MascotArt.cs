namespace PTScheduler.Portal.Services;

/// <summary>
/// Wbudowane grafiki maskotki-hantelka (SVG) w sześciu nastrojach. W Panelu każdą można podmienić
/// własnym plikiem — wtedy ta wersja zostaje jako „domyślna” do przywrócenia.
/// </summary>
public static class MascotArt
{
    public sealed record Mood(string Key, string Label, string Hint);

    /// <summary>Nastroje w kolejności wyświetlania w Panelu. Klucze używa też aplikacja trenera.</summary>
    public static readonly Mood[] Moods =
    [
        new("hello", "Wita się", "pierwsze otwarcie dnia, plan na dziś"),
        new("happy", "Zadowolony", "zwykłe, dobre wiadomości"),
        new("celebrate", "Świętuje", "rekordy, kolejne 10 treningów klienta"),
        new("think", "Myśli", "sugestie: klienci, którzy dawno nie byli"),
        new("remind", "Przypomina", "płatności, faktury, akceptacje"),
        new("sleep", "Śpi", "wyciszony asystent"),
    ];

    public static bool IsMood(string? key) => Moods.Any(m => m.Key == key);

    private const string Body = """
        <defs>
          <linearGradient id="hb" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#9F7AEA"/><stop offset="1" stop-color="#6D28D9"/></linearGradient>
          <linearGradient id="hp" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#475569"/><stop offset="1" stop-color="#1E293B"/></linearGradient>
          <clipPath id="hc"><rect x="33" y="38" width="54" height="50" rx="24"/></clipPath>
        </defs>
        <ellipse cx="60" cy="102" rx="36" ry="4.5" fill="#0F172A" opacity=".13"/>
        <rect x="6" y="57" width="10" height="12" rx="3" fill="#64748B"/>
        <rect x="104" y="57" width="10" height="12" rx="3" fill="#64748B"/>
        <rect x="13" y="30" width="15" height="66" rx="6" fill="url(#hp)"/>
        <rect x="92" y="30" width="15" height="66" rx="6" fill="url(#hp)"/>
        <rect x="24" y="37" width="12" height="52" rx="5" fill="#334155"/>
        <rect x="84" y="37" width="12" height="52" rx="5" fill="#334155"/>
        <rect x="16" y="34" width="4" height="22" rx="2" fill="#fff" opacity=".18"/>
        <rect x="95" y="34" width="4" height="22" rx="2" fill="#fff" opacity=".18"/>
        <rect x="33" y="38" width="54" height="50" rx="24" fill="url(#hb)"/>
        <g clip-path="url(#hc)"><rect x="30" y="44" width="60" height="8" fill="#06D6A0"/><rect x="30" y="44" width="60" height="2.5" fill="#fff" opacity=".35"/></g>
        <ellipse cx="47" cy="44" rx="7" ry="3" fill="#fff" opacity=".22"/>
        """;

    private const string Cheeks = """<circle cx="43" cy="72" r="4.5" fill="#F472B6" opacity=".55"/><circle cx="77" cy="72" r="4.5" fill="#F472B6" opacity=".55"/>""";

    private static string OpenEyes(double dx = 0, double dy = 0) =>
        $"""<ellipse cx="51" cy="63" rx="4.6" ry="5.4" fill="#1E1B4B"/><ellipse cx="69" cy="63" rx="4.6" ry="5.4" fill="#1E1B4B"/><circle cx="{52.6 + dx}" cy="{61 + dy}" r="1.8" fill="#fff"/><circle cx="{70.6 + dx}" cy="{61 + dy}" r="1.8" fill="#fff"/>""";

    private const string HappyEyes = """<path d="M46 64 q5 -6 10 0" stroke="#1E1B4B" stroke-width="3" fill="none" stroke-linecap="round"/><path d="M64 64 q5 -6 10 0" stroke="#1E1B4B" stroke-width="3" fill="none" stroke-linecap="round"/>""";

    private const string ClosedEyes = """<path d="M46 64 q5 4 10 0" stroke="#1E1B4B" stroke-width="3" fill="none" stroke-linecap="round"/><path d="M64 64 q5 4 10 0" stroke="#1E1B4B" stroke-width="3" fill="none" stroke-linecap="round"/>""";

    private const string Smile = """<path d="M52 73 q8 7 16 0" stroke="#1E1B4B" stroke-width="3" fill="none" stroke-linecap="round"/>""";

    private const string BigSmile = """<path d="M51 72 q9 11 18 0 z" fill="#1E1B4B"/><path d="M55 76 q5 3 10 0" fill="#F472B6"/>""";

    private static string Face(string mood) => mood switch
    {
        "hello" => OpenEyes() + Cheeks + BigSmile,
        "happy" => HappyEyes + Cheeks + Smile,
        "celebrate" => HappyEyes + Cheeks + """<ellipse cx="60" cy="76" rx="7" ry="6" fill="#1E1B4B"/><ellipse cx="60" cy="79" rx="4" ry="2.5" fill="#F472B6"/>""",
        "think" => OpenEyes(1.6, -1.6) + """<path d="M54 75 h12" stroke="#1E1B4B" stroke-width="3" stroke-linecap="round"/>""",
        "remind" => OpenEyes() + Cheeks + """<ellipse cx="60" cy="75" rx="3.4" ry="3.8" fill="#1E1B4B"/>""",
        "sleep" => ClosedEyes + Cheeks + """<path d="M56 75 q4 3 8 0" stroke="#1E1B4B" stroke-width="2.6" fill="none" stroke-linecap="round"/>""",
        _ => OpenEyes() + Cheeks + Smile,
    };

    private static string Extras(string mood) => mood switch
    {
        "hello" => """<g fill="#FBBF24"><path d="M100 14 l2.4 5.2 5.2 2.4 -5.2 2.4 -2.4 5.2 -2.4 -5.2 -5.2 -2.4 5.2 -2.4z"/><circle cx="88" cy="12" r="2"/></g>""",
        "celebrate" => """<g><rect x="12" y="10" width="6" height="3" rx="1" fill="#F472B6" transform="rotate(-25 15 11)"/><rect x="100" y="8" width="6" height="3" rx="1" fill="#06D6A0" transform="rotate(30 103 9)"/><circle cx="30" cy="20" r="2.4" fill="#FBBF24"/><circle cx="92" cy="22" r="2.4" fill="#60A5FA"/><rect x="58" y="4" width="5" height="3" rx="1" fill="#FBBF24" transform="rotate(15 60 5)"/><path d="M76 12 l1.8 3.8 3.8 1.8 -3.8 1.8 -1.8 3.8 -1.8 -3.8 -3.8 -1.8 3.8 -1.8z" fill="#FBBF24"/><path d="M40 8 l1.4 3 3 1.4 -3 1.4 -1.4 3 -1.4 -3 -3 -1.4 3 -1.4z" fill="#A78BFA"/></g>""",
        "think" => """<g fill="#CBD5E1"><circle cx="92" cy="26" r="3"/><circle cx="100" cy="17" r="4"/><ellipse cx="110" cy="7" rx="7" ry="5"/></g>""",
        "remind" => """<g transform="translate(96 6)"><path d="M8 2 a6 6 0 0 1 6 6 v5 l2 3 h-16 l2 -3 v-5 a6 6 0 0 1 6 -6z" fill="#FBBF24"/><circle cx="8" cy="18.5" r="2.2" fill="#F59E0B"/></g>""",
        "sleep" => """<g fill="#94A3B8" font-family="Arial, sans-serif" font-weight="700"><text x="92" y="24" font-size="13">Z</text><text x="103" y="13" font-size="9">z</text></g>""",
        _ => "",
    };

    /// <summary>Gotowy plik SVG dla nastroju (nieznany klucz = „happy”).</summary>
    public static string Svg(string mood) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">{Body}{Face(mood)}{Extras(mood)}</svg>""";

    public static string DataUri(string mood) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Svg(mood)));
}
