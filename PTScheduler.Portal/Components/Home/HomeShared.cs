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
        new("Czy muszę coś instalować albo mieć serwer?", "Nie. Po rejestracji dostajesz gotową aplikację pod adresem twoja-nazwa.hantly.pl. Serwery, poczta, SMS-y i kopie zapasowe są po naszej stronie."),
        new("Czy klienci muszą pobierać aplikację ze sklepu?", "Nie. Otwierają link w telefonie i dodają aplikację do ekranu głównego — działa jak zwykła aplikacja, z ikoną i powiadomieniami."),
        new("Jak działają płatności online?", "Podpinasz swoje konto PayU albo Przelewy24. Klienci płacą BLIKIEM, kartą lub przelewem, a pieniądze trafiają bezpośrednio na Twoje konto. Nie pobieramy prowizji od Twoich płatności."),
        new("Czy mogę wystawiać faktury i wysyłać je do KSeF?", "Tak. Aplikacja tworzy faktury z danymi firmy klienta i może wysłać je do KSeF — wystarczy token z Twojego konta w KSeF."),
        new("Czy mogę podpiąć własną domenę?", "Tak (od planu Starter), np. annafit.pl. Wpisujesz ją w kreatorze, ustawiasz u dostawcy domeny jeden rekord DNS, a my sami podłączamy domenę i certyfikat HTTPS — dostaniesz e-mail, gdy zadziała."),
        new("Czy asystent AI ma dostęp do danych moich klientów?", "Asystent korzysta z danych w Twojej aplikacji, żeby wiedzieć, co jest ważne — ale do modelu AI wysyłamy tylko liczby i fakty (np. „3 treningi poza pakietem”), bez imion i danych kontaktowych. Każda liczba w odpowiedzi jest sprawdzana z faktami, a asystenta wyciszysz jednym kliknięciem."),
        new("Czy dane moich klientów są bezpieczne?", "Każdy trener ma osobną aplikację i osobną bazę danych na serwerach w Europie. Zdjęcia sylwetki i ankiety zdrowotne widzisz tylko Ty i dany klient. Klient może pobrać lub usunąć swoje dane zgodnie z RODO."),
        new("Dlaczego przy rejestracji trzeba podać kartę?", "Żeby każda aplikacja należała do prawdziwego trenera, a serwer pracował dla Twoich klientów, nie dla kont „na próbę”. Okres próbny jest za darmo, przed pierwszą opłatą przypominamy e-mailem, a subskrypcję anulujesz w aplikacji jednym kliknięciem."),
        new("Czy mogę zacząć za darmo?", "Tak. Każdy plan zaczyna się od okresu próbnego, a plan startowy jest za 0 zł. Przy publikacji podpinasz kartę — pierwsza opłata dopiero po okresie próbnym, a zrezygnujesz w każdej chwili jednym kliknięciem."),
        new("Co, jeśli zrezygnuję?", "Nie ma umów ani okresu wypowiedzenia. Przed zamknięciem konta możesz wyeksportować dane klientów.")
    ];

    /// <summary>Lista cech planu z jego flag — to samo, co trener dostaje w aplikacji.</summary>
    public static IEnumerable<(string Label, bool Included)> PlanFeatures(Plan p)
    {
        yield return (p.MaxClients >= 9999 ? "Nielimitowani klienci" : $"Do {p.MaxClients} klientów", true);
        yield return ("Grafik, rezerwacje online, czat", true);
        yield return ("Przypomnienia e-mail", p.EmailReminders);
        yield return ("SMS-y i powiadomienia push", p.SmsReminders || p.PushNotifications);
        yield return ("Płatności online, pakiety i karnety", p.PaymentsEnabled);
        yield return ("Plany treningowe i postępy klientów", p.TrainingPlansEnabled);
        yield return ("Pomiary i zdjęcia sylwetki", p.BodyMeasurements);
        yield return (p.CoursesEnabled && p.MaxCourses is > 0 and < 9999 ? $"{p.MaxCourses} kursów wideo" : "Kursy wideo", p.CoursesEnabled);
        yield return ("Kupony rabatowe", p.Coupons);
        yield return ("Program poleceń", p.ReferralProgram);
        yield return ("Raporty finansowe", p.FinancialReports);
        yield return ("Kalendarz Google i Meet", p.IntegrationGoogleMeet);
        yield return (p.BrandingTier switch
        {
            "premium" => "Pełny branding + własna domena",
            "full" => "Własne logo, kolory i domena",
            "basic" => "Własne logo i kolory",
            _ => "Twoja marka (logo, kolory)"
        }, p.BrandingTier != "preview");
        if (p.MaxTrainers > 1) yield return ($"{p.MaxTrainers} trenerów w zespole", true);
    }
}
