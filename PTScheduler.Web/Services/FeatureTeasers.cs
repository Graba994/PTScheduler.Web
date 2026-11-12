namespace PTScheduler.Web.Services;

/// <summary>
/// Zajawki zablokowanych funkcji: tytuł, jedno zdanie i trzy konkretne korzyści — językiem trenera, bez wykrzykników.
/// Pokazujemy je na zablokowanym ekranie zamiast suchego „niedostępne w planie”.
/// </summary>
public static class FeatureTeasers
{
    public sealed record Teaser(string Icon, string Title, string Lead, string[] Points);

    private static readonly Dictionary<string, Teaser> All = new()
    {
        ["TrainingPlansEnabled"] = new("bi-clipboard-pulse", "Plany treningowe w aplikacji", "Klient ćwiczy z Twoim planem w telefonie, a Ty widzisz, co zrobił.",
            ["Plan z katalogu ćwiczeń po polsku, z filmami", "Trening na żywo: serie, ciężary i przerwy zapisują się same", "Podpowiedzi progresji z oceny wysiłku po treningu"]),
        ["SalesTools"] = new("bi-arrow-repeat", "Karnety i vouchery", "Stały przychód co miesiąc, bez przypominania klientom o płatności.",
            ["Karnety odnawiane automatycznie", "Bony podarunkowe, które klienci kupują znajomym", "Wszystko rozlicza się w aplikacji"]),
        ["Automations"] = new("bi-magic", "Automatyzacje", "Wiadomości, które wysyłają się same — klienci wracają częściej.",
            ["Seria powitalna dla nowych klientów", "„Dawno Cię nie było” po przerwie, z kuponem", "Życzenia urodzinowe i lista klientów, którzy mogą odejść"]),
        ["Surveys"] = new("bi-clipboard2-pulse", "Ankiety dla klientów", "Wiesz, jak klient się czuje, zanim przyjdzie na trening.",
            ["Gotowe ankiety: samopoczucie, cele, opinia", "Odpowiedzi od razu w profilu klienta", "Wnioski i zmiany w czasie"]),
        ["Documents"] = new("bi-file-earmark-check", "Dokumenty do podpisu", "Regulamin i zgody bez papieru i skanów.",
            ["Klient podpisuje w aplikacji jednym kliknięciem", "Widzisz, kto podpisał, a kto jeszcze nie", "Dokumenty zawsze pod ręką w profilu"]),
        ["CoursesEnabled"] = new("bi-mortarboard", "Kursy wideo", "Zarabiasz także wtedy, gdy nie prowadzisz treningu.",
            ["Programy online z lekcjami wideo", "Sprzedaż i dostęp dla klientów w aplikacji", "Własny odtwarzacz bez reklam"]),
        ["Coupons"] = new("bi-tag", "Kody rabatowe", "Promocje, które przyciągają nowych klientów.",
            ["Kod na start albo na święta", "Limit użyć i data ważności", "Widzisz, ile osób skorzystało"]),
        ["ReferralProgram"] = new("bi-gift", "Program poleceń", "Klienci przyprowadzają znajomych, a aplikacja sama ich nagradza.",
            ["Osobisty link polecający dla każdego klienta", "Nagroda po pierwszym treningu poleconej osoby", "Lista poleceń i nagród w jednym miejscu"]),
        ["AdvancedAnalytics"] = new("bi-bar-chart-line", "Statystyki studia", "Widzisz, jak naprawdę idzie biznes.",
            ["Kto trenuje regularnie, a kto odpuszcza", "Obłożenie grafiku i prognoza przychodu", "Które pakiety sprzedają się najlepiej"]),
        ["FinancialReports"] = new("bi-graph-up-arrow", "Finanse i paczka dla księgowej", "Pieniądze w porządku, bez arkusza w Excelu.",
            ["Przychody, sprzedane pakiety i zaległości", "Zestawienie dla księgowej jednym kliknięciem", "Przypomnienia o zaległych płatnościach"]),
        ["ClientReports"] = new("bi-file-earmark-pdf", "Raporty dla klientów", "Podsumowanie postępów, które klient chętnie pokaże znajomym.",
            ["Raport PDF z Twoim logo", "Treningi, pomiary i rekordy z miesiąca", "Wysyłka do klienta jednym kliknięciem"]),
        ["DataExport"] = new("bi-download", "Eksport danych", "Wszystkie dane studia u Ciebie, kiedy chcesz.",
            ["Klienci, wizyty i płatności w CSV", "Gotowe do arkusza albo dla księgowej", "Pełna kopia w formacie JSON"]),
        ["IntegrationGoogleMeet"] = new("bi-camera-video", "Kalendarz Google i Meet", "Jeden kalendarz zamiast dwóch.",
            ["Wizyty od razu w Twoim Kalendarzu Google", "Prywatne wydarzenia blokują terminy w aplikacji", "Link do spotkania online dołącza się sam"]),
        ["PushNotifications"] = new("bi-bell", "Powiadomienia na telefonie", "Przypomnienia i wiadomości jak z każdej aplikacji — za darmo.",
            ["Przypomnienie przed treningiem prosto na telefon", "Wiadomości od Ciebie od razu u klienta", "Mniej nieobecności bez SMS-ów"]),
        ["SmsReminders"] = new("bi-chat-dots", "Przypomnienia SMS", "Dla klientów, którzy nie mają aplikacji.",
            ["SMS dzień przed treningiem", "Tylko gdy powiadomienie w aplikacji nie dotrze", "Mniej pustych okienek w grafiku"]),
        ["CustomEmailTemplates"] = new("bi-envelope-heart", "E-maile Twoim językiem", "Wiadomości do klientów brzmią jak Ty.",
            ["Własne powitanie i przypomnienia", "Imię klienta i szczegóły wizyty w treści", "Podgląd przed zapisaniem"]),
        ["CustomLogo"] = new("bi-palette", "Twoje logo i kolory", "Klienci widzą aplikację Twojego studia.",
            ["Logo i kolory w całej aplikacji", "Twoja ikona na ekranie telefonu klienta", "Spójna marka w e-mailach"]),
        ["CustomFavicon"] = new("bi-app", "Ikona aplikacji", "Twoja ikona na ekranie telefonu klienta.",
            ["Ikona po instalacji aplikacji", "Ikona w karcie przeglądarki", "Marka studia zamiast domyślnej"]),
        ["RoleBasedAccess"] = new("bi-people", "Zespół w studiu", "Trenerzy i asystenci, każdy z własnym dostępem.",
            ["Trenerzy z własnym grafikiem i klientami", "Asystent albo recepcja z ograniczonym dostępem", "Decydujesz, co każdy może zobaczyć"]),
        ["AuditLog"] = new("bi-journal-text", "Historia zmian", "Wiesz, kto i co zmienił.",
            ["Każda zmiana w grafiku i płatnościach", "Kto, kiedy i z jakiego konta", "Spokój przy pracy z zespołem"]),
        ["PaymentsEnabled"] = new("bi-credit-card", "Płatności online", "Pieniądze od razu na Twoim koncie, bez przypominania.",
            ["BLIK, karta i przelew przy zakupie pakietu", "Twoje konto PayU albo Przelewy24 — bez prowizji platformy", "Pakiet aktywuje się sam po wpłacie"]),
        ["BodyMeasurements"] = new("bi-rulers", "Pomiary i zdjęcia sylwetki", "Postępy klienta czarno na białym.",
            ["Wymiary, waga i skład ciała", "Zdjęcia sylwetki w czasie", "Wykresy, które motywują"]),
        ["RecurringSessions"] = new("bi-repeat", "Serie wizyt", "Stałe terminy klienta ustawione raz na cały miesiąc.",
            ["Wizyty co tydzień o tej samej porze", "Zmiana całej serii jednym ruchem", "Grafik na miesiąc w minutę"]),
        ["Wearables"] = new("pt-ico-watch", "Zegarki klientów (Garmin)", "Gotowość klienta, zanim przyjdzie na trening.",
            ["Sen, HRV i Body Battery przed wizytą", "Plan wysyłany prosto na zegarek", "Obciążenie treningowe z ostatnich tygodni"]),
        ["AiAssistant"] = new("bi-stars", "Asystent AI", "Mniej pisania i planowania — Ty decydujesz, AI przygotowuje.",
            ["Propozycja planu z historii klienta", "Poranny brief: co dziś ważne", "Szkice wiadomości w Twoim stylu"]),
    };

    public static Teaser For(string flag, string? title = null) =>
        All.TryGetValue(flag, out var t) ? t : new("bi-stars", title ?? "Funkcja z wyższego planu", "Odblokujesz ją w wyższym planie.", []);
}
