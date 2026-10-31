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

    /// <summary>Hantelek: cień, talerze (lewy i prawy), korpus z opaską. <paramref name="p"/> — prefiks id (inline SVG nie może ich dublować).</summary>
    private static string Shadow => """<ellipse cx="60" cy="102" rx="36" ry="4.5" fill="#0F172A" opacity=".13"/>""";

    private static string Defs(string p) => $"""
        <defs>
          <linearGradient id="{p}b" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#9F7AEA"/><stop offset="1" stop-color="#6D28D9"/></linearGradient>
          <linearGradient id="{p}p" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#475569"/><stop offset="1" stop-color="#1E293B"/></linearGradient>
          <clipPath id="{p}c"><rect x="33" y="38" width="54" height="50" rx="24"/></clipPath>
        </defs>
        """;

    private static string PlateLeft(string p) =>
        $"""<rect x="6" y="57" width="10" height="12" rx="3" fill="#64748B"/><rect x="13" y="30" width="15" height="66" rx="6" fill="url(#{p}p)"/><rect x="24" y="37" width="12" height="52" rx="5" fill="#334155"/><rect x="16" y="34" width="4" height="22" rx="2" fill="#fff" opacity=".18"/>""";

    private static string PlateRight(string p) =>
        $"""<rect x="104" y="57" width="10" height="12" rx="3" fill="#64748B"/><rect x="92" y="30" width="15" height="66" rx="6" fill="url(#{p}p)"/><rect x="84" y="37" width="12" height="52" rx="5" fill="#334155"/><rect x="95" y="34" width="4" height="22" rx="2" fill="#fff" opacity=".18"/>""";

    /// <param name="glint">Animowana wersja: błysk przesuwający się po opasce.</param>
    private static string Core(string p, bool glint = false) =>
        $"""<rect x="33" y="38" width="54" height="50" rx="24" fill="url(#{p}b)"/><g clip-path="url(#{p}c)"><rect x="30" y="44" width="60" height="8" fill="#06D6A0"/><rect x="30" y="44" width="60" height="2.5" fill="#fff" opacity=".35"/>{(glint ? """<rect class="hma-glint" x="40" y="44" width="6" height="8" fill="#fff" opacity="0"/>""" : "")}</g><ellipse cx="47" cy="44" rx="7" ry="3" fill="#fff" opacity=".22"/>""";

    private static string Body => Defs("h") + Shadow + PlateLeft("h") + PlateRight("h") + Core("h");

    private const string Cheeks = """<circle cx="43" cy="72" r="4.5" fill="#F472B6" opacity=".55"/><circle cx="77" cy="72" r="4.5" fill="#F472B6" opacity=".55"/>""";

    /// <param name="live">Wersja animowana: błyski w oczach z klasą „hma-pupil” (idą za kursorem). Statyczna ikona jest bez klas.</param>
    private static string OpenEyes(double dx = 0, double dy = 0, bool live = false)
    {
        var pupil = live ? " class=\"hma-pupil\"" : "";
        return $"""<ellipse cx="51" cy="63" rx="4.6" ry="5.4" fill="#1E1B4B"/><ellipse cx="69" cy="63" rx="4.6" ry="5.4" fill="#1E1B4B"/><circle{pupil} cx="{52.6 + dx}" cy="{61 + dy}" r="1.8" fill="#fff"/><circle{pupil} cx="{70.6 + dx}" cy="{61 + dy}" r="1.8" fill="#fff"/>""";
    }

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

    // ── Wersja animowana ─────────────────────────────────────────────
    // Jeden inline SVG ze wszystkimi minami i efektami; co widać i jak się rusza, ustawiają klasy rodzica:
    // „hm-live st-{scena} md-{nastrój}”. Style są w środku SVG, więc ten sam plik działa w aplikacji trenera,
    // w podglądzie Panelu i na stronie głównej Portalu.

    /// <summary>Sceny animacji: wejście (podskokami z boku), lądowanie, spoczynek, radość (salto), sen.</summary>
    public static readonly string[] Stages = ["enter", "land", "idle", "excited", "sleep"];

    private static string BlinkEyes(double dx = 0, double dy = 0) => $"""<g class="hma-blink">{OpenEyes(dx, dy, live: true)}</g>""";

    private static string Eyes(string svg) => $"""<g class="hma-eyes">{svg}</g>""";
    private static string Mouth(string svg) => $"""<g class="hma-mouth">{svg}</g>""";

    /// <summary>Minka w wersji animowanej: oczy i usta w osobnych grupach (mrugają, mówią, ziewają).</summary>
    private static string LiveFace(string mood) => mood switch
    {
        "hello" => Eyes(BlinkEyes()) + Cheeks + Mouth(BigSmile),
        "happy" => Eyes(HappyEyes) + Cheeks + Mouth(Smile),
        "celebrate" => Eyes(HappyEyes) + Cheeks + Mouth("""<ellipse cx="60" cy="76" rx="7" ry="6" fill="#1E1B4B"/><ellipse cx="60" cy="79" rx="4" ry="2.5" fill="#F472B6"/>"""),
        "think" => Eyes(BlinkEyes(1.6, -1.6)) + Mouth("""<path d="M54 75 h12" stroke="#1E1B4B" stroke-width="3" stroke-linecap="round"/>"""),
        "remind" => Eyes(BlinkEyes()) + Cheeks + Mouth("""<ellipse cx="60" cy="75" rx="3.4" ry="3.8" fill="#1E1B4B"/>"""),
        "sleep" => Eyes(ClosedEyes) + Cheeks + Mouth("""<path d="M56 75 q4 3 8 0" stroke="#1E1B4B" stroke-width="2.6" fill="none" stroke-linecap="round"/>"""),
        _ => Eyes(BlinkEyes()) + Cheeks + Mouth(Smile),
    };

    /// <summary>Elementy sztuczek (widoczne tylko w trakcie): ziewnięcie, serduszko, „+1” przy podnoszeniu, konfetti.</summary>
    /// <summary>Reakcje na wydarzenia — za maskotką: linia „stonks” i wschodzące słońce.</summary>
    private const string ReactBack = """
        <g class="hma-stonks">
          <polyline class="hma-stonks-line" pathLength="100" points="2,100 20,84 32,92 52,66 66,76 92,40 112,18" fill="none" stroke="#22C55E" stroke-width="5" stroke-linecap="round" stroke-linejoin="round"/>
          <path class="hma-stonks-head" d="M104 12 l14 2 -6 13z" fill="#22C55E"/>
          <text class="hma-stonks-txt" x="84" y="12" font-family="Arial, sans-serif" font-weight="800" font-size="12" fill="#16A34A">+zł</text>
        </g>
        <g class="hma-sun">
          <g class="hma-sun-rays" stroke="#FBBF24" stroke-width="3" stroke-linecap="round"><path d="M60 2 v8"/><path d="M60 50 v8"/><path d="M32 30 h8"/><path d="M80 30 h8"/><path d="M40 10 l6 6"/><path d="M74 44 l6 6"/><path d="M80 10 l-6 6"/><path d="M46 44 l-6 6"/></g>
          <circle cx="60" cy="30" r="13" fill="#FCD34D"/>
        </g>
        """;

    /// <summary>Okulary „deal with it” (jadą razem z twarzą) i kropla potu (z ciałem).</summary>
    private const string ReactFace = """<g class="hma-shades"><rect x="42" y="57" width="17" height="10" rx="3.5" fill="#0F172A"/><rect x="61" y="57" width="17" height="10" rx="3.5" fill="#0F172A"/><rect x="58" y="60" width="4" height="2.4" rx="1" fill="#0F172A"/><rect x="45" y="59" width="5" height="2" rx="1" fill="#fff" opacity=".5"/><rect x="64" y="59" width="5" height="2" rx="1" fill="#fff" opacity=".5"/></g>""";
    private const string ReactBody = """<path class="hma-sweat" d="M82 38 c4 6.5 7 10.5 7 14 a7 7 0 0 1 -14 0 c0 -3.5 3 -7.5 7 -14z" fill="#60A5FA"/>""";

    /// <summary>Reakcje na wydarzenia — przed maskotką: puchar, monety, kalendarz, lupa, wykres, metka z ceną, dzwonek.</summary>
    private const string ReactFront = """
        <g class="hma-trophy">
          <path d="M50 4 h20 v8 a10 10 0 0 1 -20 0z" fill="#FBBF24"/>
          <path d="M50 6 h-5 a5 5 0 0 0 5 8 M70 6 h5 a5 5 0 0 1 -5 8" fill="none" stroke="#F59E0B" stroke-width="2.2"/>
          <rect x="57.5" y="21" width="5" height="5" fill="#F59E0B"/><rect x="52" y="26" width="16" height="4" rx="1.5" fill="#D97706"/>
          <path d="M60 7 l1.3 2.7 3 .4 -2.2 2 .5 3 -2.6 -1.4 -2.6 1.4 .5 -3 -2.2 -2 3 -.4z" fill="#fff" opacity=".85"/>
        </g>
        <g class="hma-coins" font-family="Arial, sans-serif" font-weight="800" font-size="8" text-anchor="middle">
          <g class="hma-coin hma-coin1"><circle cx="99" cy="14" r="9.5" fill="#FBBF24" stroke="#D97706" stroke-width="2"/><text x="99" y="17" fill="#92400E">zł</text></g>
          <g class="hma-coin hma-coin2"><circle cx="80" cy="12" r="8.5" fill="#FCD34D" stroke="#D97706" stroke-width="2"/><text x="80" y="15" fill="#92400E">zł</text></g>
          <g class="hma-coin hma-coin3"><circle cx="62" cy="10" r="8" fill="#FBBF24" stroke="#D97706" stroke-width="2"/><text x="62" y="13" fill="#92400E">zł</text></g>
        </g>
        <g class="hma-cal">
          <rect x="84" y="4" width="28" height="26" rx="5" fill="#fff" stroke="#CBD5E1" stroke-width="1.5"/>
          <path d="M84 9 a5 5 0 0 1 5 -5 h18 a5 5 0 0 1 5 5 v3 h-28z" fill="#EF4444"/>
          <rect x="90" y="1" width="3" height="6" rx="1.5" fill="#475569"/><rect x="103" y="1" width="3" height="6" rx="1.5" fill="#475569"/>
          <path class="hma-cal-check" pathLength="100" d="M91 21 l4 4 8 -9" fill="none" stroke="#22C55E" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
        </g>
        <g class="hma-lens"><circle cx="0" cy="0" r="9" fill="#E0F2FE" fill-opacity=".55" stroke="#334155" stroke-width="3"/><path d="M6.5 6.5 l9 9" stroke="#334155" stroke-width="4" stroke-linecap="round"/><path d="M-4 -3 a5 5 0 0 1 4 -3" fill="none" stroke="#fff" stroke-width="2" stroke-linecap="round"/></g>
        <g class="hma-chart">
          <rect x="4" y="2" width="30" height="30" rx="6" fill="#fff" stroke="#CBD5E1" stroke-width="1.5"/>
          <rect class="hma-bar hma-bar1" x="9" y="18" width="5" height="10" rx="1.5" fill="#A78BFA"/>
          <rect class="hma-bar hma-bar2" x="16.5" y="13" width="5" height="15" rx="1.5" fill="#8B5CF6"/>
          <rect class="hma-bar hma-bar3" x="24" y="7" width="5" height="21" rx="1.5" fill="#22C55E"/>
        </g>
        <g class="hma-tag">
          <path d="M100 0 v8" stroke="#94A3B8" stroke-width="1.5"/>
          <path d="M92 8 h16 l4 6 -4 6 h-16 a3 3 0 0 1 -3 -3 v-6 a3 3 0 0 1 3 -3z" fill="#F472B6"/>
          <circle cx="93.5" cy="14" r="1.6" fill="#fff"/>
          <text x="102" y="17.5" font-family="Arial, sans-serif" font-weight="800" font-size="9" text-anchor="middle" fill="#fff">+</text>
        </g>
        <g class="hma-bigbell"><path d="M60 2 a9 9 0 0 1 9 9 v7 l3 4 h-24 l3 -4 v-7 a9 9 0 0 1 9 -9z" fill="#FBBF24"/><circle cx="60" cy="25" r="3" fill="#F59E0B"/><path d="M44 8 q-3 4 0 8 M76 8 q3 4 0 8" fill="none" stroke="#FBBF24" stroke-width="2" stroke-linecap="round"/></g>
        """;

    private const string TrickFx = """
        <ellipse class="hma-yawn" cx="60" cy="76" rx="5" ry="6.5" fill="#1E1B4B"/>
        <path class="hma-heart" d="M60 14 c-3-5-11-4-11 2 c0 5 7 8 11 12 c4-4 11-7 11-12 c0-6-8-7-11-2z" fill="#F472B6"/>
        <text class="hma-plus" x="94" y="22" font-family="Arial, sans-serif" font-weight="800" font-size="14" fill="#06D6A0">+1</text>
        <g class="hma-party">
          <rect style="--dx:-46px;--dy:-34px;--r:-160deg" x="57" y="56" width="6" height="3.5" rx="1" fill="#F472B6"/>
          <rect style="--dx:44px;--dy:-38px;--r:200deg" x="57" y="56" width="6" height="3.5" rx="1" fill="#06D6A0"/>
          <rect style="--dx:-30px;--dy:-58px;--r:120deg" x="57" y="56" width="5" height="5" rx="1" fill="#FBBF24"/>
          <rect style="--dx:30px;--dy:-60px;--r:-140deg" x="57" y="56" width="5" height="5" rx="1" fill="#60A5FA"/>
          <rect style="--dx:-58px;--dy:-6px;--r:90deg" x="57" y="56" width="6" height="3.5" rx="1" fill="#A78BFA"/>
          <rect style="--dx:58px;--dy:-10px;--r:-90deg" x="57" y="56" width="6" height="3.5" rx="1" fill="#FBBF24"/>
          <circle style="--dx:-14px;--dy:-66px" cx="60" cy="58" r="2.6" fill="#06D6A0"/>
          <circle style="--dx:16px;--dy:-64px" cx="60" cy="58" r="2.6" fill="#F472B6"/>
          <circle style="--dx:-50px;--dy:-48px" cx="60" cy="58" r="2.2" fill="#60A5FA"/>
          <circle style="--dx:50px;--dy:-50px" cx="60" cy="58" r="2.2" fill="#A78BFA"/>
        </g>
        """;

    private static string LiveFx(string mood) => mood switch
    {
        "hello" => """<g class="hma-spark"><path d="M100 14 l2.4 5.2 5.2 2.4 -5.2 2.4 -2.4 5.2 -2.4 -5.2 -5.2 -2.4 5.2 -2.4z" fill="#FBBF24"/></g><circle class="hma-spark hma-spark2" cx="88" cy="12" r="2" fill="#FBBF24"/><circle class="hma-spark hma-spark3" cx="20" cy="20" r="1.8" fill="#A78BFA"/>""",
        "celebrate" => """<g class="hma-confetti"><rect x="14" y="0" width="5" height="3" rx="1" fill="#F472B6"/><rect x="34" y="0" width="4" height="4" rx="1" fill="#FBBF24"/><rect x="56" y="0" width="5" height="3" rx="1" fill="#06D6A0"/><rect x="78" y="0" width="4" height="4" rx="1" fill="#60A5FA"/><rect x="98" y="0" width="5" height="3" rx="1" fill="#A78BFA"/></g><g class="hma-confetti hma-confetti2"><rect x="24" y="0" width="4" height="4" rx="1" fill="#06D6A0"/><rect x="46" y="0" width="5" height="3" rx="1" fill="#F472B6"/><rect x="68" y="0" width="4" height="4" rx="1" fill="#FBBF24"/><rect x="90" y="0" width="5" height="3" rx="1" fill="#60A5FA"/></g>""",
        "think" => """<circle class="hma-bub hma-bub1" cx="92" cy="26" r="3" fill="#CBD5E1"/><circle class="hma-bub hma-bub2" cx="100" cy="17" r="4" fill="#CBD5E1"/><ellipse class="hma-bub hma-bub3" cx="110" cy="7" rx="7" ry="5" fill="#CBD5E1"/>""",
        "remind" => """<g class="hma-bell"><path d="M104 8 a6 6 0 0 1 6 6 v5 l2 3 h-16 l2 -3 v-5 a6 6 0 0 1 6 -6z" fill="#FBBF24"/><circle cx="104" cy="24.5" r="2.2" fill="#F59E0B"/></g>""",
        "sleep" => """<g fill="#94A3B8" font-family="Arial, sans-serif" font-weight="700"><text class="hma-z hma-z1" x="90" y="28" font-size="13">Z</text><text class="hma-z hma-z2" x="101" y="18" font-size="10">z</text><text class="hma-z hma-z3" x="109" y="10" font-size="8">z</text></g>""",
        _ => "",
    };

    /// <summary>
    /// Animowany hantelek (inline SVG ze stylami). Rodzic: <c>&lt;span class="hm-live st-idle md-happy"&gt;</c>, a wokół niego
    /// otoczka <c>&lt;span class="hm-fx" data-hm-motion="normal" data-hm-fx="look,tricks,…"&gt;</c> — tę obsługuje hantly-motion.js.
    /// </summary>
    public static string AnimatedSvg()
    {
        const string p = "hma-";
        var faces = string.Concat(Moods.Select(m => $"""<g class="hma-face-{m.Key}">{LiveFace(m.Key)}</g>"""));
        var fx = string.Concat(Moods.Select(m => $"""<g class="hma-fx-{m.Key}">{LiveFx(m.Key)}</g>"""));
        const string burst = """<g class="hma-burst"><circle cx="20" cy="40" r="2.6" fill="#FBBF24"/><circle cx="100" cy="36" r="2.6" fill="#06D6A0"/><circle cx="30" cy="16" r="2" fill="#F472B6"/><circle cx="90" cy="14" r="2" fill="#60A5FA"/><path d="M60 4 l1.6 3.4 3.4 1.6 -3.4 1.6 -1.6 3.4 -1.6 -3.4 -3.4 -1.6 3.4 -1.6z" fill="#FBBF24"/></g>""";
        return $"""<svg class="hma" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120" aria-hidden="true" focusable="false"><style>{LiveCss}</style>{Defs(p)}<g class="hma-back">{ReactBack}</g><g class="hma-shadow">{Shadow}</g><g class="hma-hover"><g class="hma-rig"><g class="hma-breath"><g class="hma-plate-l">{PlateLeft(p)}</g><g class="hma-plate-r">{PlateRight(p)}</g><g class="hma-core">{Core(p, glint: true)}<g class="hma-face">{faces}{ReactFace}</g>{ReactBody}</g></g></g></g><g class="hma-fx">{fx}{burst}{TrickFx}{ReactFront}</g></svg>""";
    }

    /// <summary>Style animacji (w SVG). Wszystko pod „.hm-live”, żeby nie dotykać reszty strony.</summary>
    public const string LiveCss = """
        .hm-live{display:inline-block;line-height:0}
        .hm-live .hma{width:100%;height:100%;overflow:visible;display:block}
        .hm-live .hma *{transform-box:view-box}
        .hm-live [class^="hma-face-"],.hm-live [class^="hma-fx-"],.hm-live .hma-burst{display:none}
        .hm-live.md-hello .hma-face-hello,.hm-live.md-happy .hma-face-happy,.hm-live.md-celebrate .hma-face-celebrate,.hm-live.md-think .hma-face-think,.hm-live.md-remind .hma-face-remind,.hm-live.md-sleep .hma-face-sleep{display:inline}
        .hm-live.md-hello .hma-fx-hello,.hm-live.md-celebrate .hma-fx-celebrate,.hm-live.md-think .hma-fx-think,.hm-live.md-remind .hma-fx-remind,.hm-live.md-sleep .hma-fx-sleep{display:inline}
        .hm-live.st-enter .hma-fx,.hm-live.st-land .hma-fx{opacity:0}
        .hm-live .hma-rig{transform-origin:60px 100px}
        .hm-live .hma-breath{transform-origin:60px 100px}
        .hm-live .hma-plate-l{transform-origin:30px 63px}
        .hm-live .hma-plate-r{transform-origin:90px 63px}
        .hm-live .hma-blink{transform-origin:60px 63px;animation:hma-blink 4.6s infinite}
        .hm-live .hma-glint{animation:hma-glint 5.5s ease-in-out infinite}
        .hm-live .hma-shadow{transform-origin:60px 102px}
        /* Wejście: trzy podskoki z prawej, potem lądowanie z przysiadem. */
        .hm-live.st-enter .hma-rig{animation:hma-hop-in 1.5s cubic-bezier(.3,.7,.4,1) both}
        .hm-live.st-enter .hma-shadow{animation:hma-shadow-in 1.5s cubic-bezier(.3,.7,.4,1) both}
        .hm-live.st-land .hma-rig{animation:hma-land .55s ease-out both}
        /* Spoczynek: oddech, talerze lekko pracują, mruganie, błysk na opasce. */
        .hm-live.st-idle .hma-breath{animation:hma-breathe 3.2s ease-in-out infinite}
        .hm-live.st-idle .hma-plate-l{animation:hma-plate-l 3.2s ease-in-out infinite}
        .hm-live.st-idle .hma-plate-r{animation:hma-plate-r 3.2s ease-in-out infinite}
        .hm-live.st-idle.md-hello .hma-rig{animation:hma-wave 3.8s ease-in-out infinite}
        .hm-live.st-idle.md-happy .hma-rig{animation:hma-bob 2.6s ease-in-out infinite}
        .hm-live.st-idle.md-celebrate .hma-rig{animation:hma-bounce 1.1s ease-in-out infinite}
        .hm-live.st-idle.md-think .hma-rig{animation:hma-tilt 5s ease-in-out infinite}
        .hm-live.st-idle.md-remind .hma-rig{animation:hma-nudge 3s ease-in-out infinite}
        .hm-live .hma-spark{transform-box:fill-box;transform-origin:center;animation:hma-twinkle 1.8s ease-in-out infinite}
        .hm-live .hma-spark2{animation-delay:.6s}.hm-live .hma-spark3{animation-delay:1.2s}
        .hm-live .hma-confetti{animation:hma-fall 2.4s linear infinite}
        .hm-live .hma-confetti2{animation-delay:1.2s}
        .hm-live .hma-bub{transform-box:fill-box;transform-origin:center;animation:hma-bub 2.4s ease-in-out infinite}
        .hm-live .hma-bub2{animation-delay:.3s}.hm-live .hma-bub3{animation-delay:.6s}
        .hm-live .hma-bell{transform-origin:104px 8px;animation:hma-ring 3s ease-in-out infinite}
        /* Radość: wyskok z saltem, talerze machają, wybuch iskierek. */
        .hm-live.st-excited .hma-rig{transform-origin:60px 63px;animation:hma-flip 1s cubic-bezier(.3,.6,.4,1) both}
        .hm-live.st-excited .hma-shadow{animation:hma-shadow-jump 1s ease-in-out both}
        .hm-live.st-excited .hma-plate-l{animation:hma-flap-l .25s ease-in-out 4 alternate}
        .hm-live.st-excited .hma-plate-r{animation:hma-flap-r .25s ease-in-out 4 alternate}
        .hm-live.st-excited .hma-burst{display:inline;transform-origin:60px 60px;animation:hma-burst .9s ease-out both}
        /* Sen: przechyla się i przysiada, oddycha wolno, lecą „zzz”. */
        .hm-live.st-sleep .hma-rig{animation:hma-doze .7s ease-out both}
        .hm-live.st-sleep .hma-breath{animation:hma-breathe 5s ease-in-out infinite .7s}
        .hm-live.st-sleep .hma-blink{animation:none}
        .hm-live .hma-z{transform-box:fill-box;animation:hma-z 3.6s ease-in infinite;opacity:0}
        .hm-live .hma-z2{animation-delay:1.2s}.hm-live .hma-z3{animation-delay:2.4s}
        @keyframes hma-blink{0%,93%,100%{transform:scaleY(1)}95%{transform:scaleY(.08)}97%{transform:scaleY(1)}}
        @keyframes hma-glint{0%,60%{transform:translateX(-14px);opacity:0}66%{opacity:.7}84%{transform:translateX(46px);opacity:0}100%{transform:translateX(46px);opacity:0}}
        @keyframes hma-hop-in{0%{transform:translate(150px,0)}12%{transform:translate(120px,-26px)}24%{transform:translate(95px,0) scale(1.06,.92)}38%{transform:translate(64px,-22px)}50%{transform:translate(40px,0) scale(1.05,.94)}64%{transform:translate(18px,-14px)}76%{transform:translate(0,0) scale(1.08,.9)}88%{transform:translate(0,0) scale(.97,1.04)}100%{transform:none}}
        @keyframes hma-shadow-in{0%{transform:translate(150px,0);opacity:0}20%{opacity:1}76%,100%{transform:none}}
        @keyframes hma-land{0%{transform:scale(1.1,.88)}45%{transform:scale(.95,1.06)}75%{transform:scale(1.02,.98)}100%{transform:none}}
        @keyframes hma-breathe{0%,100%{transform:scale(1,1)}50%{transform:scale(1.015,1.035)}}
        @keyframes hma-plate-l{0%,100%{transform:rotate(0)}50%{transform:rotate(-2.5deg)}}
        @keyframes hma-plate-r{0%,100%{transform:rotate(0)}50%{transform:rotate(2.5deg)}}
        @keyframes hma-wave{0%,55%,100%{transform:rotate(0)}62%{transform:rotate(-7deg)}70%{transform:rotate(6deg)}78%{transform:rotate(-5deg)}86%{transform:rotate(3deg)}}
        @keyframes hma-bob{0%,100%{transform:translateY(0)}50%{transform:translateY(-3px)}}
        @keyframes hma-bounce{0%,100%{transform:translateY(0) scale(1,1)}15%{transform:scale(1.05,.94)}45%{transform:translateY(-9px) scale(.97,1.03)}75%{transform:translateY(0) scale(1.03,.97)}}
        @keyframes hma-tilt{0%,100%{transform:rotate(0)}30%,60%{transform:rotate(-6deg)}}
        @keyframes hma-nudge{0%,70%,100%{transform:translateX(0)}74%{transform:translateX(-3px)}78%{transform:translateX(3px)}82%{transform:translateX(-2px)}86%{transform:translateX(2px)}}
        @keyframes hma-twinkle{0%,100%{transform:scale(.6);opacity:.4}50%{transform:scale(1.15);opacity:1}}
        @keyframes hma-fall{0%{transform:translateY(-6px);opacity:0}10%{opacity:1}90%{opacity:1}100%{transform:translateY(46px);opacity:0}}
        @keyframes hma-bub{0%,100%{transform:scale(.8);opacity:.5}50%{transform:scale(1.1);opacity:1}}
        @keyframes hma-ring{0%,60%,100%{transform:rotate(0)}66%{transform:rotate(16deg)}72%{transform:rotate(-14deg)}78%{transform:rotate(10deg)}84%{transform:rotate(-6deg)}}
        @keyframes hma-flip{0%{transform:scale(1.08,.9)}20%{transform:translateY(-30px) rotate(0) scale(.96,1.05)}55%{transform:translateY(-36px) rotate(360deg)}80%{transform:translateY(0) rotate(360deg) scale(1.08,.9)}100%{transform:rotate(360deg)}}
        @keyframes hma-shadow-jump{0%,100%{transform:scale(1);opacity:1}45%{transform:scale(.6);opacity:.5}}
        @keyframes hma-flap-l{from{transform:rotate(0)}to{transform:rotate(-14deg)}}
        @keyframes hma-flap-r{from{transform:rotate(0)}to{transform:rotate(14deg)}}
        @keyframes hma-burst{0%{transform:scale(.3);opacity:0}30%{opacity:1}100%{transform:scale(1.25);opacity:0}}
        @keyframes hma-doze{0%{transform:none}100%{transform:translateY(4px) rotate(-10deg)}}
        @keyframes hma-z{0%{transform:translate(0,4px);opacity:0}25%{opacity:1}100%{transform:translate(8px,-12px);opacity:0}}
        .hm-live.st-sleep .hma-rig{transform:translateY(4px) rotate(-10deg)}
        /* ── Ruch sterowany z hantly-motion.js: klasy na otoczce .hm-fx (Blazor jej nie nadpisuje) ── */
        .hm-fx{display:inline-block;line-height:0;--hm-lx:0;--hm-ly:0}
        /* Patrzy za kursorem: twarz przesuwa się w stronę kursora, błyski w oczach jeszcze dalej, a całe ciało lekko się wychyla. */
        .hm-live .hma-face{transform:translate(calc(var(--hm-lx) * 6px),calc(var(--hm-ly) * 4px));transition:transform .3s cubic-bezier(.3,.7,.4,1)}
        .hm-live .hma-pupil{transform:translate(calc(var(--hm-lx) * 1.6px),calc(var(--hm-ly) * 1.4px));transition:transform .3s ease-out}
        .hm-live .hma-mouth{transform-origin:60px 75px}
        .hm-live .hma-eyes{transform-origin:60px 63px}
        .hm-live .hma-hover{transform-origin:60px 100px;rotate:calc(var(--hm-lx) * 8deg);transition:transform .3s cubic-bezier(.3,1.6,.5,1),rotate .45s cubic-bezier(.3,.7,.4,1)}
        .hm-live .hma-yawn,.hm-live .hma-heart,.hm-live .hma-plus,.hm-live .hma-party{opacity:0}
        .hm-live .hma-heart{transform-origin:60px 22px}
        .hm-live .hma-party > *{transform-box:fill-box;transform-origin:center}
        /* Najechanie: prostuje się i patrzy na Ciebie. */
        .hm-fx.is-hover .hm-live .hma-hover{transform:translateY(-6px) scale(1.1)}
        .hm-fx.is-hover .hm-live .hma-face{transform:translate(calc(var(--hm-lx) * 6px),calc(var(--hm-ly) * 4px)) scale(1.06)}
        /* Sztuczki w spoczynku */
        .hm-fx.tr-wiggle .hm-live .hma-rig{animation:hmt-wiggle .9s ease-in-out both}
        .hm-fx.tr-hop .hm-live .hma-rig{animation:hmt-hop .95s cubic-bezier(.3,.7,.4,1) both}
        .hm-fx.tr-hop .hm-live .hma-shadow{animation:hmt-shadow-hop .95s ease-in-out both}
        .hm-fx.tr-spin .hm-live .hma-rig{animation:hmt-spin .9s ease-in-out both}
        .hm-fx.tr-curl .hm-live .hma-rig{animation:hmt-curl 1.5s ease-in-out both}
        .hm-fx.tr-curl .hm-live .hma-plate-l{animation:hma-flap-l .3s ease-in-out 4 alternate}
        .hm-fx.tr-curl .hm-live .hma-plate-r{animation:hma-flap-r .3s ease-in-out 4 alternate}
        .hm-fx.tr-curl .hm-live .hma-plus{animation:hmt-plus 1.5s ease-out both}
        .hm-fx.tr-look .hm-live .hma-face{animation:hmt-look 1.7s ease-in-out both}
        .hm-fx.tr-stretch .hm-live .hma-rig{animation:hmt-stretch 1.2s ease-in-out both}
        .hm-fx.tr-heart .hm-live .hma-heart{animation:hmt-heart 1.3s ease-out both}
        .hm-fx.tr-talk .hm-live .hma-mouth{animation:hmt-talk .16s ease-in-out infinite alternate}
        .hm-fx.tr-yawn .hm-live .hma-mouth{opacity:0}
        .hm-fx.tr-yawn .hm-live .hma-yawn{animation:hmt-yawn 1.8s ease-in-out both}
        .hm-fx.tr-yawn .hm-live .hma-eyes{animation:hmt-squint 1.8s ease-in-out both}
        .hm-fx.tr-yawn .hm-live .hma-rig{animation:hmt-stretch 1.8s ease-in-out both}
        .hm-fx.tr-party .hm-live .hma-party{opacity:1}
        .hm-fx.tr-party .hm-live .hma-party > *{animation:hmt-confetti 1.3s cubic-bezier(.2,.7,.4,1) both}
        .hm-fx.tr-party .hm-live .hma-rig{animation:hmt-hop .95s cubic-bezier(.3,.7,.4,1) both}
        /* Reakcje na wydarzenia (grają raz, gdy pokazuje się podpowiedź danego rodzaju) */
        .hm-live .hma-stonks,.hm-live .hma-sun,.hm-live .hma-shades,.hm-live .hma-sweat,.hm-live .hma-trophy,.hm-live .hma-coins,.hm-live .hma-cal,.hm-live .hma-lens,.hm-live .hma-chart,.hm-live .hma-tag,.hm-live .hma-bigbell{opacity:0}
        .hm-live .hma-coin,.hm-live .hma-bar,.hm-live .hma-trophy,.hm-live .hma-cal,.hm-live .hma-tag,.hm-live .hma-bigbell,.hm-live .hma-chart{transform-box:fill-box;transform-origin:center}
        .hm-live .hma-bar{transform-origin:center bottom}
        .hm-live .hma-tag{transform-origin:top center}
        .hm-live .hma-bigbell{transform-origin:top center}
        .hm-live .hma-stonks-line{stroke-dasharray:100;stroke-dashoffset:100}
        .hm-live .hma-cal-check{stroke-dasharray:100;stroke-dashoffset:100}
        .hm-fx.tr-stonks .hm-live .hma-stonks{opacity:1}
        .hm-fx.tr-stonks .hm-live .hma-stonks-line{animation:hmr-draw 1.1s cubic-bezier(.5,0,.3,1) .1s both}
        .hm-fx.tr-stonks .hm-live .hma-stonks-head,.hm-fx.tr-stonks .hm-live .hma-stonks-txt{animation:hmr-pop 2.6s ease-out both}
        .hm-fx.tr-stonks .hm-live .hma-shades{animation:hmr-shades 2.8s cubic-bezier(.3,1.4,.5,1) both}
        .hm-fx.tr-stonks .hm-live .hma-rig{animation:hmr-proud 2.8s ease-in-out both}
        .hm-fx.tr-stonks .hm-live .hma-stonks{animation:hmr-fade 2.8s ease-in-out both}
        .hm-fx.tr-trophy .hm-live .hma-trophy{animation:hmr-trophy 2s cubic-bezier(.3,1.5,.5,1) both}
        .hm-fx.tr-trophy .hm-live .hma-rig{animation:hmt-hop .95s cubic-bezier(.3,.7,.4,1) .15s both}
        .hm-fx.tr-coins .hm-live .hma-coins{opacity:1}
        .hm-fx.tr-coins .hm-live .hma-coin{animation:hmr-coin 1.9s cubic-bezier(.5,0,.6,1.3) both}
        .hm-fx.tr-coins .hm-live .hma-coin2{animation-delay:.25s}.hm-fx.tr-coins .hm-live .hma-coin3{animation-delay:.5s}
        .hm-fx.tr-coins .hm-live .hma-rig{animation:hmr-nod 1.9s ease-in-out both}
        .hm-fx.tr-calendar .hm-live .hma-cal{animation:hmr-popin 1.9s cubic-bezier(.3,1.5,.5,1) both}
        .hm-fx.tr-calendar .hm-live .hma-cal-check{animation:hmr-draw .5s ease-out .55s both}
        .hm-fx.tr-calendar .hm-live .hma-face{animation:hmr-glance-up 1.9s ease-in-out both}
        .hm-fx.tr-sweat .hm-live .hma-sweat{animation:hmr-sweat 1.9s ease-in both}
        .hm-fx.tr-sweat .hm-live .hma-rig{animation:hmt-wiggle .9s ease-in-out .9s both}
        .hm-fx.tr-magnifier .hm-live .hma-lens{animation:hmr-lens 2.2s ease-in-out both}
        .hm-fx.tr-magnifier .hm-live .hma-face{animation:hmt-look 2.2s ease-in-out both}
        .hm-fx.tr-sun .hm-live .hma-sun{animation:hmr-sunrise 2.4s ease-out both}
        .hm-fx.tr-sun .hm-live .hma-sun-rays{transform-box:fill-box;transform-origin:center;animation:hmr-spin 2.4s linear both}
        .hm-fx.tr-sun .hm-live .hma-rig{animation:hmt-stretch 1.4s ease-in-out .5s both}
        .hm-fx.tr-chart .hm-live .hma-chart{animation:hmr-popin 2s cubic-bezier(.3,1.5,.5,1) both}
        .hm-fx.tr-chart .hm-live .hma-bar{animation:hmr-grow .6s cubic-bezier(.3,1.4,.5,1) both}
        .hm-fx.tr-chart .hm-live .hma-bar1{animation-delay:.3s}.hm-fx.tr-chart .hm-live .hma-bar2{animation-delay:.45s}.hm-fx.tr-chart .hm-live .hma-bar3{animation-delay:.6s}
        .hm-fx.tr-tag .hm-live .hma-tag{animation:hmr-swing 2s ease-in-out both}
        .hm-fx.tr-tag .hm-live .hma-face{animation:hmr-glance-up 2s ease-in-out both}
        .hm-fx.tr-ring .hm-live .hma-bigbell{animation:hmr-ring 1.8s ease-in-out both}
        .hm-fx.tr-ring .hm-live .hma-rig{animation:hma-nudge 1.8s ease-in-out both}
        .hm-fx.tr-warmup .hm-live .hma-rig{animation:hmr-squat 1.8s ease-in-out both}
        .hm-fx.tr-warmup .hm-live .hma-shadow{animation:hmr-squat-shadow 1.8s ease-in-out both}
        @keyframes hmr-draw{to{stroke-dashoffset:0}}
        @keyframes hmr-fade{0%,85%{opacity:1}100%{opacity:0}}
        @keyframes hmr-pop{0%,40%{opacity:0;transform:translateY(4px)}52%,85%{opacity:1;transform:none}100%{opacity:0}}
        @keyframes hmr-shades{0%{transform:translateY(-34px);opacity:0}25%{opacity:1}40%,85%{transform:none;opacity:1}100%{transform:none;opacity:0}}
        @keyframes hmr-proud{0%,100%{transform:none}30%,85%{transform:translateY(-3px) rotate(-5deg)}}
        @keyframes hmr-trophy{0%{transform:translateY(14px) scale(.2);opacity:0}25%{transform:translateY(-4px) scale(1.15);opacity:1}40%,80%{transform:translateY(0) scale(1);opacity:1}100%{transform:translateY(-8px) scale(.9);opacity:0}}
        @keyframes hmr-coin{0%{transform:translateY(-26px);opacity:0}15%{opacity:1}45%{transform:translateY(8px)}55%{transform:translateY(4px)}65%,85%{transform:translateY(8px);opacity:1}100%{transform:translateY(8px);opacity:0}}
        @keyframes hmr-nod{0%,100%{transform:none}30%,60%{transform:rotate(4deg) translateY(1px)}45%,75%{transform:rotate(-2deg)}}
        @keyframes hmr-popin{0%{transform:scale(.2);opacity:0}25%{transform:scale(1.12);opacity:1}40%,82%{transform:scale(1);opacity:1}100%{transform:scale(.9);opacity:0}}
        @keyframes hmr-glance-up{0%,100%{transform:none}25%,80%{transform:translate(4px,-3px)}}
        @keyframes hmr-sweat{0%{transform:translateY(-2px);opacity:0}20%{opacity:1}80%{transform:translateY(16px);opacity:.9}100%{transform:translateY(22px);opacity:0}}
        @keyframes hmr-lens{0%{transform:translate(8px,56px) rotate(-20deg);opacity:0}15%{opacity:1}50%{transform:translate(104px,52px) rotate(10deg)}80%{transform:translate(30px,58px) rotate(-10deg);opacity:1}100%{transform:translate(30px,58px);opacity:0}}
        @keyframes hmr-sunrise{0%{transform:translateY(44px);opacity:0}30%{opacity:1}55%,85%{transform:translateY(0);opacity:1}100%{opacity:0}}
        @keyframes hmr-spin{to{transform:rotate(90deg)}}
        @keyframes hmr-grow{from{transform:scaleY(0)}to{transform:scaleY(1)}}
        @keyframes hmr-swing{0%{transform:translateY(-14px) rotate(0);opacity:0}15%{transform:translateY(0) rotate(18deg);opacity:1}35%{transform:rotate(-14deg)}55%{transform:rotate(9deg)}75%,88%{transform:rotate(0);opacity:1}100%{opacity:0}}
        @keyframes hmr-ring{0%{opacity:0;transform:scale(.4)}12%{opacity:1;transform:scale(1.1)}20%{transform:rotate(18deg)}30%{transform:rotate(-16deg)}40%{transform:rotate(12deg)}50%{transform:rotate(-8deg)}60%,85%{transform:rotate(0);opacity:1}100%{opacity:0}}
        @keyframes hmr-squat{0%,100%{transform:none}15%,55%{transform:scale(1.08,.86) translateY(4px)}30%,70%{transform:translateY(-10px) scale(.96,1.05)}42%,82%{transform:none}}
        @keyframes hmr-squat-shadow{0%,42%,82%,100%{transform:scale(1)}30%,70%{transform:scale(.75)}}
        /* Drzemka po bezczynności: ta sama minka co przy wyciszeniu. */
        .hm-fx.is-dozing .hm-live [class^="hma-face-"],.hm-fx.is-dozing .hm-live [class^="hma-fx-"]{display:none}
        .hm-fx.is-dozing .hm-live .hma-face-sleep,.hm-fx.is-dozing .hm-live .hma-fx-sleep{display:inline}
        .hm-fx.is-dozing .hm-live .hma-rig{animation:hma-doze .7s ease-out both}
        .hm-fx.is-dozing .hm-live .hma-breath{animation:hma-breathe 5s ease-in-out infinite .7s}
        /* „Bez ruchu” (ustawienie trenera albo Portalu): maskotka stoi spokojnie. */
        .hm-fx[data-hm-motion="off"] .hm-live *{animation:none!important;transition:none!important}
        .hm-fx[data-hm-motion="off"] .hm-live .hma-fx{opacity:1}
        @keyframes hmt-wiggle{0%,100%{transform:rotate(0)}15%{transform:rotate(-9deg)}35%{transform:rotate(8deg)}55%{transform:rotate(-6deg)}75%{transform:rotate(4deg)}}
        @keyframes hmt-hop{0%{transform:none}12%{transform:scale(1.08,.9)}30%{transform:translateY(-16px) scale(.96,1.05)}48%{transform:translateY(0) scale(1.06,.93)}62%{transform:translateY(-8px)}80%{transform:translateY(0) scale(1.04,.96)}100%{transform:none}}
        @keyframes hmt-shadow-hop{0%,48%,80%,100%{transform:scale(1);opacity:1}30%{transform:scale(.7);opacity:.6}62%{transform:scale(.85)}}
        @keyframes hmt-spin{0%{transform:scaleX(1)}25%{transform:scaleX(.1) translateY(-4px)}50%{transform:scaleX(-1) translateY(-6px)}75%{transform:scaleX(-.1) translateY(-4px)}100%{transform:scaleX(1)}}
        @keyframes hmt-curl{0%,100%{transform:none}20%{transform:translateY(-14px) rotate(-4deg)}40%{transform:translateY(0) scale(1.04,.96)}60%{transform:translateY(-14px) rotate(4deg)}80%{transform:translateY(0) scale(1.04,.96)}}
        @keyframes hmt-plus{0%,15%{transform:translateY(6px);opacity:0}30%{opacity:1}100%{transform:translateY(-16px);opacity:0}}
        @keyframes hmt-look{0%,100%{transform:translate(0,0)}20%,40%{transform:translate(-3.5px,-.5px)}55%,80%{transform:translate(3.5px,-.5px)}}
        @keyframes hmt-stretch{0%,100%{transform:none}35%,60%{transform:scale(.94,1.12)}80%{transform:scale(1.04,.96)}}
        @keyframes hmt-heart{0%{transform:scale(.3) translateY(10px);opacity:0}25%{transform:scale(1.15);opacity:1}100%{transform:scale(1) translateY(-14px);opacity:0}}
        @keyframes hmt-talk{from{transform:scaleY(1)}to{transform:scaleY(1.45) translateY(-.5px)}}
        @keyframes hmt-yawn{0%,100%{transform:scale(.3);opacity:0}20%{opacity:1}35%,70%{transform:scale(1,1.15);opacity:1}90%{transform:scale(.6);opacity:.6}}
        @keyframes hmt-squint{0%,100%{transform:scaleY(1)}30%,75%{transform:scaleY(.3)}}
        @keyframes hmt-confetti{0%{transform:translate(0,0) rotate(0) scale(.4);opacity:0}15%{opacity:1}100%{transform:translate(var(--dx),var(--dy)) rotate(var(--r,0deg)) scale(1);opacity:0}}
        @media (prefers-reduced-motion:reduce){.hm-live *{animation:none!important}.hm-live.st-enter .hma-fx,.hm-live.st-land .hma-fx{opacity:1}}
        """;
}
