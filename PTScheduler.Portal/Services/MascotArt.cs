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

    // ── Wersja animowana ─────────────────────────────────────────────
    // Jeden inline SVG ze wszystkimi minami i efektami; co widać i jak się rusza, ustawiają klasy rodzica:
    // „hm-live st-{scena} md-{nastrój}”. Style są w środku SVG, więc ten sam plik działa w aplikacji trenera,
    // w podglądzie Panelu i na stronie głównej Portalu.

    /// <summary>Sceny animacji: wejście (podskokami z boku), lądowanie, spoczynek, radość (salto), sen.</summary>
    public static readonly string[] Stages = ["enter", "land", "idle", "excited", "sleep"];

    private static string BlinkEyes(double dx = 0, double dy = 0) => $"""<g class="hma-blink">{OpenEyes(dx, dy)}</g>""";

    private static string LiveFace(string mood) => mood switch
    {
        "hello" => BlinkEyes() + Cheeks + BigSmile,
        "think" => BlinkEyes(1.6, -1.6) + """<path d="M54 75 h12" stroke="#1E1B4B" stroke-width="3" stroke-linecap="round"/>""",
        "remind" => BlinkEyes() + Cheeks + """<ellipse cx="60" cy="75" rx="3.4" ry="3.8" fill="#1E1B4B"/>""",
        _ => Face(mood),
    };

    private static string LiveFx(string mood) => mood switch
    {
        "hello" => """<g class="hma-spark"><path d="M100 14 l2.4 5.2 5.2 2.4 -5.2 2.4 -2.4 5.2 -2.4 -5.2 -5.2 -2.4 5.2 -2.4z" fill="#FBBF24"/></g><circle class="hma-spark hma-spark2" cx="88" cy="12" r="2" fill="#FBBF24"/><circle class="hma-spark hma-spark3" cx="20" cy="20" r="1.8" fill="#A78BFA"/>""",
        "celebrate" => """<g class="hma-confetti"><rect x="14" y="0" width="5" height="3" rx="1" fill="#F472B6"/><rect x="34" y="0" width="4" height="4" rx="1" fill="#FBBF24"/><rect x="56" y="0" width="5" height="3" rx="1" fill="#06D6A0"/><rect x="78" y="0" width="4" height="4" rx="1" fill="#60A5FA"/><rect x="98" y="0" width="5" height="3" rx="1" fill="#A78BFA"/></g><g class="hma-confetti hma-confetti2"><rect x="24" y="0" width="4" height="4" rx="1" fill="#06D6A0"/><rect x="46" y="0" width="5" height="3" rx="1" fill="#F472B6"/><rect x="68" y="0" width="4" height="4" rx="1" fill="#FBBF24"/><rect x="90" y="0" width="5" height="3" rx="1" fill="#60A5FA"/></g>""",
        "think" => """<circle class="hma-bub hma-bub1" cx="92" cy="26" r="3" fill="#CBD5E1"/><circle class="hma-bub hma-bub2" cx="100" cy="17" r="4" fill="#CBD5E1"/><ellipse class="hma-bub hma-bub3" cx="110" cy="7" rx="7" ry="5" fill="#CBD5E1"/>""",
        "remind" => """<g class="hma-bell"><path d="M104 8 a6 6 0 0 1 6 6 v5 l2 3 h-16 l2 -3 v-5 a6 6 0 0 1 6 -6z" fill="#FBBF24"/><circle cx="104" cy="24.5" r="2.2" fill="#F59E0B"/></g>""",
        "sleep" => """<g fill="#94A3B8" font-family="Arial, sans-serif" font-weight="700"><text class="hma-z hma-z1" x="90" y="28" font-size="13">Z</text><text class="hma-z hma-z2" x="101" y="18" font-size="10">z</text><text class="hma-z hma-z3" x="109" y="10" font-size="8">z</text></g>""",
        _ => "",
    };

    /// <summary>Animowany hantelek (inline SVG ze stylami). Rodzic: <c>&lt;span class="hm-live st-idle md-happy"&gt;</c>.</summary>
    public static string AnimatedSvg()
    {
        const string p = "hma-";
        var faces = string.Concat(Moods.Select(m => $"""<g class="hma-face-{m.Key}">{LiveFace(m.Key)}</g>"""));
        var fx = string.Concat(Moods.Select(m => $"""<g class="hma-fx-{m.Key}">{LiveFx(m.Key)}</g>"""));
        const string burst = """<g class="hma-burst"><circle cx="20" cy="40" r="2.6" fill="#FBBF24"/><circle cx="100" cy="36" r="2.6" fill="#06D6A0"/><circle cx="30" cy="16" r="2" fill="#F472B6"/><circle cx="90" cy="14" r="2" fill="#60A5FA"/><path d="M60 4 l1.6 3.4 3.4 1.6 -3.4 1.6 -1.6 3.4 -1.6 -3.4 -3.4 -1.6 3.4 -1.6z" fill="#FBBF24"/></g>""";
        return $"""<svg class="hma" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120" aria-hidden="true" focusable="false"><style>{LiveCss}</style>{Defs(p)}<g class="hma-shadow">{Shadow}</g><g class="hma-rig"><g class="hma-breath"><g class="hma-plate-l">{PlateLeft(p)}</g><g class="hma-plate-r">{PlateRight(p)}</g><g class="hma-core">{Core(p, glint: true)}<g class="hma-face">{faces}</g></g></g></g><g class="hma-fx">{fx}{burst}</g></svg>""";
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
        @media (prefers-reduced-motion:reduce){.hm-live *{animation:none!important}.hm-live.st-enter .hma-fx,.hm-live.st-land .hma-fx{opacity:1}}
        """;
}
