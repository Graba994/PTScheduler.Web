namespace PTScheduler.Domain.Constants;

/// <summary>Rodzaje automatycznych wiadomości i ich ustawienia startowe (trener zmienia je w Automatyzacjach).</summary>
public static class AutomationKinds
{
    public const string Welcome1 = "welcome-1";
    public const string Welcome2 = "welcome-2";
    public const string Welcome3 = "welcome-3";
    public const string WinBack = "winback";
    public const string PackageEnded = "package-ended";
    public const string Birthday = "birthday";

    public sealed record Default(
        string Kind, string Group, string Title, string Description, string Icon,
        int DelayDays, int CooldownDays, int CouponPercent, bool ViaSms,
        string Subject, string Message, string ButtonText, string LinkPath,
        /// <summary>Czy liczymy „powroty” (rezerwacja albo zakup po wiadomości).</summary>
        bool TracksReturn);

    public static readonly IReadOnlyList<Default> All =
    [
        new(Welcome1, "welcome", "Powitanie — dzień 1", "Zaraz po założeniu konta: jak umówić pierwszy trening.", "bi-hand-thumbs-up",
            0, 3650, 0, false,
            "Witaj w {Studio}!",
            "Cześć {Imie}!\nCieszę się, że trenujemy razem. W aplikacji umówisz trening, zobaczysz swoje pakiety i postępy.\nZacznij od wybrania terminu — to zajmie minutę.",
            "Umów trening", "/my", false),
        new(Welcome2, "welcome", "Powitanie — dzień 2", "Prośba o ankietę zdrowotną przed pierwszym treningiem.", "bi-clipboard2-pulse",
            2, 3650, 0, false,
            "Kilka pytań przed pierwszym treningiem",
            "Cześć {Imie}!\nZanim spotkamy się na treningu, wypełnij krótką ankietę o zdrowiu — dzięki niej dopasuję ćwiczenia bezpiecznie do Ciebie.",
            "Wypełnij ankietę", "/my/survey/zdrowie", false),
        new(Welcome3, "welcome", "Powitanie — dzień 5", "Zachęta do pierwszego pomiaru i śledzenia postępów.", "bi-rulers",
            5, 3650, 0, false,
            "Zobacz, jak zmienia się Twoje ciało",
            "Cześć {Imie}!\nWpisz swoją wagę albo obwody — za kilka tygodni zobaczysz postępy na wykresie. To najlepsza motywacja!",
            "Dodaj pomiar", "/my/measurements", false),
        new(WinBack, "retention", "Dawno Cię nie było", "Gdy klient nie ma wizyty od ustawionej liczby dni i nic nie zaplanował.", "bi-arrow-repeat",
            14, 60, 10, false,
            "{Imie}, brakuje nam Cię na treningach",
            "Cześć {Imie}!\nDawno się nie widzieliśmy — wróćmy do formy razem. Mam dla Ciebie {Rabat} rabatu z kodem {Kupon} (ważny do {WaznyDo}).",
            "Wybierz termin", "/my", true),
        new(PackageEnded, "retention", "Koniec pakietu", "Gdy pakiet się skończył, a klient nie kupił kolejnego.", "bi-box-seam",
            3, 45, 10, false,
            "Twój pakiet się skończył — trenujemy dalej?",
            "Cześć {Imie}!\nTwój pakiet właśnie się skończył. Nie trać rozpędu — z kodem {Kupon} kolejny pakiet kupisz {Rabat} taniej (do {WaznyDo}).",
            "Kup pakiet", "/packages", true),
        new(Birthday, "birthday", "Urodziny", "W dniu urodzin klienta — życzenia z prezentem.", "bi-gift",
            0, 300, 15, false,
            "Wszystkiego najlepszego, {Imie}! 🎉",
            "Cześć {Imie}!\nZdrowia, siły i samych rekordów! Na urodziny mam dla Ciebie prezent: {Rabat} rabatu z kodem {Kupon} (ważny do {WaznyDo}).",
            "Odbierz prezent", "/packages", true),
    ];

    public static Default? Find(string kind) => All.FirstOrDefault(d => d.Kind == kind);
}
