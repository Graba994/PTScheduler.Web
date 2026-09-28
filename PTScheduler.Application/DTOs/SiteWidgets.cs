namespace PTScheduler.Application.DTOs;

/// <summary>
/// Kreator strony głównej trenera: katalog widgetów, motywy i gotowe szablony stron.
/// Szablon wypełnia stronę przykładową treścią — trener potem podmienia teksty i zdjęcia.
/// </summary>
public static class SiteWidgets
{
    public sealed record Variant(string Key, string Name);

    public sealed record Widget(string Type, string Name, string Description, string Icon, string Group,
        IReadOnlyList<Variant> Variants, bool Live = false);

    public sealed record ThemeInfo(string Key, string Name, string Description, string Bg, string Surface, string Ink, string Accent, bool Dark);

    public sealed record PageTemplate(string Key, string Name, string Description, string Theme, string Icon);

    public static readonly IReadOnlyList<Widget> Catalog =
    [
        new("hero", "Powitanie (hero)", "Duży nagłówek z przyciskiem „Umów trening” — pierwsze, co widzi gość.", "bi-stars", "Start",
            [new("split", "Tekst + zdjęcie"), new("full", "Zdjęcie na całą szerokość"), new("center", "Wyśrodkowany z gradientem")]),
        new("marquee", "Przewijany pasek", "Pasek z hasłami, który sam się przesuwa — np. Redukcja • Siła • Mobilność.", "bi-arrow-left-right", "Start", []),
        new("stats", "Liczby", "Liczniki, które same odliczają przy przewijaniu: klienci, lata doświadczenia, kilogramy.", "bi-123", "Zaufanie", []),
        new("benefits", "Korzyści", "Dlaczego warto trenować z Tobą — ikony z krótkim opisem.", "bi-patch-check", "Oferta",
            [new("cards", "Kafelki"), new("list", "Lista z ikonami")]),
        new("about", "O mnie", "Twoje zdjęcie, historia i certyfikaty. Ludzie kupują od ludzi.", "bi-person-badge", "Zaufanie",
            [new("left", "Zdjęcie po lewej"), new("right", "Zdjęcie po prawej")]),
        new("transformations", "Metamorfozy", "Zdjęcia przed i po z suwakiem — najmocniejszy dowód efektów.", "bi-layout-split", "Zaufanie", []),
        new("steps", "Jak zacząć", "3–4 proste kroki od pierwszej wiadomości do efektów.", "bi-signpost-split", "Oferta", []),
        new("offer", "Cennik / pakiety", "Karty z cenami. Mogą pokazywać pakiety prosto z Twojej oferty w aplikacji.", "bi-tags", "Oferta", [], Live: true),
        new("slots", "Wolne terminy", "Najbliższe wolne godziny z Twojego grafiku — kliknięcie od razu rezerwuje.", "bi-calendar-check", "Sprzedaż", [], Live: true),
        new("testimonials", "Opinie", "Opinie klientów — także te zebrane w aplikacji. Wersja przewijana albo siatka.", "bi-chat-quote", "Zaufanie",
            [new("marquee", "Przewijane"), new("grid", "Siatka")], Live: true),
        new("gallery", "Galeria", "Zdjęcia z treningów i studia.", "bi-images", "Zaufanie", []),
        new("video", "Film", "Film z YouTube lub Vimeo — np. przedstawienie się albo trening.", "bi-play-btn", "Zaufanie", []),
        new("faq", "Pytania i odpowiedzi", "Rozwiewa wątpliwości przed zakupem.", "bi-question-circle", "Sprzedaż", []),
        new("cta", "Wezwanie do działania", "Mocny baner z przyciskiem i hasłem, np. „Zostały 3 wolne miejsca w tym miesiącu”.", "bi-megaphone", "Sprzedaż", []),
        new("contact", "Kontakt", "Telefon, e-mail, adres i media społecznościowe.", "bi-geo-alt", "Sprzedaż", []),
        new("text", "Tekst", "Dowolny akapit — gdy chcesz coś dopisać.", "bi-text-paragraph", "Inne", []),
    ];

    public static readonly IReadOnlyList<ThemeInfo> Themes =
    [
        new("studio", "Studio", "Jasny i czysty, w kolorze Twojej marki", "#f6f7fb", "#ffffff", "#0f172a", "", false),
        new("energia", "Energia", "Ciemny z neonowym akcentem", "#0b0f17", "#131a27", "#eef2f7", "#c6f432", true),
        new("premium", "Premium", "Czerń i złoto, eleganckie nagłówki", "#0d0d10", "#17171c", "#f5f1e8", "#d6b36a", true),
        new("natura", "Natura", "Spokojna zieleń, miękkie kształty", "#f3f6f1", "#ffffff", "#1b2a20", "#2f855a", false),
        new("sport", "Sport", "Mocny kontrast i pomarańcz", "#ffffff", "#f3f4f6", "#111111", "#ff5a1f", false),
        new("ocean", "Ocean", "Chłodny błękit i gradienty", "#eff6fb", "#ffffff", "#0b2239", "#0ea5e9", false),
        new("roz", "Róż", "Ciepły pudrowy róż, miękkie kształty", "#fff6f8", "#ffffff", "#2b1320", "#db2f6f", false),
        new("minimal", "Minimal", "Czerń i biel, dużo powietrza, szeryfowe nagłówki", "#ffffff", "#f4f4f2", "#111111", "#111111", false),
        new("ring", "Ring", "Ciemny z czerwienią, mocne wersaliki", "#101010", "#1b1b1b", "#f4f4f4", "#e5202e", true),
        new("noc", "Noc", "Granat i fiolet, nowocześnie", "#0d1024", "#161a38", "#eef0ff", "#8b7bff", true),
    ];

    public static readonly IReadOnlyList<PageTemplate> Templates =
    [
        new("personal", "Trener personalny", "Uniwersalna strona: kim jesteś, korzyści, cennik, wolne terminy.", "studio", "bi-person-arms-up"),
        new("transform", "Metamorfozy", "Efekty na pierwszym planie: przed i po, liczby, opinie.", "energia", "bi-fire"),
        new("premium", "Premium 1:1", "Elegancka strona dla treningów indywidualnych wyższej półki.", "premium", "bi-gem"),
        new("health", "Zdrowie i ruch", "Spokojnie i ciepło: mobilność, rehabilitacja, seniorzy.", "natura", "bi-heart-pulse"),
        new("online", "Trening online", "Prowadzenie online i plany: jak to działa, pakiety, pytania.", "ocean", "bi-laptop"),
        new("sport", "Siła i motoryka", "Dla sportowców: dynamiczny pasek, liczby, metamorfozy.", "sport", "bi-lightning-charge"),
        new("women", "Trening dla kobiet", "Sylwetka i pewność siebie bez presji: metamorfozy, opinie, pytania.", "roz", "bi-flower1"),
        new("mindful", "Pilates i mobilność", "Spokojnie i minimalistycznie: oddech, postawa, zdrowy kręgosłup.", "minimal", "bi-wind"),
        new("combat", "Sporty walki", "Boks, kickboxing, MMA: dynamika, film z treningu, mocne wezwanie.", "ring", "bi-shield-shaded"),
        new("bootcamp", "Treningi grupowe", "Bootcamp i małe grupy: grafik zajęć, wolne miejsca, karnety.", "noc", "bi-people-fill"),
        new("senior", "Aktywny senior", "Czytelnie i ciepło: bezpieczny ruch 60+, duże przyciski, telefon.", "natura", "bi-sun"),
        new("duo", "Treningi w parze", "Dla par i przyjaciół: taniej we dwoje, motywacja x2, pakiet Duet.", "ocean", "bi-people"),
    ];

    public static Widget? Find(string type) => Catalog.FirstOrDefault(w => w.Type == type);
    public static ThemeInfo ThemeOf(string? key) => Themes.FirstOrDefault(t => t.Key == key) ?? Themes[0];

    /// <summary>Nowy widget z przykładową treścią (po dodaniu od razu wygląda dobrze).</summary>
    public static SiteBlock Create(string type) => type switch
    {
        "hero" => new()
        {
            Type = "hero", Variant = "split",
            Eyebrow = "Trener personalny",
            Title = "Silniejsze ciało. Lepsze samopoczucie. Bez zgadywania.",
            Subtitle = "Treningi dopasowane do Ciebie, plan i opieka między sesjami. Umów pierwszy trening — zobaczysz, jak to działa.",
            CtaLabel = "Umów pierwszy trening", CtaUrl = "/book",
            Cta2Label = "Zobacz cennik", Cta2Url = "#cennik",
            Items = [new() { Icon = "bi-star-fill", Title = "4,9 / 5", Text = "opinie klientów" }, new() { Icon = "bi-lightning-charge-fill", Title = "Wolny termin", Text = "już jutro" }]
        },
        "marquee" => new()
        {
            Type = "marquee", Background = "accent",
            Items = [new() { Title = "Redukcja" }, new() { Title = "Siła" }, new() { Title = "Mobilność" }, new() { Title = "Zdrowy kręgosłup" }, new() { Title = "Kondycja" }, new() { Title = "Lepsza sylwetka" }]
        },
        "stats" => new()
        {
            Type = "stats",
            Items = [new() { Value = "150+", Title = "klientów" }, new() { Value = "8", Title = "lat doświadczenia" }, new() { Value = "1200+", Title = "kg zrzuconych przez klientów" }, new() { Value = "98%", Title = "poleca dalej" }]
        },
        "benefits" => new()
        {
            Type = "benefits", Variant = "cards", Title = "Dlaczego warto trenować ze mną",
            Items =
            [
                new() { Icon = "bi-bullseye", Title = "Plan pod Twój cel", Text = "Bez gotowców z internetu — ćwiczenia dobrane do Ciebie i Twojego dnia." },
                new() { Icon = "bi-graph-up-arrow", Title = "Widoczne postępy", Text = "Pomiary, ciężary i zdjęcia w aplikacji. Widzisz, że działa." },
                new() { Icon = "bi-calendar2-check", Title = "Rezerwacja w 10 sekund", Text = "Wolne terminy online, przypomnienie SMS dzień wcześniej." },
                new() { Icon = "bi-chat-heart", Title = "Wsparcie między treningami", Text = "Masz pytanie? Piszesz w aplikacji, odpowiadam." },
            ]
        },
        "about" => new()
        {
            Type = "about", Variant = "left", Title = "Cześć, jestem Twoim trenerem",
            Text = "Od lat pomagam ludziom wrócić do formy — bez skrajności i katowania się. Stawiam na technikę, systematyczność i plan, który da się utrzymać przy pracy i rodzinie.\n\nNa pierwszym spotkaniu poznamy Twój cel i sprawdzimy, od czego zacząć.",
            Items = [new() { Title = "Certyfikowany trener" }, new() { Title = "Dietetyka sportowa" }, new() { Title = "Trening funkcjonalny" }]
        },
        "transformations" => new()
        {
            Type = "transformations", Title = "Efekty moich podopiecznych", Subtitle = "Przesuń suwak, żeby zobaczyć różnicę.",
            Items = [new() { Title = "Kasia", Text = "−12 kg w 5 miesięcy" }, new() { Title = "Tomek", Text = "+8 kg mięśni w rok" }, new() { Title = "Ania", Text = "Koniec z bólem pleców" }]
        },
        "steps" => new()
        {
            Type = "steps", Title = "Jak zacząć?",
            Items =
            [
                new() { Title = "Umawiasz pierwszy trening", Text = "Wybierasz wolny termin online — zajmuje to chwilę." },
                new() { Title = "Poznajemy się", Text = "Rozmowa o celu, zdrowiu i krótki test sprawności." },
                new() { Title = "Dostajesz plan", Text = "Treningi i wskazówki w aplikacji, na telefonie." },
                new() { Title = "Widzisz efekty", Text = "Co kilka tygodni sprawdzamy postępy i korygujemy plan." },
            ]
        },
        "offer" => new()
        {
            Type = "offer", Title = "Cennik", Subtitle = "Im więcej treningów w pakiecie, tym taniej za jeden.", UseLiveData = true,
            Items =
            [
                new() { Title = "Pierwszy trening", Value = "99 zł", Text = "na start", Bullets = ["Rozmowa o celu", "Test sprawności", "Pierwszy trening"], Url = "/book" },
                new() { Title = "Pakiet 10 treningów", Value = "1 290 zł", Text = "129 zł / trening", Bullets = ["Plan w aplikacji", "Pomiary co miesiąc", "Wsparcie na czacie"], Url = "/packages", Highlight = true, Badge = "Najczęściej wybierany" },
                new() { Title = "Pakiet 20 treningów", Value = "2 380 zł", Text = "119 zł / trening", Bullets = ["Wszystko z pakietu 10", "Najniższa cena za trening", "Priorytet przy terminach"], Url = "/packages" },
            ]
        },
        "slots" => new() { Type = "slots", Background = "tint", Title = "Najbliższe wolne terminy", Subtitle = "Kliknij godzinę, żeby zarezerwować pierwszy trening." },
        "testimonials" => new()
        {
            Type = "testimonials", Variant = "marquee", Title = "Co mówią podopieczni", UseLiveData = true,
            Items =
            [
                new() { Title = "Marta", Badge = "trenuje od roku", Text = "Pierwszy raz trening sprawia mi przyjemność. Plecy przestały boleć po miesiącu." },
                new() { Title = "Paweł", Badge = "−9 kg", Text = "Konkretnie, bez lania wody. Plan dopasowany do mojej zmianowej pracy." },
                new() { Title = "Ola", Badge = "trening w parze", Text = "Trenujemy razem z mężem — motywacja x2 i taniej w pakiecie dla pary." },
            ]
        },
        "gallery" => new() { Type = "gallery", Title = "Z treningów", Items = [new(), new(), new(), new(), new(), new()] },
        "video" => new() { Type = "video", Title = "Zobacz, jak wygląda trening", Subtitle = "Dwie minuty o tym, jak pracuję." },
        "faq" => new()
        {
            Type = "faq", Title = "Pytania przed pierwszym treningiem",
            Items =
            [
                new() { Title = "Nie mam kondycji. Czy dam radę?", Text = "Tak. Zaczynamy od Twojego poziomu, a obciążenie rośnie razem z formą." },
                new() { Title = "Ile treningów w tygodniu?", Text = "Najczęściej 2–3. Ustalimy to na pierwszym spotkaniu, pod Twój grafik." },
                new() { Title = "Co jeśli nie mogę przyjść?", Text = "Odwołasz trening w aplikacji — bez telefonów i tłumaczenia się." },
                new() { Title = "Czy mogę trenować w parze?", Text = "Tak, jest pakiet dla pary — kupuje jedna osoba, trenujecie razem." },
            ]
        },
        "cta" => new()
        {
            Type = "cta", Background = "accent", Eyebrow = "Zostały 3 wolne miejsca w tym miesiącu",
            Title = "Zacznij od jednego treningu", Text = "Bez zobowiązań. Zobaczysz, jak pracuję, i wtedy zdecydujesz, co dalej.",
            CtaLabel = "Umów pierwszy trening", CtaUrl = "/book"
        },
        "contact" => new()
        {
            Type = "contact", Title = "Kontakt", Text = "Masz pytanie? Napisz albo zadzwoń.",
            Items = [new() { Icon = "bi-telephone", Title = "Telefon", Value = "+48 600 000 000", Url = "tel:+48600000000" }, new() { Icon = "bi-envelope", Title = "E-mail", Value = "kontakt@twojadomena.pl", Url = "mailto:kontakt@twojadomena.pl" }, new() { Icon = "bi-instagram", Title = "Instagram", Value = "@twoj.trener", Url = "https://instagram.com/" }, new() { Icon = "bi-geo-alt", Title = "Adres", Value = "ul. Przykładowa 1, Warszawa" }]
        },
        _ => new() { Type = "text", Title = "Nagłówek", Text = "Twój tekst." }
    };

    /// <summary>Gotowa strona z szablonu: motyw + zestaw widgetów z przykładową treścią.</summary>
    public static SiteContentDto BuildTemplate(string key, SiteContentDto? keep = null)
    {
        var t = Templates.FirstOrDefault(x => x.Key == key) ?? Templates[0];
        var blocks = key switch
        {
            "transform" => Seq(
                B("hero", b => { b.Variant = "full"; b.Eyebrow = "Metamorfozy z trenerem"; b.Title = "Twoja najlepsza forma zaczyna się dziś"; b.Subtitle = "Plan, technika i ktoś, kto pilnuje, żeby się udało. Sprawdź efekty moich podopiecznych."; b.Cta2Label = "Zobacz efekty"; b.Cta2Url = "#metamorfozy"; }),
                B("stats"), B("transformations"), B("benefits", b => b.Variant = "list"), B("offer"),
                B("testimonials"), B("cta"), B("contact")),
            "premium" => Seq(
                B("hero", b => { b.Variant = "center"; b.Eyebrow = "Trening personalny 1:1"; b.Title = "Indywidualnie. Dyskretnie. Skutecznie."; b.Subtitle = "Pełna uwaga trenera, plan szyty na miarę i elastyczne terminy — dla osób, które cenią swój czas."; b.CtaLabel = "Zarezerwuj konsultację"; b.Cta2Label = "Poznaj mnie"; b.Cta2Url = "#o-mnie"; }),
                B("about", b => b.Variant = "right"), B("gallery"), B("offer"), B("testimonials", b => b.Variant = "grid"),
                B("video"), B("cta", b => { b.Eyebrow = "Liczba miejsc ograniczona"; b.Title = "Porozmawiajmy o Twoim celu"; b.CtaLabel = "Zarezerwuj konsultację"; })),
            "health" => Seq(
                B("hero", b => { b.Eyebrow = "Zdrowy ruch w każdym wieku"; b.Title = "Ruszaj się bez bólu i z przyjemnością"; b.Subtitle = "Mobilność, zdrowy kręgosłup i siła na co dzień. Spokojnie, bezpiecznie, w Twoim tempie."; b.Items = [new() { Icon = "bi-heart-fill", Title = "Bezpiecznie", Text = "pod okiem trenera" }, new() { Icon = "bi-emoji-smile-fill", Title = "Bez presji", Text = "w Twoim tempie" }]; }),
                B("benefits", b => { b.Title = "Co zyskasz"; b.Items = [new() { Icon = "bi-person-walking", Title = "Swobodę ruchu", Text = "Łatwiej się schylić, wstać i nosić zakupy." }, new() { Icon = "bi-shield-check", Title = "Zdrowe plecy", Text = "Ćwiczenia, które odciążają kręgosłup." }, new() { Icon = "bi-moon-stars", Title = "Lepszy sen", Text = "Regularny ruch to spokojniejsza głowa." }, new() { Icon = "bi-people", Title = "Wsparcie", Text = "Zawsze wiesz, co i jak robić." }]; }),
                B("about"), B("steps"), B("testimonials", b => b.Variant = "grid"), B("faq"), B("slots"), B("contact")),
            "online" => Seq(
                B("hero", b => { b.Eyebrow = "Trening i prowadzenie online"; b.Title = "Trener w Twoim telefonie — gdziekolwiek jesteś"; b.Subtitle = "Plan treningowy w aplikacji, filmy z techniką, kontrola postępów i czat z trenerem."; b.CtaLabel = "Zacznij współpracę"; b.Items = [new() { Icon = "bi-phone", Title = "Plan w aplikacji", Text = "z filmami ćwiczeń" }, new() { Icon = "bi-chat-dots-fill", Title = "Czat z trenerem", Text = "odpowiedź w 24 h" }]; }),
                B("steps"), B("benefits"), B("offer"), B("testimonials"), B("faq"), B("cta")),
            "sport" => Seq(
                B("hero", b => { b.Variant = "full"; b.Eyebrow = "Przygotowanie motoryczne"; b.Title = "Szybciej. Mocniej. Dalej."; b.Subtitle = "Trening siły i motoryki dla sportowców i ambitnych amatorów. Mierzymy, planujemy, poprawiamy."; b.CtaLabel = "Umów test sprawności"; }),
                B("marquee", b => b.Items = [new() { Title = "Siła" }, new() { Title = "Szybkość" }, new() { Title = "Skoczność" }, new() { Title = "Wytrzymałość" }, new() { Title = "Prewencja urazów" }, new() { Title = "Technika" }]),
                B("stats"), B("benefits"), B("transformations"), B("offer"), B("cta"), B("contact")),
            "women" => Seq(
                B("hero", b => { b.Eyebrow = "Trening dla kobiet"; b.Title = "Silna, pewna siebie, w swoim tempie"; b.Subtitle = "Treningi bez oceniania i bez presji. Plan pod Twoje ciało, cykl i codzienność — z opieką między sesjami."; b.CtaLabel = "Umów pierwszy trening"; b.Items = [new() { Icon = "bi-heart-fill", Title = "Bez presji", Text = "i bez oceniania" }, new() { Icon = "bi-calendar-heart", Title = "Elastycznie", Text = "pod Twój grafik" }]; }),
                B("benefits", b => { b.Title = "Co zmieni się po kilku tygodniach"; b.Items = [new() { Icon = "bi-emoji-sunglasses", Title = "Pewność siebie", Text = "Lepiej czujesz się w swoim ciele — i to widać." }, new() { Icon = "bi-lightning-charge", Title = "Więcej energii", Text = "Mniej zmęczenia w ciągu dnia, lepszy sen." }, new() { Icon = "bi-person-arms-up", Title = "Jędrna sylwetka", Text = "Trening siłowy, który modeluje, a nie „rozbudowuje”." }, new() { Icon = "bi-shield-plus", Title = "Zdrowe plecy i miednica", Text = "Ćwiczenia bezpieczne także po ciąży." }]; }),
                B("about"), B("transformations", b => { b.Title = "Metamorfozy moich podopiecznych"; b.Items = [new() { Title = "Karolina", Text = "−9 kg i pierwsze podciągnięcie" }, new() { Title = "Magda", Text = "Powrót do formy po ciąży" }, new() { Title = "Ewa", Text = "Koniec z bólem kolan" }]; }),
                B("offer"), B("testimonials", b => b.Items = [new() { Title = "Natalia", Badge = "trenuje od pół roku", Text = "Pierwszy raz nie czuję się na siłowni nie na miejscu. Wszystko wytłumaczone, zero oceniania." }, new() { Title = "Asia", Badge = "−7 kg", Text = "Plan dopasowany do dwójki dzieci i pracy. Działa, bo da się go utrzymać." }, new() { Title = "Kinga", Badge = "po ciąży", Text = "Bezpieczny powrót do ruchu — mięśnie brzucha wreszcie wróciły." }]),
                B("faq", b => b.Items = [new() { Title = "Nigdy nie ćwiczyłam. Od czego zaczniemy?", Text = "Od rozmowy i prostego testu. Pierwsze tygodnie to technika i nawyki — bez zakwasów na tydzień." }, new() { Title = "Czy trening siłowy mnie „rozbuduje”?", Text = "Nie. Da jędrną, wysportowaną sylwetkę i mocniejsze kości." }, new() { Title = "Trenujesz kobiety po ciąży?", Text = "Tak — zaczynamy od bezpiecznej pracy z mięśniami głębokimi i dnem miednicy." }]),
                B("cta", b => { b.Eyebrow = "Pierwszy krok jest najtrudniejszy"; b.Title = "Zróbmy go razem"; b.Text = "Umów pierwszy trening — porozmawiamy o Twoim celu i od razu zaczniemy."; })),
            "mindful" => Seq(
                B("hero", b => { b.Variant = "center"; b.Eyebrow = "Pilates · mobilność · zdrowy kręgosłup"; b.Title = "Mniej napięcia. Więcej ruchu."; b.Subtitle = "Spokojne, precyzyjne treningi, po których wstajesz lżejsza i prosta jak struna. Indywidualnie lub w małej grupie."; b.CtaLabel = "Zarezerwuj zajęcia"; b.Cta2Label = "Grafik"; b.Cta2Url = "#terminy"; }),
                B("marquee", b => { b.Background = "default"; b.Items = [new() { Title = "Oddech" }, new() { Title = "Postawa" }, new() { Title = "Mobilność" }, new() { Title = "Mięśnie głębokie" }, new() { Title = "Równowaga" }, new() { Title = "Spokój" }]; }),
                B("benefits", b => { b.Variant = "list"; b.Title = "Dla kogo są te zajęcia"; b.Items = [new() { Icon = "bi-laptop", Title = "Pracujesz przy biurku", Text = "Odciążysz kark, barki i odcinek lędźwiowy." }, new() { Icon = "bi-bicycle", Title = "Trenujesz inny sport", Text = "Lepsza mobilność to mniej kontuzji i lepsza technika." }, new() { Icon = "bi-flower2", Title = "Chcesz się wyciszyć", Text = "Godzina tylko dla Ciebie — ruch zamiast scrollowania." }]; }),
                B("about", b => b.Variant = "right"), B("gallery", b => b.Title = "Studio"), B("offer"), B("slots"), B("contact")),
            "combat" => Seq(
                B("hero", b => { b.Variant = "full"; b.Eyebrow = "Boks · kickboxing · MMA"; b.Title = "Wejdź na matę. Wyjdź silniejszy."; b.Subtitle = "Technika, kondycja i charakter. Zajęcia dla początkujących i zawodników — indywidualnie i w grupach."; b.CtaLabel = "Pierwszy trening za darmo"; b.Cta2Label = "Grafik zajęć"; b.Cta2Url = "#terminy"; }),
                B("marquee", b => b.Items = [new() { Title = "Technika" }, new() { Title = "Kondycja" }, new() { Title = "Sparingi" }, new() { Title = "Samoobrona" }, new() { Title = "Pewność siebie" }, new() { Title = "Dyscyplina" }]),
                B("stats", b => b.Items = [new() { Value = "12", Title = "lat na ringu" }, new() { Value = "300+", Title = "wychowanków" }, new() { Value = "40+", Title = "walk zawodników" }, new() { Value = "5", Title = "treningów w tygodniu" }]),
                B("benefits", b => { b.Title = "Co dają sporty walki"; b.Items = [new() { Icon = "bi-fire", Title = "Spalanie jak nigdzie", Text = "Godzina treningu to nawet 800 kcal." }, new() { Icon = "bi-shield-check", Title = "Samoobrona", Text = "Wiesz, jak zareagować, gdy trzeba." }, new() { Icon = "bi-emoji-angry", Title = "Odreagowanie stresu", Text = "Worek przyjmie wszystko." }, new() { Icon = "bi-trophy", Title = "Cel do zdobycia", Text = "Kolejne pasy, sparingi, zawody." }]; }),
                B("video", b => { b.Title = "Zobacz, jak trenujemy"; b.Subtitle = "Minuta z naszego treningu."; }), B("offer"), B("testimonials"),
                B("cta", b => { b.Eyebrow = "Bez doświadczenia? Idealnie."; b.Title = "Pierwszy trening jest na nas"; b.CtaLabel = "Zapisz się"; })),
            "bootcamp" => Seq(
                B("hero", b => { b.Eyebrow = "Treningi w małych grupach"; b.Title = "Razem trenuje się łatwiej"; b.Subtitle = "Do 8 osób w grupie, trener, który zna Twoje imię, i energia, która niesie do końca treningu."; b.CtaLabel = "Zarezerwuj miejsce"; b.Cta2Label = "Grafik zajęć"; b.Cta2Url = "#terminy"; b.Items = [new() { Icon = "bi-people-fill", Title = "Max 8 osób", Text = "w grupie" }, new() { Icon = "bi-clock-fill", Title = "45 minut", Text = "konkretnego treningu" }]; }),
                B("steps", b => { b.Title = "Jak to działa"; b.Items = [new() { Title = "Wybierasz zajęcia", Text = "Grafik i wolne miejsca widzisz online." }, new() { Title = "Rezerwujesz miejsce", Text = "Jednym kliknięciem, z karnetu albo pojedynczo." }, new() { Title = "Przychodzisz i trenujesz", Text = "Dostajesz przypomnienie dzień wcześniej." }]; }),
                B("slots", b => { b.Title = "Najbliższe zajęcia"; b.Subtitle = "Kliknij godzinę, żeby zarezerwować miejsce."; }),
                B("benefits"), B("offer", b => { b.Title = "Karnety"; b.Subtitle = "Stała cena co miesiąc albo wejścia na zajęcia."; }), B("testimonials"), B("faq"), B("cta")),
            "senior" => Seq(
                B("hero", b => { b.Eyebrow = "Aktywny senior 60+"; b.Title = "Sprawność na co dzień. Bezpiecznie i spokojnie."; b.Subtitle = "Ćwiczenia, które ułatwiają zwykłe czynności: wstawanie, chodzenie po schodach, zakupy. Pod okiem trenera, w Twoim tempie."; b.CtaLabel = "Zadzwoń i umów się"; b.CtaUrl = "tel:+48600000000"; b.Cta2Label = "Zapisz się online"; b.Cta2Url = "/book"; b.Items = [new() { Icon = "bi-telephone-fill", Title = "Zapisy telefoniczne", Text = "+48 600 000 000" }, new() { Icon = "bi-heart-pulse-fill", Title = "Bezpiecznie", Text = "po konsultacji zdrowia" }]; }),
                B("benefits", b => { b.Title = "Co zyskasz"; b.Items = [new() { Icon = "bi-person-walking", Title = "Pewny krok", Text = "Lepsza równowaga to mniejsze ryzyko upadku." }, new() { Icon = "bi-bag", Title = "Siła na co dzień", Text = "Łatwiej nieść zakupy i wnosić je po schodach." }, new() { Icon = "bi-emoji-smile", Title = "Dobre samopoczucie", Text = "Ruch poprawia nastrój i sen." }, new() { Icon = "bi-people", Title = "Towarzystwo", Text = "Zajęcia indywidualne albo w małej grupie." }]; }),
                B("about"), B("steps"), B("testimonials", b => b.Variant = "grid"),
                B("faq", b => b.Items = [new() { Title = "Mam nadciśnienie / cukrzycę. Czy mogę ćwiczyć?", Text = "Najczęściej tak. Zaczynamy od rozmowy o zdrowiu i dopasowujemy ćwiczenia do zaleceń lekarza." }, new() { Title = "Nie mam smartfona. Jak się zapisać?", Text = "Wystarczy zadzwonić — zapiszę Cię i przypomnę o zajęciach SMS-em." }, new() { Title = "Czy potrzebuję sprzętu?", Text = "Nie. Wszystko jest na miejscu, wystarczy wygodny strój." }]),
                B("contact")),
            "duo" => Seq(
                B("hero", b => { b.Eyebrow = "Treningi w parze"; b.Title = "We dwoje raźniej — i taniej"; b.Subtitle = "Trenuj z partnerem, przyjaciółką albo mamą. Jeden trener, dwa plany, podwójna motywacja."; b.CtaLabel = "Umów trening dla dwojga"; b.Items = [new() { Icon = "bi-people-fill", Title = "Motywacja x2", Text = "trudniej odpuścić" }, new() { Icon = "bi-piggy-bank-fill", Title = "Taniej", Text = "na osobę w pakiecie Duet" }]; }),
                B("benefits", b => { b.Title = "Dlaczego w parze"; b.Items = [new() { Icon = "bi-heart", Title = "Wspólny czas", Text = "Godzina razem, zamiast osobno na kanapie i w pracy." }, new() { Icon = "bi-sliders", Title = "Dwa różne plany", Text = "Każde ćwiczy na swoim poziomie, pod swój cel." }, new() { Icon = "bi-calendar2-check", Title = "Jeden termin", Text = "Rezerwujecie raz, przychodzicie razem." }, new() { Icon = "bi-wallet2", Title = "Jeden zakup", Text = "Pakiet kupuje jedna osoba — dla was obojga." }]; }),
                B("steps", b => b.Items = [new() { Title = "Rezerwujecie termin", Text = "Jedna osoba wybiera godzinę i dopisuje partnera." }, new() { Title = "Poznajemy wasze cele", Text = "Każde dostaje plan dopasowany do siebie." }, new() { Title = "Trenujecie razem", Text = "Ćwiczenia w parze i obok siebie." }]),
                B("offer"), B("testimonials", b => b.Items = [new() { Title = "Ola i Michał", Badge = "razem od roku", Text = "Wreszcie mamy wspólne hobby. I nikt nie odwołuje treningu, bo wstyd przed drugą osobą." }, new() { Title = "Basia", Badge = "z córką", Text = "Trenujemy we dwie — ja dla zdrowia, córka do egzaminu na AWF. Każda ma swój plan." }]),
                B("faq", b => b.Items = [new() { Title = "Mamy różny poziom. Czy to problem?", Text = "Nie. Każda osoba ma swój plan i obciążenia, trenujecie w tym samym czasie." }, new() { Title = "Kto płaci za pakiet?", Text = "Pakiet Duet kupuje jedna osoba — treningi odliczają się wam obojgu." }, new() { Title = "Co jeśli jedno z nas nie może przyjść?", Text = "Możecie przełożyć termin w aplikacji albo trenować wyjątkowo osobno." }]),
                B("cta", b => { b.Eyebrow = "Przyprowadź kogoś bliskiego"; b.Title = "Pierwszy trening dla dwojga"; b.CtaLabel = "Umów trening w parze"; })),
            _ => Seq(
                B("hero"), B("marquee"), B("stats"), B("benefits"), B("about"), B("steps"), B("offer"),
                B("slots"), B("testimonials"), B("faq"), B("cta"), B("contact")),
        };

        var c = keep ?? new SiteContentDto();
        c.LayoutVersion = 2;
        c.Theme = t.Theme;
        c.Template = key;
        c.Blocks = blocks;
        c.Animations = true;
        return c;

        static SiteBlock B(string type, Action<SiteBlock>? edit = null)
        {
            var b = Create(type);
            edit?.Invoke(b);
            return b;
        }
        static List<SiteBlock> Seq(params SiteBlock[] b) => [.. b];
    }

    /// <summary>
    /// Przeniesienie treści ze starego układu sekcji do widgetów — teksty trenera zostają.
    /// </summary>
    public static SiteContentDto FromLegacy(SiteContentDto old)
    {
        var blocks = new List<SiteBlock>
        {
            new()
            {
                Type = "hero", Variant = !string.IsNullOrEmpty(old.HeroImageUrl) && old.HeroBackground == "image" ? "full" : "split",
                Eyebrow = old.HeroEyebrow, Title = old.HeroTitle, Subtitle = old.HeroSubtitle, ImageUrl = old.HeroImageUrl,
                CtaLabel = old.PrimaryCtaLabel, CtaUrl = old.PrimaryCtaUrl, Cta2Label = old.SecondaryCtaLabel, Cta2Url = old.SecondaryCtaUrl
            }
        };
        foreach (var key in old.SectionOrder)
        {
            switch (key)
            {
                case "stats" when old.ShowStats && old.Stats.Count > 0:
                    blocks.Add(new() { Type = "stats", Items = old.Stats.Select(x => new SiteBlockItem { Value = x.Value, Title = x.Label }).ToList() });
                    break;
                case "features" when old.ShowFeatures && old.Features.Count > 0:
                    blocks.Add(new() { Type = "benefits", Variant = "cards", Title = old.FeaturesTitle, Items = old.Features.Select(x => new SiteBlockItem { Icon = x.Icon, Title = x.Title, Text = x.Text }).ToList() });
                    break;
                case "about" when old.ShowAbout:
                    blocks.Add(new() { Type = "about", Variant = "left", Title = old.AboutTitle, Text = StripHtml(old.AboutHtml), ImageUrl = old.AboutImageUrl });
                    break;
                case "offer" when old.ShowOffer && old.Offers.Count > 0:
                    blocks.Add(new()
                    {
                        Type = "offer", Title = old.OfferTitle, UseLiveData = false,
                        Items = old.Offers.Select(o => new SiteBlockItem { Title = o.Name, Value = o.Price, Text = o.Period ?? o.Description, Bullets = o.Features, Url = o.CtaUrl, Highlight = o.Highlighted, Badge = o.Highlighted ? "Polecany" : null }).ToList()
                    });
                    break;
                case "testimonials":
                    blocks.Add(new()
                    {
                        Type = "testimonials", Variant = "grid", Title = old.TestimonialsTitle,
                        Items = old.ShowTestimonials ? old.Testimonials.Select(x => new SiteBlockItem { Title = x.Author, Badge = x.Role, Text = x.Text }).ToList() : []
                    });
                    break;
                case "faq" when old.ShowFaq && old.Faqs.Count > 0:
                    blocks.Add(new() { Type = "faq", Title = old.FaqTitle, Items = old.Faqs.Select(x => new SiteBlockItem { Title = x.Question, Text = x.Answer }).ToList() });
                    break;
                case "cta" when old.ShowCta:
                    blocks.Add(new() { Type = "cta", Background = "accent", Title = old.CtaTitle, Text = old.CtaText, CtaLabel = old.CtaButtonLabel, CtaUrl = old.CtaButtonUrl });
                    break;
            }
        }
        if (old.ShowSocial && old.Socials.Any(s => !string.IsNullOrWhiteSpace(s.Url)))
            blocks.Add(new()
            {
                Type = "contact", Title = "Kontakt",
                Items = old.Socials.Where(s => !string.IsNullOrWhiteSpace(s.Url)).Select(s => new SiteBlockItem { Icon = s.Icon, Title = s.Icon.Replace("bi-", ""), Value = s.Url, Url = s.Url }).ToList()
            });

        old.LayoutVersion = 2;
        old.Theme = old.Template == "dark" ? "energia" : "studio";
        old.Blocks = blocks;
        return old;
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return html;
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"</p>\s*<p[^>]*>", "\n\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}
