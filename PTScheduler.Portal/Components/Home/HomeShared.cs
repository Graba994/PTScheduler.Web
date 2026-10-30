using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Components.Home;

/// <summary>Treści wspólne dla szablonów strony głównej (Nowa i Klasyczna): szablony stron trenera, pytania, cechy planów.</summary>
public static class HomeShared
{
    public sealed record TrainerTemplate(string Key, string Name, string For, string Url, string Accent, string Bg);

    public sealed record FaqItem(string Question, string Answer);

    /// <summary>Szablony strony trenera (zrzuty prawdziwych stron w /img/templates, odświeżane przy zmianach szablonów).</summary>
    public static readonly TrainerTemplate[] Templates =
    [
        new("personal", "Klasyka", "Uniwersalna: korzyści, cennik, wolne terminy", "jan-kowalski.pl", "#0284C7", "#f6f7fb"),
        new("transform", "Przemiana", "Efekty przed i po na pierwszym planie", "forma-z-trenerem.pl", "#c6f432", "#0b0f17"),
        new("premium", "Elegancja", "Elegancko, dla treningów wyższej półki", "studio-premium.pl", "#d6b36a", "#0d0d10"),
        new("women", "Ona", "Sylwetka i pewność siebie bez presji", "silna-ty.pl", "#db2f6f", "#fff6f8"),
        new("combat", "Ring", "Boks, kickboxing, MMA — mocno i dynamicznie", "ring-club.pl", "#e5202e", "#101010"),
        new("mindful", "Spokój", "Spokojnie i minimalistycznie", "pilates-studio.pl", "#111111", "#ffffff"),
        new("run", "Dystans", "Bieganie i triathlon: plany startowe i testy", "biegam-z-ola.pl", "#009e8f", "#f1f8f6"),
        new("physique", "Forma", "Sylwetka i kulturystyka, przygotowanie do startu", "forma-team.pl", "#3ea6ff", "#0e1114"),
        new("mama", "Mama", "Trening w ciąży i po porodzie — bezpiecznie", "silna-mama.pl", "#e2774a", "#fff6ef"),
    ];

    public static readonly FaqItem[] Faqs =
    [
        new("Czy muszę coś instalować albo mieć serwer?", "Nie. Po rejestracji dostajesz gotową aplikację pod adresem twoja-nazwa.hantly.pl. Serwery, poczta, powiadomienia i kopie zapasowe są po naszej stronie."),
        new("Czy klienci muszą pobierać aplikację ze sklepu?", "Nie. Otwierają link w telefonie i dodają aplikację do ekranu głównego — działa jak zwykła aplikacja, z ikoną i powiadomieniami."),
        new("Jak działają płatności online?", "Podpinasz swoje konto PayU albo Przelewy24. Klienci płacą BLIKIEM, kartą lub przelewem, a pieniądze trafiają bezpośrednio na Twoje konto. Nie pobieramy prowizji od Twoich płatności."),
        new("Czy mogę wystawiać faktury i wysyłać je do KSeF?", "Tak. Aplikacja tworzy faktury z danymi firmy klienta i może wysłać je do KSeF — wystarczy token z Twojego konta w KSeF."),
        new("Czy mogę podpiąć własną domenę?", "Tak (od planu Starter), np. annafit.pl. Wpisujesz ją w kreatorze, ustawiasz u dostawcy domeny jeden rekord DNS, a my sami podłączamy domenę i certyfikat HTTPS — dostaniesz e-mail, gdy zadziała."),
        new("Czy asystent AI ma dostęp do danych moich klientów?", "Asystent korzysta z danych w Twojej aplikacji, żeby wiedzieć, co jest ważne — ale do modelu AI wysyłamy tylko liczby i fakty (np. „3 treningi poza pakietem”), bez imion i danych kontaktowych. Każda liczba w odpowiedzi jest sprawdzana z faktami, a asystenta wyciszysz jednym kliknięciem."),
        new("Czy dane moich klientów są bezpieczne?", "Każdy trener ma osobną aplikację i osobną bazę danych na serwerach w Europie. Zdjęcia sylwetki i ankiety zdrowotne widzisz tylko Ty i dany klient. Klient może pobrać lub usunąć swoje dane zgodnie z RODO."),
        new("Czy przy rejestracji trzeba podać kartę?", "Przy planie darmowym nie — wystarczy potwierdzić e-mail kodem. Przy planach płatnych podpinasz kartę: pierwsza opłata dopiero po okresie próbnym, przed nią przypominamy e-mailem, a subskrypcję anulujesz w aplikacji jednym kliknięciem."),
        new("Czy mogę zacząć za darmo?", "Tak. Plan startowy jest za 0 zł i bez karty — idealny, żeby sprawdzić aplikację z pierwszymi klientami. Na plan płatny przejdziesz w aplikacji, gdy przybędzie klientów; dane zostają."),
        new("Co, jeśli zrezygnuję?", "Nie ma umów ani okresu wypowiedzenia. Przed zamknięciem konta możesz wyeksportować dane klientów.")
    ];

    public const string GarminLabel = "Zegarki klientów (Garmin)";
    public const string SmsPushLabel = "SMS-y i powiadomienia push";
    public const string PushLabel = "Powiadomienia push";

    /// <summary>
    /// Lista cech planu z jego flag — to samo, co trener dostaje w aplikacji. Garmin tylko, gdy włączony w Portalu;
    /// SMS-y tylko, gdy platforma ma bramkę SMS (inaczej same powiadomienia push).
    /// </summary>
    public static IEnumerable<(string Label, bool Included)> PlanFeatures(Plan p, bool garmin = false, bool sms = false)
    {
        yield return (p.MaxClients >= 9999 ? "Nielimitowani klienci" : $"Do {p.MaxClients} klientów", true);
        yield return ("Grafik, rezerwacje online, czat", true);
        yield return ("Przypomnienia e-mail", p.EmailReminders);
        yield return sms ? (SmsPushLabel, p.SmsReminders || p.PushNotifications) : (PushLabel, p.PushNotifications);
        yield return ("Płatności online, pakiety i karnety", p.PaymentsEnabled);
        yield return ("Plany treningowe i postępy klientów", p.TrainingPlansEnabled);
        yield return ("Pomiary i zdjęcia sylwetki", p.BodyMeasurements);
        yield return (p.CoursesEnabled && p.MaxCourses is > 0 and < 9999 ? $"{p.MaxCourses} kursów wideo" : "Kursy wideo", p.CoursesEnabled);
        yield return ("Kupony rabatowe", p.Coupons);
        yield return ("Program poleceń", p.ReferralProgram);
        yield return ("Raporty finansowe", p.FinancialReports);
        yield return ("Kalendarz Google i Meet", p.IntegrationGoogleMeet);
        if (garmin) yield return (GarminLabel, p.Wearables);
        yield return (p.BrandingTier switch
        {
            "premium" => "Pełny branding + własna domena",
            "full" => "Własne logo, kolory i domena",
            "basic" => "Własne logo i kolory",
            _ => "Twoja marka (logo, kolory)"
        }, p.BrandingTier != "preview");
        if (p.MaxTrainers > 1) yield return ($"{p.MaxTrainers} trenerów w zespole", true);
    }

    /// <summary>Plany na pierwszy rzut oka: darmowy (najtańszy), wyróżniony i najwyższy — reszta w porównaniu.</summary>
    public static List<Plan> MainPlans(IReadOnlyList<Plan> plans)
    {
        if (plans.Count <= 3) return [.. plans];
        var byPrice = plans.OrderBy(p => p.MonthlyPrice).ToList();
        var picked = new HashSet<Plan> { byPrice[0], byPrice[^1] };
        picked.Add(plans.FirstOrDefault(p => p.IsFeatured) ?? byPrice[byPrice.Count / 2]);
        return [.. plans.Where(picked.Contains)];
    }

    /// <summary>Dla kogo jest plan — jedno zdanie z jego limitów (bez ręcznego opisu w Panelu).</summary>
    public static string ForWhom(Plan p)
    {
        var clients = p.MaxClients >= 9999 ? "bez limitu klientów" : $"do {p.MaxClients} klientów";
        if (p.MaxTrainers >= 5) return $"Studio z zespołem: {(p.MaxTrainers >= 9999 ? "bez limitu trenerów" : $"do {p.MaxTrainers} trenerów")}, {clients}";
        if (p.MonthlyPrice <= 0) return $"Na start: sprawdzasz aplikację z pierwszymi klientami ({clients})";
        var team = p.MaxTrainers > 1 ? $", do {p.MaxTrainers} trenerów" : "";
        return p.PaymentsEnabled ? $"Trener z pełnym grafikiem: płatności online, {clients}{team}" : $"Trener, który rośnie: {clients}{team}";
    }

    /// <summary>
    /// Najważniejsze punkty karty planu: przy pierwszym planie pierwsze cechy, przy wyższych tylko to,
    /// czego nie ma niższy plan („Wszystko z …, a także:”) — karta zostaje krótka.
    /// </summary>
    public static (string? Lead, List<string> Points) Highlights(Plan plan, Plan? lower, int max = 5, bool garmin = false, bool sms = false)
    {
        var mine = PlanFeatures(plan, garmin, sms).Where(f => f.Included).Select(f => f.Label).ToList();
        if (lower is null) return (null, mine.Take(max).ToList());
        var theirs = PlanFeatures(lower, garmin, sms).Where(f => f.Included).Select(f => f.Label).ToHashSet();
        var extra = mine.Where(l => !theirs.Contains(l)).Take(max).ToList();
        return ($"Wszystko z planu {lower.Name}, a także:", extra);
    }

    public sealed record CompareRow(string Label, Func<Plan, string> Value);

    /// <summary>Tabela „Porównaj wszystkie plany”: ✓ / — albo wartość (liczba klientów, trenerów, kursów).</summary>
    public static readonly CompareRow[] CompareRows =
    [
        new("Klienci", p => p.MaxClients >= 9999 ? "bez limitu" : $"do {p.MaxClients}"),
        new("Trenerzy w zespole", p => p.MaxTrainers >= 9999 ? "bez limitu" : p.MaxTrainers > 1 ? p.MaxTrainers.ToString() : "—"),
        new("Grafik, rezerwacje online, czat", _ => "✓"),
        new("Przypomnienia e-mail", p => Mark(p.EmailReminders)),
        new(SmsPushLabel, p => Mark(p.SmsReminders || p.PushNotifications)),
        new(PushLabel, p => Mark(p.PushNotifications)),
        new("Płatności online, pakiety i karnety", p => Mark(p.PaymentsEnabled)),
        new("Plany treningowe i postępy", p => Mark(p.TrainingPlansEnabled)),
        new("Pomiary i zdjęcia sylwetki", p => Mark(p.BodyMeasurements)),
        new("Kursy wideo", p => !p.CoursesEnabled ? "—" : p.MaxCourses is > 0 and < 9999 ? p.MaxCourses.ToString()! : "✓"),
        new("Kupony i program poleceń", p => Mark(p.Coupons || p.ReferralProgram)),
        new("Raporty finansowe", p => Mark(p.FinancialReports)),
        new("Kalendarz Google i Meet", p => Mark(p.IntegrationGoogleMeet)),
        new(GarminLabel, p => Mark(p.Wearables)),
        new("Własna domena", p => Mark(p.BrandingTier is "full" or "premium")),
    ];

    private static string Mark(bool on) => on ? "✓" : "—";
}
