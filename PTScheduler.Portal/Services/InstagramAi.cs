using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using K = PTScheduler.Portal.Services.SiteSettingsService.Keys;

namespace PTScheduler.Portal.Services;

/// <summary>
/// AI dla Instagrama marki (OpenRouter przez <see cref="AiService"/>): pomysły na posty ze slajdami i opisem,
/// poprawianie opisu, hashtagi, odpowiedzi na komentarze i krótka analiza wyników. AI zna tylko fakty o Hantly
/// z katalogu funkcji i cennika — nie wymyśla funkcji. Gdy AI nie działa, pomysły biorą się z wbudowanego banku.
/// </summary>
public class InstagramAi(AiService ai, SiteSettingsService settings, IDbContextFactory<PortalDbContext> dbFactory)
{
    public const string Feature = "instagram";

    public sealed class Slide
    {
        [JsonPropertyName("template")] public string Template { get; set; } = "tip";
        [JsonPropertyName("eyebrow")] public string? Eyebrow { get; set; }
        [JsonPropertyName("headline")] public string Headline { get; set; } = "";
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("points")] public List<string>? Points { get; set; }
    }

    public sealed class Idea
    {
        [JsonPropertyName("pillar")] public string Pillar { get; set; } = "porada";
        [JsonPropertyName("format")] public string Format { get; set; } = "image";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("slides")] public List<Slide> Slides { get; set; } = [];
        [JsonPropertyName("caption")] public string Caption { get; set; } = "";
        [JsonPropertyName("hashtags")] public List<string> Hashtags { get; set; } = [];
        [JsonIgnore] public bool FromAi { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public Task<string?> UnavailableAsync() => ai.UnavailableReasonAsync();

    // ── Kontekst marki ──

    private async Task<string> SystemAsync()
    {
        var voice = (await settings.GetAsync(K.InstagramVoice)).Trim();
        var sb = new StringBuilder();
        sb.AppendLine("Jesteś social media managerem marki Hantly — polskiej aplikacji dla trenerów personalnych (grafik i rezerwacje, klienci, plany treningowe w telefonie klienta, płatności).");
        sb.AppendLine("Piszesz po polsku do trenerów personalnych, na „Ty”, konkretnie i życzliwie, bez korpomowy i bez przesadnych obietnic.");
        sb.AppendLine("Używaj form neutralnych płciowo: zamiast „zrobiłeś/aś” pisz np. „warto”, „Twoi klienci”, „udało się”. Nie podawaj nazwisk ani danych osób.");
        sb.AppendLine("Nie wymyślaj funkcji, liczb ani cen — korzystaj tylko z faktów poniżej. Jeśli czegoś nie ma w faktach, nie pisz o tym.");
        if (voice.Length > 0) sb.AppendLine($"Styl marki (od właściciela): {voice}");
        sb.AppendLine();
        sb.AppendLine("FAKTY O HANTLY:");
        foreach (var f in PlanFeatureCatalog.All.Where(f => f.Kind == PlanFeatureKind.Flag || f.Kind == PlanFeatureKind.Choice))
            sb.AppendLine($"- {f.Name}: {f.Benefit}");
        await using var db = await dbFactory.CreateDbContextAsync();
        var plans = await db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
        if (plans.Count > 0)
            sb.AppendLine("- Pakiety: " + string.Join(", ", plans.Select(p => p.MonthlyPrice <= 0 ? $"{p.Name} (za darmo)" : $"{p.Name} ({p.MonthlyPrice:0} zł/mies.)")) + ".");
        sb.AppendLine("- Program testerów: 3 miesiące za darmo w zamian za krótką ankietę raz w tygodniu.");
        sb.AppendLine("- Działa na telefonie i komputerze, klient instaluje aplikację z przeglądarki (bez sklepu).");
        sb.AppendLine($"- Strona: {(await settings.GetAsync(K.MainDomain)).Trim()}");
        return sb.ToString();
    }

    // ── Pomysły ──

    /// <summary>Jeden gotowy post (slajdy + opis + hashtagi) w danym filarze. Null = AI niedostępne albo zła odpowiedź.</summary>
    public async Task<(Idea? Idea, string? Error)> IdeaAsync(string pillar, string format, IEnumerable<string> recentTitles, string? brief = null, CancellationToken ct = default)
    {
        var p = InstagramRules.FindPillar(pillar) ?? InstagramRules.Pillars[0];
        var carousel = format == "carousel";
        var user = new StringBuilder();
        user.AppendLine($"Przygotuj post na Instagram marki. Filar: {p.Label} — {p.Hint}.");
        if (!string.IsNullOrWhiteSpace(brief)) user.AppendLine($"Temat od właściciela: {brief.Trim()}");
        var recent = recentTitles.Where(t => !string.IsNullOrWhiteSpace(t)).Take(15).ToList();
        if (recent.Count > 0) user.AppendLine("Nie powtarzaj tematów: " + string.Join("; ", recent) + ".");
        user.AppendLine(carousel
            ? "Format: karuzela 4–6 slajdów. Slajd 1 to haczyk (template tip, question albo myth), środkowe to konkret (tip albo list), ostatni to zachęta (cta)."
            : "Format: jedna grafika (template: tip, list, myth albo question).");
        user.AppendLine("""
            Zasady: headline do 70 znaków, body do 220 znaków, points (tylko w list) 2–5 pozycji do 60 znaków, eyebrow 1–3 słowa.
            Opis (caption) 400–900 znaków: pierwsze zdanie przyciąga uwagę, krótkie akapity, na końcu jedno wezwanie (zapisz post, napisz w komentarzu albo link w bio). Bez hashtagów w opisie.
            Hashtagi: 6–10, po polsku i branżowe (np. #trenerpersonalny), bez spacji.
            Odpowiedz WYŁĄCZNIE poprawnym JSON:
            {"pillar":"...","format":"image|carousel","title":"krótki temat roboczy","slides":[{"template":"tip|list|myth|question|cta","eyebrow":"...","headline":"...","body":"...","points":["..."]}],"caption":"...","hashtags":["#..."]}
            """);
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user.ToString(), maxTokens: carousel ? 1600 : 1000, ct: ct);
        if (r.Text is null) return (null, r.Error);
        var idea = Parse(r.Text);
        if (idea is null) return (null, "AI zwróciło odpowiedź w złym formacie — spróbuj jeszcze raz.");
        idea.Pillar = p.Key;
        idea.Format = carousel && idea.Slides.Count > 1 ? "carousel" : "image";
        if (idea.Format == "image") idea.Slides = idea.Slides.Take(1).ToList();
        idea.FromAi = true;
        return (idea, null);
    }

    public static Idea? Parse(string text)
    {
        try
        {
            var json = InstagramRules.JsonPart(text);
            if (json is null) return null;
            var idea = JsonSerializer.Deserialize<Idea>(json, Json);
            if (idea is null || idea.Slides.Count == 0 || string.IsNullOrWhiteSpace(idea.Slides[0].Headline)) return null;
            idea.Slides = idea.Slides.Where(s => !string.IsNullOrWhiteSpace(s.Headline)).Take(InstagramRules.MaxCarousel).ToList();
            foreach (var s in idea.Slides)
                if (InstagramGraphics.Templates.All(t => t.Key != s.Template) || s.Template == "screen") s.Template = "tip";
            idea.Caption = InstagramRules.Hashtags(idea.Caption).Aggregate(idea.Caption ?? "", (c, tag) => c.Replace(tag, "")).Trim();
            if (string.IsNullOrWhiteSpace(idea.Title)) idea.Title = InstagramGraphics.Clean(idea.Slides[0].Headline, 120);
            return idea;
        }
        catch (JsonException) { return null; }
    }

    // ── Opis ──

    public static readonly (string Key, string Label)[] CaptionActions =
    [
        ("write", "Napisz opis"), ("improve", "Popraw"), ("shorten", "Krócej"), ("hook", "Mocniejszy początek"), ("hashtags", "Dobierz hashtagi")
    ];

    public async Task<(string? Text, string? Error)> CaptionAsync(string action, string title, string slidesText, string caption, CancellationToken ct = default)
    {
        var task = action switch
        {
            "improve" => "Popraw ten opis: lepszy rytm, krótkie akapity, jasne wezwanie na końcu. Zachowaj sens.",
            "shorten" => "Skróć ten opis do ok. 300 znaków, zachowaj haczyk i wezwanie.",
            "hook" => "Przepisz tylko pierwsze zdanie tak, żeby zatrzymało kciuk trenera na scrollu; resztę zostaw.",
            "hashtags" => "Podaj 8–12 hashtagów do tego posta (polskie i branżowe), oddzielone spacją. Tylko hashtagi.",
            _ => "Napisz opis do posta (400–900 znaków): haczyk w pierwszym zdaniu, krótkie akapity, jedno wezwanie na końcu, bez hashtagów."
        };
        var user = $"{task}\n\nTemat: {title}\nTreść grafik: {slidesText}\n\nObecny opis:\n{caption}\n\nOdpowiedz samym tekstem, bez cudzysłowów i komentarzy.";
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: 700, ct: ct);
        return (r.Text?.Trim().Trim('"'), r.Error);
    }

    /// <summary>Opis przerobiony pod konkretną platformę (ton, długość, hashtagi) — z tych samych faktów.</summary>
    public async Task<(string? Text, string? Error)> AdaptAsync(string platform, string tone, int maxLength, string title, string caption, CancellationToken ct = default)
    {
        var user = $"""
            Przerób opis posta pod platformę {platform}. Zasady tej platformy: {tone}
            Najwyżej {maxLength} znaków łącznie z hashtagami. Zachowaj sens i fakty, nie dodawaj nowych obietnic.
            Tam, gdzie zapraszasz do sprawdzenia Hantly, wstaw dokładnie znacznik {Social.LinkService.Placeholder} (Portal podmieni go na link śledzący tej platformy).
            Jeśli opis mówi „link w bio”, zamień to na {Social.LinkService.Placeholder}. Znacznik wstaw najwyżej raz.
            Temat: {title}

            Opis z Instagrama:
            {caption}

            Odpowiedz samym gotowym tekstem, bez cudzysłowów i komentarzy.
            """;
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: Math.Clamp(maxLength / 2, 200, 1200), ct: ct);
        var text = r.Text?.Trim().Trim('"');
        if (text is not null && text.Length > maxLength) text = text[..maxLength].TrimEnd();
        return (text, r.Error);
    }

    // ── Komentarze ──

    public async Task<(string? Text, string? Error)> ReplyAsync(string comment, string? username, string? postCaption, CancellationToken ct = default)
    {
        var user = $"""
            Napisz krótką odpowiedź (1–2 zdania, do 200 znaków) marki Hantly na komentarz pod postem na Instagramie.
            Ciepło i konkretnie. Na pytanie o aplikację odpowiedz zgodnie z faktami i zaproś do linku w bio albo wiadomości prywatnej.
            Nie obiecuj niczego spoza faktów. Najwyżej jedna emotka. Nie zaczynaj od powtórzenia nazwy użytkownika.

            Post: {InstagramGraphics.Clean(postCaption, 400)}
            Komentarz od @{username}: {comment}

            Odpowiedz samym tekstem odpowiedzi.
            """;
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: 160, ct: ct);
        return (r.Text?.Trim().Trim('"'), r.Error);
    }

    // ── Analiza ──

    public async Task<(string? Text, string? Error)> AnalysisAsync(string statsSummary, CancellationToken ct = default)
    {
        var user = $"""
            Oto wyniki profilu marki na Instagramie. Napisz krótką analizę po polsku (do 6 punktów, każdy 1–2 zdania):
            co działa najlepiej, co słabiej, kiedy publikować, jakie 3 tematy zrobić w przyszłym tygodniu.
            Opieraj się tylko na liczbach poniżej; gdy danych jest mało, powiedz to wprost.

            {statsSummary}
            """;
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: 700, ct: ct);
        return (r.Text?.Trim(), r.Error);
    }

    /// <summary>Podpowiedź odpowiedzi na wiadomość prywatną (treść bez nazw, e-maili i telefonów).</summary>
    public async Task<(string? Text, string? Error)> DmReplyAsync(string platform, string conversation, CancellationToken ct = default)
    {
        var user = $"""
            Zaproponuj odpowiedź marki Hantly na wiadomość prywatną ({platform}). Rozmowa (od najstarszej):

            {conversation}

            Zasady: po polsku, ciepło i konkretnie, 1–4 zdania, najwyżej jedna emotka. Odpowiadaj tylko zgodnie z faktami o Hantly.
            Gdy pytają o założenie konta albo cenę — zaproś do linku: wstaw dokładnie {Social.LinkService.Placeholder}.
            Nie proś o dane wrażliwe. Gdy nie znasz odpowiedzi, napisz, że sprawdzisz i wrócisz z odpowiedzią.
            Odpowiedz samym tekstem wiadomości.
            """;
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: 400, ct: ct);
        return (r.Text?.Trim().Trim('"'), r.Error);
    }

    /// <summary>Wnioski z obserwowanego konta albo hasztagu: co działa i pomysły dla Hantly (bez kopiowania cudzych treści).</summary>
    public async Task<(string? Text, string? Error)> WatchAsync(string facts, CancellationToken ct = default)
    {
        var user = $"""
            Oto ostatnie posty obserwowanego konta albo hasztagu na Instagramie (rodzaj, polubienia, komentarze, czas, fragment opisu).
            Napisz po polsku do 5 krótkich punktów (od myślnika): jakie formaty i tematy działają najlepiej, kiedy publikują,
            i 3 pomysły na posty dla Hantly (aplikacja dla trenerów personalnych) inspirowane tym, co działa — bez kopiowania cudzych treści.
            Opieraj się tylko na danych poniżej.

            {facts}
            """;
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: 600, ct: ct);
        return (r.Text?.Trim(), r.Error);
    }

    /// <summary>3 wnioski do cotygodniowego raportu (same liczby na wejściu — bez danych osób).</summary>
    public async Task<(string? Text, string? Error)> WeeklyAsync(string facts, CancellationToken ct = default)
    {
        var user = $"""
            Oto liczby z ostatniego tygodnia mediów społecznościowych marki (Instagram, Facebook, Threads, linki śledzące do rejestracji).
            Napisz dokładnie 3 krótkie wnioski po polsku (każdy 1 zdanie, od myślnika): co zadziałało, co poprawić,
            i jedna konkretna rzecz do zrobienia w tym tygodniu. Najważniejsze są rejestracje z linków, nie polubienia.
            Opieraj się tylko na liczbach; gdy danych jest mało, powiedz to wprost.

            {facts}
            """;
        var r = await ai.CompleteAsync(Feature, null, await SystemAsync(), user, maxTokens: 400, ct: ct);
        return (r.Text?.Trim(), r.Error);
    }

    // ── Bank pomysłów (bez AI) ──

    private static Slide S(string template, string headline, string? body = null, string? eyebrow = null, params string[] points) =>
        new() { Template = template, Headline = headline, Body = body, Eyebrow = eyebrow, Points = points.Length > 0 ? [.. points] : null };

    private static Idea I(string pillar, string title, string caption, string[] tags, params Slide[] slides) =>
        new() { Pillar = pillar, Title = title, Format = slides.Length > 1 ? "carousel" : "image", Slides = [.. slides], Caption = caption, Hashtags = [.. tags] };

    private static readonly string[] Base = ["#trenerpersonalny", "#trenerpersonalnypolska", "#treningpersonalny", "#fitbiznes", "#hantly"];

    /// <summary>Gotowe pomysły, gdy AI jest wyłączone — od razu z grafikami i opisem.</summary>
    public static readonly Idea[] Bank =
    [
        I("porada", "Jak ograniczyć odwołane treningi",
            "Odwołany trening w ostatniej chwili to dziura w grafiku i w budżecie.\n\nTrzy rzeczy, które realnie zmniejszają liczbę odwołań:\n1. Przypomnienie dzień wcześniej — automatycznie, nie z pamięci.\n2. Stały termin co tydzień — klient planuje wokół treningu.\n3. Jasne zasady odwołań — znane od pierwszej wizyty.\n\nZapisz post, wróci się przy układaniu grafiku na kolejny miesiąc.",
            [.. Base, "#biznestrenera"],
            S("list", "3 sposoby na mniej odwołanych treningów", null, "Porada", "Przypomnienie dzień wcześniej", "Stały termin co tydzień", "Jasne zasady odwołań")),
        I("funkcja", "Rezerwacje bez pisania na WhatsAppie",
            "Ile wiadomości dziennie wymieniasz tylko po to, żeby umówić jeden trening?\n\nW Hantly klient widzi Twoje wolne terminy i sam rezerwuje. Ty dostajesz powiadomienie, a grafik układa się sam.\n\nLink w bio — sprawdź za darmo.",
            [.. Base, "#grafik", "#rezerwacjeonline"],
            S("tip", "Klient sam rezerwuje trening", "Widzi Twoje wolne terminy i zapisuje się w kilka sekund. Bez przepisywania z czatu do kalendarza.", "Funkcja")),
        I("mit", "Mit: dobry trener nie potrzebuje aplikacji",
            "„Dobry trener ma wszystko w głowie.” Do czasu, aż ma 25 klientów, trzy siłownie i rodzinę.\n\nAplikacja nie zastąpi Twojej wiedzy. Zabiera za to papierologię: przypomnienia, płatności, plany w telefonie klienta.\n\nCzas odzyskany z administracji to czas na klientów albo na odpoczynek.\n\nA Ty co pamiętasz najgorzej? Napisz w komentarzu.",
            [.. Base, "#organizacjaczasu"],
            S("myth", "Dobry trener ma wszystko w głowie", "Wiedza tak. Ale przypomnienia, płatności i plany lepiej oddać aplikacji — i odzyskać godziny w tygodniu.")),
        I("pytanie", "Co zabiera najwięcej czasu poza treningiem",
            "Prowadzenie treningów to tylko część pracy trenera.\n\nCo zabiera Ci najwięcej czasu poza salą? Umawianie terminów, rozliczenia, pisanie planów, a może przypominanie o płatnościach?\n\nNapisz w komentarzu — z odpowiedzi zrobimy kolejne porady.",
            [.. Base, "#pracatrenera"],
            S("question", "Co zabiera Ci najwięcej czasu poza treningami?", "Umawianie, rozliczenia, plany, przypomnienia?")),
        I("porada", "Jak utrzymać klienta po pierwszym miesiącu",
            "Najwięcej klientów rezygnuje po pierwszych tygodniach — zanim zobaczą efekty.\n\nCo pomaga przetrwać ten moment:\n— pomiary na starcie i co 4 tygodnie,\n— zdjęcia sylwetki (tylko dla Was dwojga),\n— krótka wiadomość po każdym treningu.\n\nEfekty, które widać czarno na białym, trzymają motywację dłużej niż obietnice.\n\nZapisz, przyda się przy następnym nowym kliencie.",
            [.. Base, "#motywacja", "#retencja"],
            S("tip", "Pierwszy miesiąc decyduje, czy klient zostanie", "Pokaż postęp czarno na białym: pomiary na starcie i co 4 tygodnie, zdjęcia sylwetki, krótka wiadomość po treningu.", "Porada")),
        I("funkcja", "Plan treningowy w telefonie klienta",
            "Kartka z planem ginie w szatni. Zdjęcie planu ginie w galerii.\n\nW Hantly klient ma plan w telefonie: ćwiczenia z filmami, serie, ciężary. Zapisuje wyniki na bieżąco, a Ty widzisz postęp bez dopytywania.\n\nLink w bio.",
            [.. Base, "#planyteningowe", "#trening"],
            S("tip", "Plan treningowy, który nie ginie w szatni", "Klient ćwiczy z planem w telefonie i zapisuje ciężary. Ty widzisz postęp od razu.", "Funkcja")),
        I("funkcja", "Mapa mięśni przy ćwiczeniach",
            "Klient pyta: „a co to właściwie ćwiczy?”\n\nPrzy każdym ćwiczeniu w Hantly widać postać z zaznaczonymi mięśniami — główne i pomocnicze, ogólnie albo szczegółowo.\n\nMniej tłumaczenia, więcej świadomego treningu.",
            [.. Base, "#anatomia", "#cwiczenia"],
            S("tip", "„A co to właściwie ćwiczy?”", "Przy każdym ćwiczeniu postać z zaznaczonymi mięśniami — główne i pomocnicze.", "Funkcja")),
        I("oferta", "Program testerów",
            "Szukamy trenerów personalnych, którzy chcą współtworzyć Hantly.\n\nDostajesz 3 miesiące pełnej aplikacji za darmo. W zamian raz w tygodniu wypełniasz krótką ankietę: co działa, co przeszkadza, czego brakuje.\n\nNapisz do nas w wiadomości prywatnej albo wejdź w link w bio.",
            [.. Base, "#testerzy"],
            S("tip", "3 miesiące Hantly za darmo", "Za krótką ankietę raz w tygodniu. Twoje uwagi zmieniają aplikację.", "Program testerów"),
            S("list", "Jak to działa", null, "Program testerów", "Zgłaszasz się", "Korzystasz z całej aplikacji", "Raz w tygodniu krótka ankieta"),
            S("cta", "Dołącz do testerów", "Napisz w wiadomości prywatnej albo wejdź w link w bio.")),
        I("porada", "Ile brać za trening",
            "Cena treningu to nie tylko godzina na sali. To dojazdy, przygotowanie planu, wiadomości, rozliczenia.\n\nPolicz, ile godzin tygodniowo pracujesz poza treningami. Jeśli cena tego nie uwzględnia — pracujesz za darmo.\n\nCo pomaga podnieść cenę bez utraty klientów: pakiety, karnety miesięczne i lepsze doświadczenie klienta.\n\nZapisz post przed rozmową o podwyżce.",
            [.. Base, "#cennik", "#biznestrenera"],
            S("tip", "Ile naprawdę zarabiasz na godzinę?", "Do czasu na sali dolicz dojazdy, plany, wiadomości i rozliczenia. Wynik bywa zaskakujący.", "Porada")),
        I("kulisy", "Hantly powstaje z trenerami",
            "Hantly nie powstaje za biurkiem. Każda funkcja to odpowiedź na problem, o którym mówią trenerzy.\n\nCzego brakuje Ci w obecnych narzędziach? Napisz w komentarzu — czytamy wszystko.",
            [.. Base, "#startup"],
            S("question", "Czego brakuje Ci w obecnych narzędziach?", "Czytamy każdy komentarz — najlepsze pomysły trafiają do aplikacji.", "Kulisy")),
        I("funkcja", "Płatności BLIK przy zakupie pakietu",
            "„Przeleję w weekend.” I tak trzeci tydzień.\n\nW Hantly klient płaci BLIK-iem albo kartą od razu przy zakupie pakietu. Ty nie przypominasz, a pieniądze trafiają bezpośrednio na Twoje konto.",
            [.. Base, "#platnosci", "#blik"],
            S("tip", "Koniec z „przeleję w weekend”", "Klient płaci BLIK-iem albo kartą przy zakupie pakietu. Bez przypominania.", "Funkcja")),
        I("mit", "Mit: klienci nie chcą kolejnej aplikacji",
            "„Moi klienci nie będą instalować kolejnej aplikacji.”\n\nW Hantly nie muszą szukać jej w sklepie — instalują ją z przeglądarki jednym kliknięciem, a przypomnienia przychodzą jak z każdej aplikacji.\n\nCo myślisz? Napisz w komentarzu.",
            [.. Base, "#aplikacja"],
            S("myth", "Klienci nie zainstalują kolejnej aplikacji", "Nie muszą szukać jej w sklepie — instalują ją z przeglądarki jednym kliknięciem.")),
    ];

    public static List<Idea> FromBank(string? pillar, IEnumerable<string> usedTitles, int count)
    {
        var used = usedTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pool = Bank.Where(i => (pillar is null || i.Pillar == pillar) && !used.Contains(i.Title)).ToList();
        if (pool.Count == 0) pool = Bank.Where(i => pillar is null || i.Pillar == pillar).ToList();
        if (pool.Count == 0) pool = [.. Bank];
        return pool.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
    }
}
