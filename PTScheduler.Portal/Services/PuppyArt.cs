using System.Text;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Agent Portalu — szczeniak golden retrievera (domyślnie „Leo”), rysowany w SVG z części: tułów z kremową kryzą,
/// opadające uszy z falowanym futrem, dłuższa kufa, kita jak pióropusz, fioletowa obroża ze złotym znaczkiem.
/// Statyczne obrazki (ikony w Panelu, PNG do e-maili) mają sześć nastrojów; wersja animowana (dymek w Panelu)
/// zawiera trzy pozy — idzie bokiem, siedzi, leży — i wszystkie miny naraz, a pokazuje je CSS
/// (klasy .leo-stage-* i .leo-mood-* na elemencie nad SVG). W Panelu każdą ikonę można podmienić własnym plikiem.
/// </summary>
public static class PuppyArt
{
    /// <summary>Nastroje Leo — klucze jak w <see cref="MascotArt"/>, opisy pod pracę administratora.</summary>
    public static readonly MascotArt.Mood[] Moods =
    [
        new("hello", "Wita się", "poranny raport, powitanie w Panelu — macha łapą"),
        new("happy", "Zadowolony", "dobre wiadomości, wszystko w porządku"),
        new("celebrate", "Świętuje", "nowa rejestracja, pierwsza płatność, aktywacja"),
        new("think", "Myśli", "wnioski, analizy, sugestie — przechyla głowę"),
        new("remind", "Czujny", "zaległości, awarie, zgłoszenia, ryzyko odejścia — uszy w górę"),
        new("sleep", "Śpi", "wyciszony agent — leży z głową na łapach"),
    ];

    private static readonly string[] MoodKeys = ["hello", "happy", "celebrate", "think", "remind", "sleep"];

    // Paleta golden retrievera.
    private const string Ink = "#2B1C14";
    private const string Brow = "#C4843A";
    private const string Fur = "#E2A04F";
    private const string FurLight = "#EEBB6E";
    private const string FurShade = "#CC8A3E";
    private const string Cream = "#F7DFB0";
    private const string Tongue = "#F07A8E";

    /// <summary>Statyczny obrazek nastroju (nieznany klucz = „happy”).</summary>
    public static string Svg(string mood)
    {
        if (!MoodKeys.Contains(mood)) mood = "happy";
        const string p = "l";
        var body = mood == "sleep" ? Lie(p, animated: false) : Sit(p, mood);
        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120">{Defs(p)}{body}{Fx(mood)}</svg>""";
    }

    public static string DataUri(string mood) =>
        "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(Svg(mood)));

    /// <summary>
    /// Animowany Leo do osadzenia w stronie: wszystkie pozy, miny i efekty w jednym SVG. Co widać i jak się rusza,
    /// ustawiają klasy rodzica: leo-stage-walk | sit | idle | excited | sleep oraz leo-mood-{nastrój} (panel.css).
    /// </summary>
    /// <param name="p">Przedrostek id — drugi Leo na tej samej stronie (karta na Pulpicie) dostaje własny, żeby id się nie dublowały.</param>
    public static string AnimatedSvg(string p = "leoa")
    {
        var fx = string.Concat(MoodKeys.Select(m => $"""<g class="leo-fx-{m}">{Fx(m)}</g>"""));
        return $"""<svg class="leo-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 120 120" width="120" height="120" aria-hidden="true" focusable="false">{Defs(p)}<g class="leo-pose leo-pose-walk">{Walk(p)}</g><g class="leo-pose leo-pose-sit">{Sit(p, null)}</g><g class="leo-pose leo-pose-lie">{Lie(p, animated: true)}</g><g class="leo-fx">{fx}</g></svg>""";
    }

    private static string Defs(string p) => $"""
        <defs>
          <linearGradient id="{p}-coat" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F0BE72"/><stop offset="1" stop-color="#D6923F"/></linearGradient>
          <radialGradient id="{p}-head" cx=".5" cy=".32" r=".75"><stop offset="0" stop-color="#F6CF8C"/><stop offset=".65" stop-color="#E9AF5E"/><stop offset="1" stop-color="#D6943F"/></radialGradient>
          <linearGradient id="{p}-ear" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#D99545"/><stop offset="1" stop-color="#B9772F"/></linearGradient>
          <linearGradient id="{p}-cream" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FBE5BA"/><stop offset="1" stop-color="#EDC686"/></linearGradient>
          <linearGradient id="{p}-muzzle" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#F9DEAB"/><stop offset="1" stop-color="#EBC27F"/></linearGradient>
        </defs>
        """;

    // ── Siedzi (przodem) ──────────────────────────────────────────────

    /// <param name="mood">Nastrój statycznego obrazka; null = wersja animowana ze wszystkimi minami.</param>
    private static string Sit(string p, string? mood)
    {
        var animated = mood is null;
        var sb = new StringBuilder();
        sb.Append("""<ellipse cx="60" cy="110.5" rx="36" ry="4.2" fill="#0F172A" opacity=".13"/>""");
        sb.Append($"""<g class="leo-tail">{TailSit(p)}</g>""");
        // Uda i tułów.
        sb.Append($"""<path d="M37 84 C26 86 21.5 96 24.5 104 C27.5 109.5 38 109.5 45 106.5 L43 88 Z" fill="{FurShade}"/>""");
        sb.Append($"""<path d="M83 84 C94 86 98.5 96 95.5 104 C92.5 109.5 82 109.5 75 106.5 L77 88 Z" fill="{FurShade}"/>""");
        sb.Append($"""<path d="M45 60 C38 70 33 84 32 99 C32 106 40 108.5 60 108.5 C80 108.5 88 106 88 99 C87 84 82 70 75 60 Z" fill="url(#{p}-coat)"/>""");
        var pawUp = $"""<path d="M44.5 82 C44 74 44.2 68.5 45 64.5 L54.5 64.5 C55.2 68.5 55.4 74 55 82 Z" fill="{FurLight}"/><ellipse cx="49.8" cy="63.2" rx="7.4" ry="5.2" fill="#F3CE8E"/><path d="M47.2 60.6 v3 M49.8 60.2 v3.4 M52.4 60.6 v3" stroke="#D9A35A" stroke-width="1" stroke-linecap="round"/>""";
        if (animated) sb.Append($"""<g class="leo-paw-up">{pawUp}</g>""");
        else if (mood == "hello") sb.Append($"""<g transform="rotate(-38 49.5 84)">{pawUp}</g>""");
        sb.Append($"""<g class="leo-chest"><path d="M47 60 C43 70 42.5 81 45.5 92 L48.5 88.5 L51.5 95 L55 89.5 L60 97.5 L65 89.5 L68.5 95 L71.5 88.5 L74.5 92 C77.5 81 77 70 73 60 Z" fill="url(#{p}-cream)"/>""");
        sb.Append("""<path d="M53.5 70 q2.2 6 0 12.5 M66.5 70 q-2.2 6 0 12.5 M60 74 v10" stroke="#E6BD7E" stroke-width="1.2" fill="none" stroke-linecap="round"/></g>""");
        // Przednie łapy: lewa stoi albo macha (hello), prawa zawsze stoi.
        var legL = Leg(44);
        if (animated) sb.Append($"""<g class="leo-leg-l">{legL}</g>""");
        else if (mood != "hello") sb.Append(legL);
        sb.Append(Leg(65));
        sb.Append(Collar(60, 61));
        // Głowa (w wersji animowanej przechyla ją CSS — przy „myśli”).
        var tilt = mood == "think" ? """ transform="rotate(-9 60 50)" """ : "";
        sb.Append($"""<g class="leo-head"{tilt}>{Head(p, mood)}</g>""");
        return sb.ToString();
    }

    private static string TailSit(string p) =>
        $"""<path d="M84 102 C96 103 106.5 96 108.5 84 C109.5 76 106.5 68.5 101 65.5 C103.5 72 102.5 78 99.5 82 C101.5 82 102.8 80.8 103.8 78.8 C101.8 87 96.5 91.5 90.5 93.2 C92.5 94.2 94.5 94.2 96.5 93.2 C92.5 97.2 88.5 99 84 99.2 Z" fill="url(#{p}-coat)"/><path d="M101 68 C104 74 103.5 80 100.5 84" stroke="{Cream}" stroke-width="1.4" fill="none" opacity=".7" stroke-linecap="round"/>""";

    private static string Leg(double x) =>
        $"""<path d="M{x} 80 C{x - .5} 90 {x - .5} 98 {x} 104 L{x + 11} 104 C{x + 11.5} 98 {x + 11.5} 90 {x + 11} 80 Z" fill="{FurLight}"/><path d="M{x + 1.5} 84 C{x + 1} 92 {x + 1.2} 98 {x + 1.6} 102" stroke="{FurShade}" stroke-width="1" fill="none" opacity=".5"/><ellipse cx="{x + 5.5}" cy="105.6" rx="7.8" ry="4.2" fill="#F3CE8E"/><path d="M{x + 3} 103.6 v3 M{x + 5.5} 103.2 v3.4 M{x + 8} 103.6 v3" stroke="#D9A35A" stroke-width="1" stroke-linecap="round"/>""";

    private static string Collar(double cx, double y) =>
        $"""<path d="M{cx - 15.5} {y} Q{cx} {y + 9} {cx + 15.5} {y} L{cx + 16} {y + 5.5} Q{cx} {y + 15} {cx - 16} {y + 5.5} Z" fill="#7C3AED"/><path d="M{cx - 15} {y + 1.6} Q{cx} {y + 10.4} {cx + 15} {y + 1.6}" stroke="#fff" stroke-opacity=".3" stroke-width="1.2" fill="none"/><circle cx="{cx}" cy="{y + 13.6}" r="3.9" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.2"/><circle cx="{cx - 1.1}" cy="{y + 12.5}" r="1" fill="#fff" opacity=".6"/>""";

    private static string Head(string p, string? mood)
    {
        var sb = new StringBuilder();
        // Czaszka z kosmykiem na czubku i policzkami.
        sb.Append($"""<path d="M60 13 C75 13 85 22 86 36 C86.5 46 83 53 77 58 L43 58 C37 53 33.5 46 34 36 C35 22 45 13 60 13 Z" fill="url(#{p}-head)"/>""");
        sb.Append($"""<path d="M53.5 15.5 C54.5 10.5 58 9.8 60 13 C62 9.8 65.5 10.5 66.5 15.5 Z" fill="{FurLight}"/>""");
        sb.Append($"""<path d="M37 46 C38 53.5 42.5 58.5 48.5 60 L45 51 Z" fill="{FurLight}"/><path d="M83 46 C82 53.5 77.5 58.5 71.5 60 L75 51 Z" fill="{FurLight}"/>""");
        sb.Append("""<path d="M60 21 C59.2 27 59.2 32 60 37" stroke="#D39446" stroke-width="1.2" fill="none" opacity=".45" stroke-linecap="round"/>""");
        // Uszy — opadające, z falowanym brzegiem; w animacji ruszają się przy ruchu i czujności.
        sb.Append($"""<g class="leo-ear-l">{Ear(p, left: true)}</g><g class="leo-ear-r">{Ear(p, left: false)}</g>""");
        // Kufa i nos.
        sb.Append($"""<path d="M47.5 44 C48 39.5 53 37.5 60 37.5 C67 37.5 72 39.5 72.5 44 L73.5 54.5 C73.5 61.5 67.5 65.5 60 65.5 C52.5 65.5 46.5 61.5 46.5 54.5 Z" fill="url(#{p}-muzzle)"/>""");
        sb.Append("""<path d="M55.5 40.2 Q60 38.6 64.5 40.2" stroke="#fff" stroke-opacity=".4" stroke-width="1.4" fill="none" stroke-linecap="round"/>""");
        sb.Append("""<g fill="#C9934E" opacity=".55"><circle cx="51.5" cy="52.5" r=".7"/><circle cx="50.3" cy="55" r=".7"/><circle cx="68.5" cy="52.5" r=".7"/><circle cx="69.7" cy="55" r=".7"/></g>""");
        sb.Append($"""<path d="M53.5 46.5 C53.5 43.8 66.5 43.8 66.5 46.5 C66.5 49.8 62.8 52.6 60 52.6 C57.2 52.6 53.5 49.8 53.5 46.5 Z" fill="{Ink}"/><ellipse cx="57.6" cy="45.6" rx="2.1" ry=".95" fill="#fff" opacity=".5"/><path d="M57.3 48.6 q1 .9 1.6 0 M61.1 48.6 q.6 .9 1.6 0" stroke="#000" stroke-width=".8" fill="none" opacity=".5"/>""");
        sb.Append("""<path d="M60 52.6 V55.6" stroke="#2B1C14" stroke-width="1.4" stroke-linecap="round"/>""");

        if (mood is not null)
            sb.Append(Face(mood));
        else
        {
            sb.Append("""<g class="leo-face">""");
            foreach (var m in MoodKeys) sb.Append($"""<g class="leo-face-{m}">{Face(m)}</g>""");
            sb.Append("</g>");
        }
        return sb.ToString();
    }

    private static string Ear(string p, bool left)
    {
        const string path = "M37 24.5 C29 24.5 23 32.5 22 43.5 C21 52.5 23 59.5 26 63.5 C27 61 28 60 29 62.8 C30 60 31 59 33 61.8 C34 59 35 58.2 37 60 C39 52 41 40 41 31 C41 27 39 24.5 37 24.5 Z";
        const string strands = "M29.5 37 C27.5 45 27.5 51.5 28.8 57.5 M34 33 C32.5 42 32.5 49 33.8 55";
        var inner = $"""<path d="{path}" fill="url(#{p}-ear)"/><path d="{strands}" stroke="#E7A957" stroke-width="1.1" fill="none" opacity=".6" stroke-linecap="round"/>""";
        return left ? inner : $"""<g transform="matrix(-1 0 0 1 120 0)">{inner}</g>""";
    }

    // ── Miny ──────────────────────────────────────────────────────────

    private static string Eyes(double dx = 0, double dy = 0) =>
        $"""<g class="leo-blink">{Eye(49, dx, dy)}{Eye(71, dx, dy)}</g>""";

    private static string Eye(double cx, double dx, double dy) =>
        $"""<ellipse cx="{cx}" cy="37.6" rx="4.4" ry="4.1" fill="#3A2416"/><ellipse cx="{cx + dx * .6}" cy="{37.9 + dy * .6}" rx="2.6" ry="2.4" fill="#1B100A"/><circle cx="{cx + 1.5 + dx}" cy="{36.2 + dy}" r="1.45" fill="#fff"/><circle cx="{cx - 1.3 + dx * .5}" cy="{39.3 + dy * .5}" r=".6" fill="#fff" opacity=".7"/>""";

    private const string HappyEyes = """<path d="M44.4 39.2 Q49 34.2 53.6 39.2" stroke="#3A2416" stroke-width="2.6" fill="none" stroke-linecap="round"/><path d="M66.4 39.2 Q71 34.2 75.6 39.2" stroke="#3A2416" stroke-width="2.6" fill="none" stroke-linecap="round"/>""";
    private const string ClosedEyes = """<path d="M44.4 38.2 Q49 41.6 53.6 38.2" stroke="#3A2416" stroke-width="2.3" fill="none" stroke-linecap="round"/><path d="M66.4 38.2 Q71 41.6 75.6 38.2" stroke="#3A2416" stroke-width="2.3" fill="none" stroke-linecap="round"/>""";

    private static string Brows(double lTilt = 0, double rTilt = 0, double lift = 0) =>
        $"""<path d="M44.2 {31.6 - lift + lTilt} Q49 {29.4 - lift} 53.4 {31.2 - lift - lTilt}" stroke="{Brow}" stroke-width="2" fill="none" stroke-linecap="round" opacity=".85"/><path d="M66.6 {31.2 - lift - rTilt} Q71 {29.4 - lift} 75.8 {31.6 - lift + rTilt}" stroke="{Brow}" stroke-width="2" fill="none" stroke-linecap="round" opacity=".85"/>""";

    private const string SmileMouth = """<path d="M60 55.6 C57.2 58.8 53.4 58.8 51.2 56.4 M60 55.6 C62.8 58.8 66.6 58.8 68.8 56.4" stroke="#2B1C14" stroke-width="1.5" fill="none" stroke-linecap="round"/>""";
    private const string TongueOut = $"""<path d="M56 57.2 C56 64 57.8 67.6 60 67.6 C62.2 67.6 64 64 64 57.2 Z" fill="{Tongue}"/><path d="M60 58.2 V65.6" stroke="#D9566E" stroke-width=".9" stroke-linecap="round"/>""";
    private const string ClosedMouth = """<path d="M60 55.6 C57.6 57.8 54.6 58 52.6 56.6 M60 55.6 C62.4 57.8 65.4 58 67.4 56.6" stroke="#2B1C14" stroke-width="1.4" fill="none" stroke-linecap="round"/>""";
    private const string OpenMouth = $"""<path d="M51.5 56 Q60 69.5 68.5 56 Q60 59.5 51.5 56 Z" fill="#5A2A22"/><path d="M55.6 61 Q60 67.6 64.4 61 Q60 63.2 55.6 61 Z" fill="{Tongue}"/>""";
    private const string AlertMouth = """<path d="M57.4 57.6 Q60 56.2 62.6 57.6 Q60 60.2 57.4 57.6 Z" fill="#2B1C14"/>""";

    private static string Face(string mood) => mood switch
    {
        "hello" => Brows() + Eyes() + SmileMouth + TongueOut,
        "happy" => Brows(lift: .6) + HappyEyes + SmileMouth + TongueOut,
        "celebrate" => Brows(lift: 1.4) + HappyEyes + OpenMouth,
        "think" => Brows(lTilt: 0, rTilt: -1.4, lift: .4) + Eyes(1.2, -1.4) + ClosedMouth,
        "remind" => Brows(lTilt: -1.2, rTilt: -1.2, lift: 1.4) + Eyes() + AlertMouth,
        "sleep" => Brows(lift: -.4) + ClosedEyes + ClosedMouth,
        _ => Brows() + Eyes() + SmileMouth,
    };

    // ── Leży (śpi) ────────────────────────────────────────────────────

    private static string Lie(string p, bool animated)
    {
        var sb = new StringBuilder();
        sb.Append("""<ellipse cx="60" cy="110.5" rx="44" ry="4" fill="#0F172A" opacity=".13"/>""");
        sb.Append($"""<path d="M94 106 C106 106 113 99 111 89 C109 96 103 100 92 100.5 Z" fill="url(#{p}-coat)"/>""");
        sb.Append($"""<g class="leo-chest-lie"><path d="M17 100 C17 86 35 79.5 60 79.5 C85 79.5 103 86 103 100 C103 107.5 91 109.5 60 109.5 C29 109.5 17 107.5 17 100 Z" fill="url(#{p}-coat)"/>""");
        sb.Append($"""<path d="M30 92 C36 86 46 84 54 85 M90 92 C84 86 74 84 66 85" stroke="{FurShade}" stroke-width="1.3" fill="none" opacity=".5" stroke-linecap="round"/></g>""");
        // Głowa opuszczona na łapy, oczy zamknięte.
        sb.Append($"""<g transform="translate(4.8 34) scale(.92)">{SleepHead(p)}</g>""");
        // Łapy przed brodą.
        sb.Append("""<ellipse cx="46" cy="101.5" rx="10.5" ry="5.2" fill="#F3CE8E"/><ellipse cx="74" cy="101.5" rx="10.5" ry="5.2" fill="#F3CE8E"/><path d="M41.5 99.5 v3.4 M45 99 v3.8 M48.5 99.3 v3.4 M71.5 99.3 v3.4 M75 99 v3.8 M78.5 99.5 v3.4" stroke="#D9A35A" stroke-width="1" stroke-linecap="round"/>""");
        return sb.ToString();
    }

    private static string SleepHead(string p) =>
        $"""<path d="M60 13 C75 13 85 22 86 36 C86.5 46 83 53 77 58 L43 58 C37 53 33.5 46 34 36 C35 22 45 13 60 13 Z" fill="url(#{p}-head)"/><path d="M53.5 15.5 C54.5 10.5 58 9.8 60 13 C62 9.8 65.5 10.5 66.5 15.5 Z" fill="{FurLight}"/>{Ear(p, true)}{Ear(p, false)}<path d="M47.5 44 C48 39.5 53 37.5 60 37.5 C67 37.5 72 39.5 72.5 44 L73.5 54.5 C73.5 61.5 67.5 65.5 60 65.5 C52.5 65.5 46.5 61.5 46.5 54.5 Z" fill="url(#{p}-muzzle)"/><path d="M53.5 46.5 C53.5 43.8 66.5 43.8 66.5 46.5 C66.5 49.8 62.8 52.6 60 52.6 C57.2 52.6 53.5 49.8 53.5 46.5 Z" fill="{Ink}"/><ellipse cx="57.6" cy="45.6" rx="2.1" ry=".95" fill="#fff" opacity=".5"/><path d="M60 52.6 V55.6" stroke="#2B1C14" stroke-width="1.4" stroke-linecap="round"/>{Face("sleep")}""";

    // ── Idzie (bokiem, w lewo) ────────────────────────────────────────

    private static string Walk(string p)
    {
        var sb = new StringBuilder();
        sb.Append("""<ellipse class="leo-walk-shadow" cx="64" cy="107" rx="38" ry="3.8" fill="#0F172A" opacity=".13"/>""");
        sb.Append("""<g class="leo-walk-body">""");
        // Dalsze nogi (ciemniejsze) pod tułowiem.
        sb.Append($"""<g class="leo-leg leo-leg-ff">{WalkLeg(42, 74, 27, FurShade, front: true)}</g><g class="leo-leg leo-leg-bf">{WalkLeg(80, 74, 27, FurShade, front: false)}</g>""");
        sb.Append($"""<g class="leo-tail-w"><path d="M94 66 C104 62 112 54 114 43 C115 38.5 111.5 37 109.8 40.5 C108 50 102 56.5 92 61 Z" fill="url(#{p}-coat)"/><path d="M109 43 C107.5 51 102 57 95 61" stroke="{Cream}" stroke-width="1.3" fill="none" opacity=".7" stroke-linecap="round"/></g>""");
        sb.Append($"""<path d="M36 68 C36 57 45 52 60 52 L82 52 C93 52 99 58 99 67 C99 76 93 82 84 82 L48 82 C41 82 36 76 36 68 Z" fill="url(#{p}-coat)"/>""");
        sb.Append($"""<path d="M48 80.5 L52 85 L56 81.5 L60 86 L64 81.5 L68 85.5 L72 81.5 L76 85 L80 81 L84 80.5 Z" fill="{FurLight}"/>""");
        sb.Append($"""<ellipse cx="87" cy="68" rx="12" ry="12.5" fill="{FurShade}" opacity=".55"/>""");
        sb.Append($"""<path d="M37 62 C33 70 34.5 79 40 85 L43.5 81.5 L46 87 L50 81.5 C46.5 76 45 69 46.5 61 Z" fill="url(#{p}-cream)"/>""");
        // Bliższe nogi.
        sb.Append($"""<g class="leo-leg leo-leg-fn">{WalkLeg(48, 76, 26, FurLight, front: true)}</g><g class="leo-leg leo-leg-bn">{WalkLeg(86, 76, 26, FurLight, front: false)}</g>""");
        // Szyja, obroża, głowa z profilu.
        sb.Append($"""<path d="M30 56 C32 47 41 43 48 50 L50 66 L36 68 Z" fill="url(#{p}-coat)"/>""");
        sb.Append("""<path d="M39 48.5 C43.5 50.5 46 56.5 45 63 L40.8 64 C41.8 58 39.8 53 36.4 51 Z" fill="#7C3AED"/><circle cx="42.2" cy="66.6" r="3" fill="#FBBF24" stroke="#F59E0B" stroke-width="1"/>""");
        sb.Append("""<g class="leo-head-w">""");
        sb.Append($"""<path d="M28 22 C39 22 45 29.5 44.5 40 C44 49 38 55 29 55.5 C20.5 56 14 49.5 14 40.5 C14 30 19 22 28 22 Z" fill="url(#{p}-head)"/>""");
        sb.Append($"""<path d="M24.5 24 C25 19.6 28.4 18.6 30 21.2 C31.6 19 34.8 20.2 35.4 24 Z" fill="{FurLight}"/>""");
        sb.Append($"""<path d="M18 41 C11.5 41 6 44 5.2 48 C4.8 51.8 8.5 54.2 15.5 54.2 C20 54.2 23.5 52 23.5 47.5 Z" fill="url(#{p}-muzzle)"/>""");
        sb.Append($"""<ellipse cx="6" cy="47.4" rx="3.1" ry="2.7" fill="{Ink}"/><ellipse cx="5.2" cy="46.4" rx="1.1" ry=".6" fill="#fff" opacity=".5"/>""");
        sb.Append("""<path d="M6.6 51.6 Q12 53.4 18.5 52" stroke="#2B1C14" stroke-width="1.3" fill="none" stroke-linecap="round"/>""");
        sb.Append($"""<path d="M10.5 52.6 C10.8 57.5 12.6 59.4 14.4 59.4 C16 59.4 16.6 57 16.4 53 Z" fill="{Tongue}"/>""");
        sb.Append("""<g class="leo-blink"><ellipse cx="24.5" cy="37.5" rx="2.8" ry="3.1" fill="#2A170E"/><circle cx="23.6" cy="36.4" r="1.05" fill="#fff"/></g>""");
        sb.Append($"""<path d="M20.6 33.2 Q24.5 31.2 28 33" stroke="{Brow}" stroke-width="1.6" fill="none" stroke-linecap="round" opacity=".85"/>""");
        sb.Append($"""<g class="leo-ear-w"><path d="M32 28.5 C38.5 28.5 42 35.5 41 45.5 C40.4 52 37.6 56.5 34.4 58.6 L33.2 54.6 L31.2 58.6 L30 54.4 C27.8 48 27.8 37 32 28.5 Z" fill="url(#{p}-ear)"/><path d="M33.6 35 C32.4 42 32.6 48.5 33.6 53" stroke="#E7A957" stroke-width="1" fill="none" opacity=".6" stroke-linecap="round"/></g>""");
        sb.Append("</g></g>");
        return sb.ToString();
    }

    /// <summary>Noga z boku: górą przy tułowiu (tam jest oś obrotu w animacji), na dole łapa.</summary>
    private static string WalkLeg(double x, double y, double h, string fill, bool front)
    {
        var feather = front
            ? $"""<path d="M{x + 9} {y + 4} L{x + 12} {y + 8} L{x + 9.5} {y + 10} L{x + 12} {y + 14}" fill="none" stroke="{Cream}" stroke-width="1.6" stroke-linejoin="round" opacity=".8"/>"""
            : $"""<path d="M{x + 9.5} {y + 1} C{x + 13} {y + 5} {x + 12.5} {y + 12} {x + 9.5} {y + 16}" fill="none" stroke="{Cream}" stroke-width="1.6" opacity=".7" stroke-linecap="round"/>""";
        return $"""<rect x="{x}" y="{y}" width="10" height="{h}" rx="5" fill="{fill}"/>{feather}<ellipse cx="{x + 4}" cy="{y + h}" rx="7" ry="3.8" fill="#F3CE8E"/>""";
    }

    // ── Efekty nastrojów ──────────────────────────────────────────────

    private static string Fx(string mood) => mood switch
    {
        "hello" => """<g fill="#FBBF24"><path d="M102 12 l2.4 5.2 5.2 2.4 -5.2 2.4 -2.4 5.2 -2.4 -5.2 -5.2 -2.4 5.2 -2.4z"/><circle cx="90" cy="9" r="2"/><circle cx="111" cy="30" r="1.5"/></g>""",
        "celebrate" => """<g><path d="M51 17 L59 -2 L67 16 Z" fill="#06D6A0" transform="rotate(-8 59 10)"/><path d="M54.5 9 l8.5 -1 M52.8 13.5 l12.4 -1.4" stroke="#fff" stroke-width="2" opacity=".75" transform="rotate(-8 59 10)"/><circle cx="56.6" cy="-1.6" r="3.2" fill="#FBBF24"/><rect x="12" y="12" width="6" height="3" rx="1" fill="#F472B6" transform="rotate(-25 15 13)"/><rect x="101" y="10" width="6" height="3" rx="1" fill="#06D6A0" transform="rotate(30 104 11)"/><circle cx="26" cy="24" r="2.4" fill="#FBBF24"/><circle cx="96" cy="28" r="2.4" fill="#60A5FA"/><path d="M86 12 l1.8 3.8 3.8 1.8 -3.8 1.8 -1.8 3.8 -1.8 -3.8 -3.8 -1.8 3.8 -1.8z" fill="#FBBF24"/><path d="M33 7 l1.4 3 3 1.4 -3 1.4 -1.4 3 -1.4 -3 -3 -1.4 3 -1.4z" fill="#A78BFA"/></g>""",
        "think" => """<g fill="#CBD5E1"><circle cx="92" cy="22" r="3"/><circle cx="100.5" cy="13" r="4"/><ellipse cx="110.5" cy="5" rx="7" ry="5"/></g>""",
        "remind" => """<g transform="translate(95 3)"><circle cx="9" cy="9" r="9" fill="#F59E0B"/><rect x="7.6" y="3.6" width="2.8" height="7.4" rx="1.4" fill="#fff"/><circle cx="9" cy="14" r="1.6" fill="#fff"/></g>""",
        "sleep" => """<g fill="#94A3B8" font-family="Arial, sans-serif" font-weight="700"><text class="leo-z leo-z1" x="88" y="64" font-size="13">Z</text><text class="leo-z leo-z2" x="100" y="52" font-size="9">z</text><text class="leo-z leo-z3" x="108" y="43" font-size="7">z</text></g>""",
        _ => "",
    };
}
