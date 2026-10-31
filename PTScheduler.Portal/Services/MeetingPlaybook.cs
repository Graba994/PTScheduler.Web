namespace PTScheduler.Portal.Services;

/// <summary>
/// Rozmowa z trenerem (Panel → Spotkania) w jednym miejscu: scenariusz blokami (Ty / Trener / wskazówka / rozgałęzienia),
/// obiekcje z odpowiedziami, fiszki do nauki i slajdy prezentacji aplikacji prowadzonej przez Hantly.
/// Z tych samych kluczy korzystają notatki (co zainteresowało, jakie obiekcje padły) i wyniki.
/// Teksty, które mówisz, są bez form rodzajowych — pasują do każdego rozmówcy.
/// </summary>
public static class MeetingPlaybook
{
    public enum Who { You, Trainer, Tip, Ask }

    /// <summary>Jeden blok rozmowy: kwestia, odpowiedź trenera, wskazówka albo lista pytań.</summary>
    public sealed record Block(Who Who, string Text, string[]? Items = null);

    /// <summary>„Jeśli trener powie…” — typowa odpowiedź i Twoja reakcja z krótkim „dlaczego”.</summary>
    public sealed record Branch(string TrainerSays, string YouSay, string Why);

    public sealed record Phase(string Key, string Title, string Time, string Icon, string Goal, Block[] Blocks, Branch[] Branches);

    public sealed record Objection(string Key, string Label, string TrainerSays, string[] Replies, string Why);

    public sealed record Card(string Front, string Back, string Hint);

    public static readonly string[] Principles =
    [
        "Słuchasz 80% czasu — pytasz i milczysz.",
        "Pytasz o konkretne sytuacje z przeszłości, nie o opinie.",
        "Pokazujesz tylko to, co rozwiązuje ich problem.",
        "Zapisujesz dosłowne cytaty.",
        "Kończysz konkretnym krokiem: start dziś albo termin rozmowy.",
    ];

    public static readonly Phase[] Phases =
    [
        new("prep", "Przygotowanie", "przed", "bi-clipboard-check", "Wiesz, z kim rozmawiasz, i masz wszystko pod ręką.",
        [
            new(Who.Tip, "Zajrzyj na Instagram i stronę trenera: ilu ma klientów, czy prowadzi treningi online, jak przyjmuje zapisy."),
            new(Who.Tip, "Otwórz notatki na telefonie, a prezentację na laptopie albo tablecie — najlepiej na pełnym ekranie."),
            new(Who.Tip, "Telefon naładowany, kod QR gotowy w notatkach jednym kliknięciem."),
        ], []),

        new("open", "Otwarcie", "2 min", "bi-chat-heart", "Luźna atmosfera i zgoda na pytania.",
        [
            new(Who.You, "Dzięki za czas! Tworzę Hantly — aplikację dla trenerów personalnych."),
            new(Who.You, "Zanim cokolwiek pokażę, chcę zrozumieć, jak pracujesz. Najpierw kilka pytań, potem pokażę to, co może się przydać. Mamy około 30 minut — pasuje?"),
            new(Who.Trainer, "Jasne, pytaj."),
            new(Who.Tip, "Powiedz, że robisz notatki — trener czuje wtedy, że jego zdanie naprawdę się liczy."),
        ], []),

        new("discover", "Odkrywanie", "10 min", "bi-search-heart", "Znaleźć 1–2 sprawy, które naprawdę bolą.",
        [
            new(Who.You, "Jak klienci zapisują się dziś do Ciebie na trening?"),
            new(Who.Trainer, "Piszą na WhatsAppie albo dzwonią."),
            new(Who.You, "A jak to wygląda wieczorem i w weekend?"),
            new(Who.Trainer, "No… często odpisuję o 23."),
            new(Who.Tip, "To jest ból — zapisz cytat dosłownie."),
            new(Who.Ask, "Dopytaj o resztę dnia", [
                "Kiedy ostatnio ktoś nie przyszedł bez odwołania? Co było dalej?",
                "Jak pilnujesz, kto ile zapłacił i ile treningów zostało w pakiecie?",
                "Jak przekazujesz klientom plany treningowe?",
                "Ile czasu w tygodniu zajmują Ci wiadomości i rozliczenia?",
                "Czego używasz dziś i ile za to płacisz?",
            ]),
            new(Who.Tip, "Dopytuj krótko: „Jak to wyglądało?”, „Co było dalej?”, „Dlaczego?”. Nie pytaj „czy kupisz?” — z grzeczności każdy powie „tak”."),
        ],
        [
            new("U mnie wszystko działa — mam zeszyt i WhatsApp.", "Super. A co by się działo, gdyby klientów było dwa razy więcej?",
                "Szukasz granicy obecnego sposobu pracy — nie przekonujesz."),
            new("Nie wiem, ile czasu mi to zajmuje.", "A wczoraj? Ile wiadomości o terminach i płatnościach było?",
                "Konkretny dzień daje prawdziwą liczbę, ogólne pytanie — zgadywanie."),
        ]),

        new("summary", "Podsumowanie", "1 min", "bi-arrow-repeat", "Trener słyszy swój problem Twoimi słowami.",
        [
            new(Who.You, "Czyli najwięcej czasu zabierają Ci wiadomości o terminach i pilnowanie płatności — dobrze rozumiem?"),
            new(Who.Trainer, "Dokładnie tak."),
            new(Who.Tip, "Jeśli trener poprawi — świetnie, masz dokładniejszy obraz. Dopiero teraz pokazujesz aplikację."),
        ], []),

        new("present", "Prezentacja", "10 min", "bi-easel2", "Pokazać 3–4 rzeczy, które rozwiązują ich problem.",
        [
            new(Who.You, "Pokażę Ci, jak to wygląda u trenera i u klienta. Oprowadzi nas Hantly — nasz asystent."),
            new(Who.Tip, "Otwórz „Prezentację”. Gwiazdką przy slajdzie zaznaczasz, co zainteresowało trenera — trafia to do notatek."),
            new(Who.Ask, "Pokaż to, co pasuje do bólu", [
                "Zapisy na WhatsAppie o 23 → Grafik i zapisy online",
                "Zeszyt z płatnościami → Pieniądze bez gonienia",
                "Klienci znikają bez słowa → Kto może odejść",
                "Plany w PDF-ach → Plany treningowe i trening na żywo",
                "Puste okienka po odwołaniu → Wolne okienka",
            ]),
            new(Who.Tip, "Po każdym slajdzie zapytaj: „Jak by to u Ciebie zadziałało?” — i znowu słuchaj."),
        ],
        [
            new("Czy klient musi coś instalować?", "Nie musi — dostaje link. Aplikacja działa w przeglądarce, a jednym kliknięciem doda ją do ekranu telefonu.",
                "Krótka, konkretna odpowiedź i wracasz do prezentacji."),
            new("Czy to będzie z moim logo?", "Tak — Twoje logo, kolory i ikona na telefonie klienta. Klient widzi Twoją aplikację.",
                "Marka trenera to częsty, ukryty powód decyzji — warto go usłyszeć."),
        ]),

        new("objections", "Obiekcje", "gdy padną", "bi-shield-check", "Zrozumieć obawę, zanim odpowiesz.",
        [
            new(Who.Tip, "Najpierw dopytaj: „Co dokładnie masz na myśli?” — obiekcja to często pytanie o coś innego."),
            new(Who.Tip, "Odpowiedzi na najczęstsze obiekcje masz niżej i w fiszkach do nauki."),
        ], []),

        new("close", "Propozycja", "3 min", "bi-hand-thumbs-up", "Konkretny następny krok — start albo termin.",
        [
            new(Who.You, "Proponuję tak: zakładamy dziś Twoją aplikację. Przez 14 dni masz pełny plan Pro za darmo, bez karty."),
            new(Who.You, "A jeśli raz w tygodniu powiesz mi, co działa, a co nie — dostajesz od nas 3 miesiące gratis."),
            new(Who.Trainer, "Brzmi dobrze, spróbujmy."),
            new(Who.Tip, "W notatkach kliknij „Daj kod QR” — trener skanuje i od razu buduje swoją aplikację, przy Tobie."),
        ],
        [
            new("A ile to kosztuje po okresie próbnym?", "Wybierasz plan, który pasuje — albo zostajesz na darmowym Start. Ceny są na ostatnim slajdzie prezentacji, pokażę.",
                "Cena na końcu, po wartości. Pokaż plany, zamiast recytować liczby."),
            new("Nie, to nie dla mnie.", "Dzięki za szczerość. Czego musiałoby być więcej albo mniej, żeby miało sens?",
                "Odmowa też jest wynikiem — i najlepszą lekcją, co poprawić w produkcie."),
        ]),

        new("after", "Po spotkaniu", "5 min", "bi-journal-check", "Nic nie ucieka — notatki, termin, podziękowanie.",
        [
            new(Who.Tip, "Uzupełnij notatki od razu, póki pamiętasz cytaty."),
            new(Who.Tip, "Jeśli trener się zastanawia — ustaw termin kontaktu. Leo przypomni w dniu rozmowy."),
            new(Who.Tip, "Wyślij krótkie podziękowanie z linkiem do aplikacji albo z kodem."),
        ], []),
    ];

    public static readonly Objection[] Objections =
    [
        new("price", "Za drogie", "To trochę dużo jak na mnie.",
            ["Rozumiem. Ile kosztuje Cię jeden trening, na który klient nie przyszedł?",
             "Jeśli aplikacja uratuje jeden taki trening w miesiącu, zwraca się. A na start jest darmowy plan Start i 14 dni Pro."],
            "Liczysz z trenerem jego pieniądze zamiast bronić ceny."),
        new("time", "Brak czasu na wdrożenie", "Nie mam teraz czasu tego ustawiać.",
            ["Konfiguracja zajmuje około 15 minut, a klientów zaimportujesz z pliku.", "Mogę to zrobić z Tobą teraz, przy kawie."],
            "Usuwasz barierę od razu, przy stole."),
        new("clients-app", "Klienci nie zainstalują", "Moi klienci nie będą instalować kolejnej aplikacji.",
            ["Nie muszą niczego instalować ze sklepu — dostają link.", "Kto nie chce aplikacji, dostaje przypomnienia e-mailem, a Ty i tak masz wszystko w jednym miejscu."],
            "Obawa dotyczy klientów — pokaż, że nic na nich nie wymuszasz."),
        new("has-tool", "Mam już inne narzędzie", "Mam już Booksy i Excela.",
            ["Co w nich lubisz, a czego brakuje?", "Hantly to nie tylko kalendarz: plany treningowe, postępy klientów i asystent, który pilnuje płatności i przerw."],
            "Najpierw doceniasz obecne narzędzie — dopiero potem pokazujesz różnicę."),
        new("think", "Muszę się zastanowić", "Muszę to przemyśleć.",
            ["Jasne. Co jest dla Ciebie najważniejsze przy decyzji?", "Zostawmy tak: 14 dni testujesz za darmo, bez karty, a w czwartek zdzwonimy się na 10 minut."],
            "Wydobywasz prawdziwą obawę i zawsze ustalasz termin."),
        new("data", "Dane i RODO", "A co z moimi danymi i danymi klientów?",
            ["Dane są Twoje — w każdej chwili pobierzesz je jednym kliknięciem.", "Klienci akceptują regulamin i politykę prywatności w aplikacji, a dostęp do ich danych masz tylko Ty."],
            "Spokojna, rzeczowa odpowiedź buduje zaufanie bardziej niż zapewnienia."),
        new("few-clients", "Mam za mało klientów", "Mam dopiero kilku klientów, to chyba nie dla mnie.",
            ["Właśnie po to jest darmowy plan Start — aplikacja rośnie razem z Tobą.", "Strona z przyciskiem „Umów trening” pomaga też zdobywać nowych klientów."],
            "Zamieniasz „za wcześnie” w „najlepszy moment, żeby zacząć”."),
        new("ai", "Nie ufam AI", "Nie chcę, żeby AI pisało do moich klientów.",
            ["AI tylko podpowiada — każdą wiadomość najpierw widzisz i decydujesz, czy ją wysłać.", "Bez AI wszystko też działa, a asystenta wyłączysz jednym przełącznikiem."],
            "Kontrola zostaje u trenera — to jest sedno odpowiedzi."),
    ];

    public static readonly string[] TrainerTypes = ["Trener personalny", "Studio / zespół", "Treningi online", "Fizjoterapeuta", "Inaczej"];
    public static readonly string[] BookingOptions = ["WhatsApp / SMS", "Telefon", "Excel / kartka", "Kalendarz Google", "Booksy lub inny system", "Aplikacja trenerska", "Inaczej"];

    /// <summary>Wynik spotkania: klucz → (etykieta, ikona).</summary>
    public static readonly (string Key, string Label, string Icon)[] Outcomes =
    [
        ("start", "Startuje", "bi-rocket-takeoff"),
        ("followup", "Wracamy do tematu", "bi-calendar-event"),
        ("no", "Nie teraz", "bi-pause-circle"),
    ];

    /// <summary>Fiszki: pytania z odkrywania, gałęzie rozmowy i obiekcje — przód to sytuacja, tył to Twoja odpowiedź.</summary>
    public static IReadOnlyList<Card> Cards()
    {
        var cards = new List<Card>();
        foreach (var p in Phases.Where(p => p.Key is not ("prep" or "after")))
            cards.Add(new($"{p.Title}: jaki jest cel tej części?", p.Goal, $"Czas: {p.Time}"));
        cards.Add(new("Odkrywanie: o co zapytasz, gdy wiesz już, jak zapisują się klienci?",
            string.Join("\n", Phases.First(p => p.Key == "discover").Blocks.First(b => b.Who == Who.Ask).Items!), "Pytaj o konkretne sytuacje z przeszłości"));
        foreach (var b in Phases.SelectMany(p => p.Branches))
            cards.Add(new($"Trener: „{b.TrainerSays}”", b.YouSay, b.Why));
        foreach (var o in Objections)
            cards.Add(new($"Trener: „{o.TrainerSays}”", string.Join("\n", o.Replies), o.Why));
        return cards;
    }

    // ── Prezentacja aplikacji ─────────────────────────────────

    /// <param name="Chapter">trener / klient / plany.</param>
    /// <param name="Scene">Nazwa sceny z HomeScene (animowany fragment aplikacji).</param>
    /// <param name="Image">Zrzut aplikacji klienta (wwwroot/img/lp, bez rozszerzenia) w ramce telefonu.</param>
    /// <param name="Mood">Mina Hantly (hello, think, happy, celebrate, remind).</param>
    /// <param name="Trick">Reakcja Hantly przy wejściu na slajd (calendar, coins, chart, trophy…).</param>
    /// <param name="Ask">Pytanie do trenera po slajdzie — widzi je tylko prowadzący.</param>
    public sealed record Slide(string Key, string Chapter, string Title, string Line, string[] Points, string Icon,
        string? Scene = null, string? Image = null, string Mood = "happy", string Trick = "talk", string Ask = "Jak by to u Ciebie zadziałało?");

    public static readonly Slide[] Slides =
    [
        new("intro", "start", "Cześć!", "Jestem Hantly. Pokażę, jak wygląda dzień trenera z naszą aplikacją — i co widzą Twoi klienci.",
            ["Grafik, zapisy i płatności w jednym miejscu", "Aplikacja dla klientów pod Twoją marką", "Asystent, który pilnuje spraw za Ciebie"], "bi-stars",
            Mood: "hello", Trick: "party", Ask: "Co zabiera Ci dziś najwięcej czasu poza treningami?"),

        new("brief", "trener", "Poranny brief", "Zanim wypijesz kawę, wiesz wszystko: ile wizyt, kto ma uwagi zdrowotne i co czeka na Twoją decyzję.",
            ["Plan dnia w jednym miejscu", "Sprawy do załatwienia z gotowym przyciskiem", "Także e-mailem i powiadomieniem"], "bi-sunrise",
            Scene: "brief", Mood: "hello", Trick: "sun"),
        new("cal", "trener", "Grafik i zapisy online", "Klienci rezerwują sami — o każdej porze, tylko w Twoich wolnych godzinach. Koniec z WhatsAppem o 23:00.",
            ["Rezerwacje z Twoich godzin pracy", "Serie wizyt i szablon tygodnia", "Przypomnienia bez Twojego udziału"], "bi-calendar-week",
            Scene: "cal", Trick: "calendar", Ask: "Ile wiadomości o terminach dostajesz w tygodniu?"),
        new("client", "trener", "Każdy klient jak na dłoni", "Historia wizyt, pakiety, treningi i pomiary — wszystko o kliencie na jednym ekranie.",
            ["Profil z historią i celami", "Ankieta zdrowotna przed pierwszym treningiem", "Czat z klientem w aplikacji"], "bi-person-vcard",
            Scene: "client", Mood: "think", Trick: "magnifier"),
        new("plan", "trener", "Plany treningowe", "Plan układasz raz, a klient ma go zawsze w telefonie. Podpowiadam, gdy czas zwiększyć ciężar.",
            ["Katalog ćwiczeń po polsku z filmami", "Propozycja planu z historii klienta", "Plan w PDF z Twoim logo"], "bi-clipboard-pulse",
            Scene: "plan", Trick: "warmup", Ask: "Jak dziś przekazujesz klientom plany?"),
        new("debts", "trener", "Pieniądze bez gonienia", "Wszystkie zaległości w jednym miejscu. Wpłatę odhaczasz jednym kliknięciem, a przypomnienie napiszę za Ciebie.",
            ["Płatności online: BLIK i karta", "Pakiety odliczają się same", "Uprzejme przypomnienie w Twoim stylu"], "bi-cash-coin",
            Scene: "debts", Trick: "coins", Ask: "Jak pilnujesz dziś, kto ile zapłacił?"),
        new("slots", "trener", "Wolne okienko nie przepada", "Ktoś odwołał trening? Podpowiem, kto zwykle trenuje o tej porze, i wyślę zaproszenie z linkiem.",
            ["Odwołanie zwalnia termin od razu", "Zaproszenie do osób, które pasują", "Rezerwacja jednym kliknięciem"], "bi-calendar-plus",
            Scene: "slots", Trick: "ring"),
        new("risk", "trener", "Kto może odejść", "Widzę przerwy i spadek formy, zanim klient zniknie. Podpowiem, do kogo napisać i co.",
            ["Lista klientów, którzy odpuszczają", "Gotowa wiadomość w Twoim stylu", "Ty decydujesz, czy ją wysłać"], "bi-person-exclamation",
            Scene: "risk", Mood: "think", Trick: "magnifier", Ask: "Kiedy ostatnio klient zniknął bez słowa?"),
        new("stats", "trener", "Biznes w liczbach", "Co się zmieniło w tym miesiącu — retencja, przychód i prognoza na 30 dni, w trzech zdaniach.",
            ["Przychód i prognoza z odnowień", "Obłożenie grafiku", "Kto trenuje regularnie, a kto odpuszcza"], "bi-graph-up-arrow",
            Scene: "stats", Trick: "stonks"),
        new("garmin", "trener", "Zegarki klientów", "Klient z zegarkiem Garmin? Przed treningiem widzisz, jak spał i czy jest gotowy na ciężki trening.",
            ["Sen, HRV i Body Battery przed wizytą", "Plan wysyłany prosto na zegarek", "Tylko za zgodą klienta"], "pt-ico-watch",
            Mood: "think", Trick: "sweat"),
        new("close", "trener", "Dzień domknięty", "Wieczorem wszystko się zgadza: wizyty zamknięte, wpłaty policzone, a paczka dla księgowej gotowa.",
            ["Wizyty zamykają się same", "Wpływy dnia i tygodnia", "Zestawienie dla księgowej jednym kliknięciem"], "bi-moon-stars",
            Scene: "close", Mood: "celebrate", Trick: "trophy"),

        new("c-app", "klient", "Aplikacja Twoich klientów", "Twój klient dostaje aplikację z Twoim logo — bez sklepu, wystarczy link. Tak to wygląda w jego telefonie.",
            ["Rezerwacje i odwołania bez pisania", "Powiadomienia przed treningiem", "Twoja marka na ekranie telefonu"], "bi-phone",
            Image: "video", Mood: "hello", Trick: "hop", Ask: "Jak klienci dowiadują się dziś o terminach?"),
        new("train", "klient", "Trening na żywo", "Klient ćwiczy z planem w telefonie: serie, ciężary i przerwy zapisują się same.",
            ["Plan na dziś zawsze pod ręką", "Ciężary zapisują się automatycznie", "Ty widzisz, co klient zrobił"], "bi-lightning-charge",
            Scene: "train", Trick: "warmup"),
        new("c-progress", "klient", "Postępy, które motywują", "Klient widzi swoje postępy: siłę, pomiary i zdjęcia sylwetki w czasie.",
            ["Wykresy siły i pomiarów", "Zdjęcia sylwetki", "Rekordy, które cieszą"], "bi-graph-up",
            Image: "klient-postepy", Trick: "chart"),
        new("c-medals", "klient", "Medale i nawyki", "Medale za serie i rekordy sprawiają, że klientom chce się wracać.",
            ["Medale za regularność", "Serie treningów z rzędu", "Powód do dumy i polecania"], "bi-trophy",
            Image: "klient-medale", Mood: "celebrate", Trick: "trophy"),
        new("c-packages", "klient", "Pakiety i płatności", "Klient sam kupuje pakiet i widzi, ile treningów mu zostało. Ty nie musisz przypominać.",
            ["Zakup pakietu online", "Licznik treningów w pakiecie", "Przypomnienie, gdy pakiet się kończy"], "bi-box-seam",
            Image: "klient-pakiety", Trick: "coins"),

        new("outro", "plany", "Zacznij za darmo", "Na start masz 14 dni pełnego planu Pro za darmo, bez karty. Potem wybierasz plan — albo zostajesz na darmowym.",
            ["Konfiguracja w 15 minut", "Twoje logo i kolory od pierwszego dnia", "Rezygnujesz jednym kliknięciem"], "bi-rocket-takeoff",
            Mood: "celebrate", Trick: "party", Ask: "Który z tych planów pasuje do Twojej pracy?"),
    ];

    public static readonly (string Key, string Label)[] Chapters = [("start", "Start"), ("trener", "Dla trenera"), ("klient", "Dla klienta"), ("plany", "Plany")];

    /// <summary>Slajdy do wyboru „co zainteresowało” w notatkach (bez powitania i planów).</summary>
    public static IEnumerable<Slide> FeatureSlides(bool garmin) => Slides.Where(s => s.Chapter is "trener" or "klient" && (garmin || s.Key != "garmin"));

    public static string? SlideTitle(string key) => Slides.FirstOrDefault(s => s.Key == key)?.Title;
    public static string? ObjectionLabel(string key) => Objections.FirstOrDefault(o => o.Key == key)?.Label;
}
