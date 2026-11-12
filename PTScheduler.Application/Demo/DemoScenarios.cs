namespace PTScheduler.Application.Demo;

/// <summary>
/// Scenariusze danych testowych — „jakby trener pracował już od kilku miesięcy”. Włącza je wyłącznie
/// administrator platformy w Portalu, i tylko na kontach oznaczonych jako testowe. Liczby są celowo
/// realistyczne (frekwencja, odwołania, płatności), żeby zobaczyć, jak aplikacja zachowuje się pod obciążeniem.
/// </summary>
public sealed record DemoScenario(
    string Key,
    string Name,
    string Description,
    int Months,
    int Clients,
    /// <summary>Średnio treningów z trenerem w tygodniu na aktywnego klienta.</summary>
    double VisitsPerWeek,
    double CancelRate,
    double LateCancelRate,
    double NoShowRate,
    /// <summary>Część pakietów i wizyt poza pakietem, które zostają niezapłacone.</summary>
    double UnpaidRate,
    /// <summary>Część klientów, którzy w trakcie odeszli (ostatnia wizyta dawno temu).</summary>
    double ChurnRate,
    /// <summary>Część klientów ćwiczących też samodzielnie z planu w aplikacji.</summary>
    double AppWorkoutRate,
    double SurveyRate,
    /// <summary>Dodatkowi trenerzy w zespole (gdy pakiet pozwala).</summary>
    int TeamTrainers = 0,
    /// <summary>Ostatnie tygodnie wyraźnie gorsze: więcej odwołań, słabsze samopoczucie, zaległości.</summary>
    bool Downturn = false);

public static class DemoScenarios
{
    public static readonly IReadOnlyList<DemoScenario> All =
    [
        new("start", "Pierwszy miesiąc",
            "Świeży trener: kilku klientów, pierwsze spotkania i pakiety, mało historii. Sprawdza puste stany i podpowiedzi „pierwszego tygodnia”.",
            Months: 1, Clients: 8, VisitsPerWeek: 1.4, CancelRate: 0.06, LateCancelRate: 0.02, NoShowRate: 0.02,
            UnpaidRate: 0.15, ChurnRate: 0.0, AppWorkoutRate: 0.25, SurveyRate: 0.5),
        new("rozwoj", "Pół roku pracy",
            "Typowy trener po 6 miesiącach: stali klienci w seriach, odnawiane pakiety, plany i treningi w aplikacji, ankiety, kilka odejść.",
            Months: 6, Clients: 28, VisitsPerWeek: 1.8, CancelRate: 0.08, LateCancelRate: 0.03, NoShowRate: 0.02,
            UnpaidRate: 0.08, ChurnRate: 0.18, AppWorkoutRate: 0.5, SurveyRate: 0.65),
        new("pelny", "Pełny grafik",
            "Rok intensywnej pracy: ponad 50 klientów, 40+ wizyt tygodniowo, karnety, kursy, kupony, vouchery, opinie. Test wydajności list, wykresów i finansów.",
            Months: 11, Clients: 55, VisitsPerWeek: 2.1, CancelRate: 0.07, LateCancelRate: 0.025, NoShowRate: 0.015,
            UnpaidRate: 0.05, ChurnRate: 0.22, AppWorkoutRate: 0.6, SurveyRate: 0.7),
        new("studio", "Studio z zespołem",
            "Właściciel i dwóch trenerów: wspólny grafik, klienci podzieleni między trenerów, raporty i uprawnienia zespołu.",
            Months: 8, Clients: 70, VisitsPerWeek: 1.9, CancelRate: 0.08, LateCancelRate: 0.03, NoShowRate: 0.02,
            UnpaidRate: 0.07, ChurnRate: 0.2, AppWorkoutRate: 0.5, SurveyRate: 0.6, TeamTrainers: 2),
        new("kryzys", "Trudny okres",
            "Pięć dobrych miesięcy i słabe ostatnie 6 tygodni: odwołania, nieobecności, zaległe płatności, cisza u klientów, ból w ankietach. Test retencji, „Kto mi zalega” i asystenta.",
            Months: 5, Clients: 26, VisitsPerWeek: 1.7, CancelRate: 0.1, LateCancelRate: 0.05, NoShowRate: 0.04,
            UnpaidRate: 0.18, ChurnRate: 0.3, AppWorkoutRate: 0.4, SurveyRate: 0.6, Downturn: true)
    ];

    public static DemoScenario? Find(string? key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>Adresy demo: zarezerwowana domena „.invalid” — nic na nie nie wyjdzie (RFC 2606).</summary>
    public const string EmailDomain = "demo.invalid";

    public static bool IsDemoEmail(string? email) =>
        email is not null && email.EndsWith("@" + EmailDomain, StringComparison.OrdinalIgnoreCase);

    public static readonly string[] FemaleNames =
    [
        "Anna", "Katarzyna", "Magdalena", "Agnieszka", "Joanna", "Monika", "Ewa", "Karolina", "Natalia", "Aleksandra", "Paulina", "Justyna",
        "Marta", "Weronika", "Zuzanna", "Julia", "Oliwia", "Dominika", "Beata", "Patrycja", "Sylwia", "Izabela", "Klaudia", "Emilia"
    ];

    public static readonly string[] MaleNames =
    [
        "Piotr", "Krzysztof", "Tomasz", "Paweł", "Michał", "Marcin", "Jakub", "Łukasz", "Adam", "Mateusz", "Bartosz", "Kamil",
        "Rafał", "Grzegorz", "Damian", "Szymon", "Wojciech", "Daniel", "Sebastian", "Marek", "Hubert", "Filip", "Igor", "Artur"
    ];

    /// <summary>Nazwiska w formie męskiej; formę żeńską tworzy <see cref="Surname"/>.</summary>
    public static readonly string[] Surnames =
    [
        "Nowak", "Kowalski", "Wiśniewski", "Wójcik", "Kowalczyk", "Kamiński", "Lewandowski", "Zieliński", "Szymański", "Woźniak",
        "Dąbrowski", "Kozłowski", "Jankowski", "Mazur", "Kwiatkowski", "Krawczyk", "Piotrowski", "Grabowski", "Nowakowski", "Pawłowski",
        "Michalski", "Król", "Wieczorek", "Jabłoński", "Wróbel", "Majewski", "Olszewski", "Stępień", "Malinowski", "Jaworski",
        "Adamczyk", "Dudek", "Nowicki", "Pawlak", "Górski", "Witkowski", "Walczak", "Sikora", "Baran", "Rutkowski"
    ];

    public static string Surname(string male, bool female) =>
        !female ? male
        : male.EndsWith("ski") ? male[..^3] + "ska"
        : male.EndsWith("cki") ? male[..^3] + "cka"
        : male.EndsWith("dzki") ? male[..^4] + "dzka"
        : male;

    public static readonly string[] Goals =
    [
        "Schudnąć 8 kg do lata", "Redukcja tkanki tłuszczowej", "Budowa masy mięśniowej", "Poprawa kondycji", "Przebiec półmaraton",
        "Zdrowy kręgosłup i mniej bólu pleców", "Silniejszy martwy ciąg", "Powrót do formy po ciąży", "Ogólna sprawność", "Sylwetka na wesele",
        "Rehabilitacja po kontuzji kolana", "Więcej energii w ciągu dnia", "Przygotowanie do sezonu narciarskiego", "Pierwsze podciągnięcie na drążku"
    ];

    public static readonly string[] TrainerNotes =
    [
        "Dobra technika przysiadu, pracujemy nad głębokością.", "Zgłasza napięcie w odcinku lędźwiowym — więcej mobilizacji na rozgrzewce.",
        "Świetna motywacja, chce zwiększyć częstotliwość.", "Słabszy sen w tym tygodniu, lżejszy trening.", "Pierwszy raz 3 serie podciągania z gumą!",
        "Prosi o plan na wyjazd — tylko masa ciała.", "Dieta: rozmowa o śniadaniach, wysłać listę przepisów.", "Kolano bez bólu, wracamy do wykroków.",
        "Spóźnienie 10 minut — skrócony trening.", "Cel na następny miesiąc: 5 km bez zatrzymania."
    ];

    public static readonly string[] ClientComments =
    [
        "Ciężko, ale super!", "Dziś bardzo zmęczony po pracy.", "Kolano trochę dawało znać przy wykrokach.", "Najlepszy trening w tym miesiącu.",
        "Mogłoby być więcej ćwiczeń na brzuch.", "Plecy ok, rozciąganie pomogło.", "Za krótka przerwa między seriami.", "Czuję postęp!",
        "Słabo spałam, ale dałam radę.", "Bark lekko boli przy wyciskaniu nad głowę."
    ];

    public static readonly string[] ChatFromClient =
    [
        "Dzień dobry, czy możemy przełożyć czwartek na 18:00?", "Dziękuję za dzisiejszy trening!", "Czy mogę ćwiczyć z lekkim przeziębieniem?",
        "Jaki ciężar na przysiadzie w domu?", "Spóźnię się ok. 10 minut, przepraszam.", "Czy jest jeszcze termin w sobotę rano?",
        "Zrobiłem cały plan z aplikacji 💪", "Kiedy kończy się mój pakiet?"
    ];

    public static readonly string[] ChatFromTrainer =
    [
        "Jasne, czwartek 18:00 zapisany.", "Brawo, świetna robota!", "Lepiej odpuść dziś, odpoczynek też jest treningiem.",
        "Zacznij od 20 kg i 3×10, zobaczymy na następnym treningu.", "Nie ma problemu, czekam.", "Sobota 9:00 jest wolna — zapisać?",
        "Super, widzę wpisy — ciężary idą w górę!", "Zostały 2 treningi, przygotuję propozycję kolejnego pakietu."
    ];

    public static readonly string[] Reviews =
    [
        "Profesjonalne podejście i plan dopasowany do moich możliwości.", "Po 3 miesiącach -7 kg i dużo więcej energii. Polecam!",
        "Trener tłumaczy każdy ruch, czuję się bezpiecznie.", "Elastyczne terminy i świetna atmosfera.", "Wreszcie bez bólu pleców po pracy przy biurku."
    ];

    /// <summary>Losowanie z rozkładem Poissona — liczba zdarzeń w okresie (np. wizyt w tygodniu).</summary>
    public static int Poisson(Random rng, double mean)
    {
        if (mean <= 0) return 0;
        var l = Math.Exp(-mean);
        var k = 0;
        var p = 1.0;
        do { k++; p *= rng.NextDouble(); } while (p > l);
        return k - 1;
    }

    public static T Pick<T>(Random rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    /// <summary>Godzina wizyty w typowym rytmie trenera: rano i po pracy najwięcej.</summary>
    public static int VisitHour(Random rng)
    {
        int[] hours = [7, 8, 9, 10, 11, 12, 16, 17, 17, 18, 18, 18, 19, 19, 20];
        return hours[rng.Next(hours.Length)];
    }
}
