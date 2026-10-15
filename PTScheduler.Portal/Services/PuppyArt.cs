namespace PTScheduler.Portal.Services;

/// <summary>
/// Wbudowane grafiki agenta Portalu — szczeniaka golden retrievera (domyślnie „Leo”) — w tych samych
/// sześciu nastrojach co maskotka trenerów. W Panelu każdą można podmienić własnym plikiem.
/// </summary>
public static class PuppyArt
{
    /// <summary>Nastroje Leo — klucze jak w <see cref="MascotArt"/>, opisy pod pracę administratora.</summary>
    public static readonly MascotArt.Mood[] Moods =
    [
        new("hello", "Wita się", "poranny raport, powitanie w Panelu"),
        new("happy", "Zadowolony", "dobre wiadomości, wszystko w porządku"),
        new("celebrate", "Świętuje", "nowa rejestracja, pierwsza płatność, aktywacja"),
        new("think", "Myśli", "wnioski, analizy, sugestie"),
        new("remind", "Czujny", "zaległości, awarie, zgłoszenia, ryzyko odejścia"),
        new("sleep", "Śpi", "wyciszony agent"),
    ];

    private const string Defs = """
        <defs>
          <linearGradient id="lh" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F7C670"/><stop offset="1" stop-color="#E29E40"/></linearGradient>
          <linearGradient id="lb" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F2BB5E"/><stop offset="1" stop-color="#D68D2F"/></linearGradient>
          <linearGradient id="le" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#D38A30"/><stop offset="1" stop-color="#B66E22"/></linearGradient>
        </defs>
        """;

    private const string BodyPart = """
        <ellipse cx="60" cy="108" rx="34" ry="4.5" fill="#0F172A" opacity=".13"/>
        <path d="M82 97 C97 97 102 84 99 73 C98 68 92 69 93 74 C94 83 90 89 81 89Z" fill="#DE9C3C"/>
        <ellipse cx="60" cy="89" rx="25" ry="19" fill="url(#lb)"/>
        <ellipse cx="60" cy="88" rx="11.5" ry="13.5" fill="#FCE6BE"/>
        <ellipse cx="48.5" cy="104" rx="7.8" ry="4.8" fill="#F6CD8A"/>
        <ellipse cx="71.5" cy="104" rx="7.8" ry="4.8" fill="#F6CD8A"/>
        <path d="M46 103.5 v2.6 M49.5 103 v3 M74 103.5 v2.6 M70.5 103 v3" stroke="#D9A35A" stroke-width="1.2" stroke-linecap="round"/>
        <rect x="42" y="68.5" width="36" height="6.5" rx="3.2" fill="#7C3AED"/>
        <rect x="42" y="68.5" width="36" height="2.2" rx="1.1" fill="#fff" opacity=".25"/>
        <circle cx="60" cy="78.5" r="3.8" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.2"/>
        """;

    private const string HeadBase = """
        <ellipse cx="60" cy="48" rx="27" ry="24" fill="url(#lh)"/>
        <path d="M54.5 25.5 q5.5 -7 11 0 q-5.5 -2.4 -11 0z" fill="#E8A94C"/>
        <ellipse cx="51" cy="31" rx="7" ry="3" fill="#fff" opacity=".2"/>
        <path d="M37 33 C26 35 21 50 24 62 C26 71 35 71 38 62 C40 53 41 43 37 33Z" fill="url(#le)"/>
        <path d="M83 33 C94 35 99 50 96 62 C94 71 85 71 82 62 C80 53 79 43 83 33Z" fill="url(#le)"/>
        <ellipse cx="60" cy="60" rx="15" ry="10.5" fill="#FCE6BE"/>
        <ellipse cx="60" cy="54.5" rx="5.2" ry="3.8" fill="#3A2A1F"/>
        <ellipse cx="58.4" cy="53.4" rx="1.7" ry=".9" fill="#fff" opacity=".55"/>
        """;

    private const string Ink = "#2B1D14";
    private const string Cheeks = """<circle cx="41.5" cy="56" r="3.8" fill="#F9A8D4" opacity=".6"/><circle cx="78.5" cy="56" r="3.8" fill="#F9A8D4" opacity=".6"/>""";

    private static string OpenEyes(double dx = 0, double dy = 0) =>
        $"""<ellipse cx="49" cy="45" rx="4.3" ry="4.9" fill="{Ink}"/><ellipse cx="71" cy="45" rx="4.3" ry="4.9" fill="{Ink}"/><circle cx="{50.5 + dx}" cy="{43.3 + dy}" r="1.6" fill="#fff"/><circle cx="{72.5 + dx}" cy="{43.3 + dy}" r="1.6" fill="#fff"/>""";

    private const string HappyEyes = $"""<path d="M44.5 46.5 q4.5 -5.6 9 0" stroke="{Ink}" stroke-width="2.8" fill="none" stroke-linecap="round"/><path d="M66.5 46.5 q4.5 -5.6 9 0" stroke="{Ink}" stroke-width="2.8" fill="none" stroke-linecap="round"/>""";

    private const string ClosedEyes = $"""<path d="M44.5 45 q4.5 4 9 0" stroke="{Ink}" stroke-width="2.6" fill="none" stroke-linecap="round"/><path d="M66.5 45 q4.5 4 9 0" stroke="{Ink}" stroke-width="2.6" fill="none" stroke-linecap="round"/>""";

    /// <summary>Pyszczek „w” pod noskiem.</summary>
    private const string Mouth = """<path d="M60 58.2 v3 M60 61.2 q-3.6 3.4 -7.2 .6 M60 61.2 q3.6 3.4 7.2 .6" stroke="#3A2A1F" stroke-width="1.8" fill="none" stroke-linecap="round"/>""";

    private const string Tongue = """<path d="M56.6 62.6 h6.8 v3 a3.4 3.4 0 0 1 -6.8 0z" fill="#FB7185"/><path d="M60 63 v3.4" stroke="#E11D48" stroke-width=".9" stroke-linecap="round" opacity=".6"/>""";

    private static string Face(string mood) => mood switch
    {
        "hello" => OpenEyes() + Cheeks + Mouth + Tongue,
        "happy" => HappyEyes + Cheeks + Mouth + Tongue,
        "celebrate" => HappyEyes + Cheeks + """<path d="M53 61 q7 11 14 0z" fill="#3A2A1F"/><path d="M56 64.5 q4 3.6 8 0 v.4 a4 3 0 0 1 -8 0z" fill="#FB7185"/>""",
        "think" => OpenEyes(1.4, -1.6) + $"""<path d="M44.5 38.6 q4.5 -1.6 9 0" stroke="{Ink}" stroke-width="1.8" fill="none" stroke-linecap="round"/><path d="M66.5 36 q4.5 -2.6 9 -.4" stroke="{Ink}" stroke-width="1.8" fill="none" stroke-linecap="round"/>"""
            + """<path d="M60 58.2 v3 M56 62.6 h8" stroke="#3A2A1F" stroke-width="1.8" fill="none" stroke-linecap="round"/>""",
        "remind" => OpenEyes() + Cheeks + $"""<path d="M44 39.4 l9 -2.6" stroke="{Ink}" stroke-width="1.8" stroke-linecap="round"/><path d="M76 39.4 l-9 -2.6" stroke="{Ink}" stroke-width="1.8" stroke-linecap="round"/>"""
            + """<path d="M60 58.2 v2.6" stroke="#3A2A1F" stroke-width="1.8" stroke-linecap="round"/><ellipse cx="60" cy="63.6" rx="2.8" ry="3.2" fill="#3A2A1F"/>""",
        "sleep" => ClosedEyes + Cheeks + Mouth,
        _ => OpenEyes() + Cheeks + Mouth + Tongue,
    };

    private static string Extras(string mood) => mood switch
    {
        "hello" => """<g fill="#FBBF24"><path d="M101 12 l2.4 5.2 5.2 2.4 -5.2 2.4 -2.4 5.2 -2.4 -5.2 -5.2 -2.4 5.2 -2.4z"/><circle cx="89" cy="10" r="2"/></g>""",
        "celebrate" => """<g><path d="M51 28 L60 5 L69 28 Z" fill="#06D6A0"/><path d="M55.5 17.5 l9 0 M53.4 23 l13.2 0" stroke="#fff" stroke-width="2" opacity=".7"/><circle cx="60" cy="5" r="3.2" fill="#FBBF24"/><rect x="12" y="12" width="6" height="3" rx="1" fill="#F472B6" transform="rotate(-25 15 13)"/><rect x="100" y="10" width="6" height="3" rx="1" fill="#06D6A0" transform="rotate(30 103 11)"/><circle cx="28" cy="22" r="2.4" fill="#FBBF24"/><circle cx="94" cy="26" r="2.4" fill="#60A5FA"/><path d="M84 12 l1.8 3.8 3.8 1.8 -3.8 1.8 -1.8 3.8 -1.8 -3.8 -3.8 -1.8 3.8 -1.8z" fill="#FBBF24"/><path d="M36 8 l1.4 3 3 1.4 -3 1.4 -1.4 3 -1.4 -3 -3 -1.4 3 -1.4z" fill="#A78BFA"/></g>""",
        "think" => """<g fill="#CBD5E1"><circle cx="93" cy="24" r="3"/><circle cx="101" cy="15" r="4"/><ellipse cx="111" cy="6" rx="7" ry="5"/></g>""",
        "remind" => """<g transform="translate(95 4)"><circle cx="9" cy="9" r="9" fill="#F59E0B"/><rect x="7.6" y="3.6" width="2.8" height="7.4" rx="1.4" fill="#fff"/><circle cx="9" cy="14" r="1.6" fill="#fff"/></g>""",
        "sleep" => """<g fill="#94A3B8" font-family="Arial, sans-serif" font-weight="700"><text x="92" y="24" font-size="13">Z</text><text x="103" y="13" font-size="9">z</text></g>""",
        _ => "",
    };

    /// <summary>Gotowy plik SVG dla nastroju (nieznany klucz = „happy”). Przy „myśli” głowa lekko się przechyla.</summary>
    public static string Svg(string mood)
    {
        var tilt = mood == "think" ? """ transform="rotate(-8 60 56)" """ : "";
        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">{Defs}{BodyPart}<g{tilt}>{HeadBase}{Face(mood)}</g>{Extras(mood)}</svg>""";
    }

    public static string DataUri(string mood) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Svg(mood)));
}
