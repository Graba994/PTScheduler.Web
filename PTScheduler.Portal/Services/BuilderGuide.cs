using System.Globalization;

namespace PTScheduler.Portal.Services;

/// <summary>
/// Asystent w kreatorze „Zbuduj swoją aplikację”: co powiedzieć na danym kroku i po tym, co już wpisano.
/// Teksty z reguł (bez AI) — reagują od razu na każdą zmianę i nic nie kosztują przy ruchu z reklam.
/// Druga osoba w formach neutralnych płciowo; asystent mówi o sobie w czasie teraźniejszym albo przyszłym.
/// </summary>
public static class BuilderGuide
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public enum Phase { Build, Verify, Card, Deploying, Done, Queued }

    /// <param name="Host">Adres aplikacji, np. annafit.hantly.pl.</param>
    /// <param name="SlugState">„free”, „taken” albo puste (jeszcze nie sprawdzono).</param>
    /// <param name="FirstPrice">Cena pierwszego treningu — do porównania z pakietem.</param>
    /// <param name="PasswordScore">0 — puste, 1 — za słabe, 2–4 — dobre do mocnego.</param>
    /// <param name="DeployStep">Krok publikacji, który właśnie trwa.</param>
    public sealed record State(
        Phase Phase, int Step, bool AdminMode, string AssistantName,
        string Company, string? FirstName, bool FullNameOk, string Host, string SlugState, bool WantDomain,
        string? SpecName, string ColorName, string TemplateName, bool TemplateRecommended,
        int OfferCount, string? FreeOffer, decimal FirstPrice, bool WithPackage, int PkgCount, decimal PkgPrice,
        bool EmailFilled, bool EmailValid, int PasswordScore, string? PlanName, bool Consent,
        string? Error, string Email, string? DeployStep);

    /// <param name="Mood">Nastrój maskotki: hello, happy, think, remind, celebrate.</param>
    public sealed record Line(string Mood, string Text);

    public static Line Say(State s)
    {
        if (s.Error is not null && s.Phase is Phase.Build or Phase.Verify or Phase.Card)
            return new("remind", "Coś trzeba poprawić — szczegóły są pod formularzem. Spokojnie, nic się nie zgubiło.");

        return s.Phase switch
        {
            Phase.Verify => new("think", $"Sprawdź skrzynkę {s.Email} — wysłaliśmy 6-cyfrowy kod. Nie widać go? Zajrzyj do spamu albo do zakładki „Oferty”."),
            Phase.Card => new("think", "To tylko zabezpieczenie przed automatami. Szczegóły płatności są poniżej, a po potwierdzeniu od razu stawiam aplikację."),
            Phase.Deploying => new("happy", $"Stawiam {Company(s)}{(s.DeployStep is { Length: > 0 } d ? $" — teraz: {Lower(d)}" : "")}. Możesz zamknąć kartę, dam znać e-mailem."),
            Phase.Done => new("celebrate", s.AdminMode
                ? $"Gotowe! {Company(s)} działa pod {s.Host}. Trener dostał e-mail z linkiem do ustawienia hasła."
                : $"Gotowe! {Company(s)} działa pod {s.Host}. Na dobry start dodaj pierwszego klienta i udostępnij mu link do aplikacji."),
            Phase.Queued => new("think", "Aplikacja jest zbudowana i czeka w kolejce. Uruchomimy ją i damy znać e-mailem — ustawienia z kreatora są zapisane."),
            _ => Build(s),
        };
    }

    private static Line Build(State s) => s.Step switch
    {
        1 => Name(s),
        2 => s.SpecName is null
            ? new("think", "Czym się zajmujesz? Na tej podstawie dobiorę kolor, styl strony i gotową ofertę — wszystko zmienisz jednym kliknięciem.")
            : new("happy", $"{s.SpecName} — świetnie! Dobieram kolor, styl strony i ofertę pod tę specjalizację. Zobacz podgląd, a potem kliknij „Dalej”."),
        3 => new("happy",
            $"Kolor {Lower(s.ColorName)} i styl „{s.TemplateName}”{(s.TemplateRecommended && s.SpecName is not null ? $" (polecany dla: {Lower(s.SpecName)})" : "")}. " +
            $"Tak zobaczą Cię klienci pod {s.Host} — podgląd zmienia się od razu."),
        4 => Offer(s),
        _ => Account(s),
    };

    private static Line Name(State s)
    {
        var who = s.AdminMode ? "trenera" : "Twojej";
        if (s.Company.Trim().Length < 2)
            return new("hello", s.AdminMode
                ? $"Cześć! Jestem {s.AssistantName}. Zbudujemy razem aplikację dla trenera — zacznij od nazwy studia."
                : $"Cześć! Jestem {s.AssistantName} i przeprowadzę Cię przez budowę {who} aplikacji. To zajmie około minuty — zacznij od nazwy studia.");
        if (s.WantDomain)
            return new("think", $"Własną domenę podłączysz po publikacji — pokażę, jaki rekord DNS dodać. Do tego czasu aplikacja działa pod {s.Host}.");
        if (!s.FullNameOk)
            return new("happy", s.SlugState == "taken"
                ? $"Ten adres jest już zajęty, więc dostaniesz {s.Host}. Teraz jeszcze imię i nazwisko — pojawi się na profilu trenera."
                : $"„{s.Company.Trim()}” — brzmi świetnie! Adres {s.Host} jest {(s.SlugState == "free" ? "wolny" : "prawie gotowy")}. Teraz jeszcze imię i nazwisko — pojawi się na profilu trenera.");
        return new("celebrate", s.FirstName is { Length: > 0 } first && !s.AdminMode
            ? $"Miło Cię poznać, {first}! Kliknij „Dalej” albo naciśnij Enter."
            : "Wygląda dobrze! Kliknij „Dalej” albo naciśnij Enter.");
    }

    private static Line Offer(State s)
    {
        if (s.OfferCount == 0)
            return new("think", "Dodaj co najmniej jeden rodzaj treningu — nazwę, czas i cenę. Resztę oferty uzupełnisz później w aplikacji.");
        // Darmowa konsultacja albo lekcja próbna to celowy wybór — ostrzegamy tylko przy zwykłym treningu za 0 zł.
        if (s.FreeOffer is { } free && !IsIntro(free))
            return new("remind", $"„{free}” ma cenę 0 zł. Jeśli to darmowe spotkanie — w porządku, inaczej wpisz cenę.");
        if (!s.WithPackage)
            return new("think", "Wskazówka: pakiet treningów to płatność z góry — mniej odwołanych wizyt i żadnego gonienia przelewów. Włączysz go jednym kliknięciem poniżej.");
        if (s.PkgCount >= 2 && s.PkgPrice > 0 && s.FirstPrice > 0)
        {
            var per = s.PkgPrice / s.PkgCount;
            var saving = (int)Math.Round((1 - per / s.FirstPrice) * 100);
            if (saving >= 3)
                return new("happy", $"Pakiet {s.PkgCount} treningów za {Money(s.PkgPrice)} to {saving}% taniej dla klienta — taki rabat dobrze zachęca do płacenia z góry.");
            if (saving < 0)
                return new("remind", $"Trening w pakiecie wychodzi drożej niż pojedynczy ({Money(per)} zamiast {Money(s.FirstPrice)}) — warto obniżyć cenę pakietu.");
        }
        return new("happy", "Oferta wygląda dobrze. Ceny i treningi zmienisz w każdej chwili w aplikacji.");
    }

    private static Line Account(State s)
    {
        if (!s.EmailFilled)
            return new("happy", s.AdminMode
                ? "Ostatni krok: e-mail trenera — wyślę na niego link do ustawienia hasła."
                : "Ostatni krok! Podaj e-mail — tym adresem zalogujesz się do aplikacji, a najpierw przyjdzie na niego kod.");
        if (!s.EmailValid)
            return new("remind", "Ten e-mail wygląda na niepełny — sprawdź, czy jest „@” i domena, np. ty@gmail.com.");
        if (!s.AdminMode)
        {
            if (s.PasswordScore == 0) return new("think", "Teraz hasło: co najmniej 8 znaków, w tym litera i cyfra.");
            if (s.PasswordScore == 1) return new("remind", "Hasło jest jeszcze za słabe — potrzeba co najmniej 8 znaków, litery i cyfry.");
        }
        var plan = s.PlanName is { Length: > 0 } p ? $"Plan {p} zmienisz w każdej chwili. " : "";
        if (!s.AdminMode && !s.Consent)
            return new("think", $"{plan}Zostało zaakceptować regulamin — i publikujemy!");
        return new("celebrate", s.AdminMode
            ? $"{plan}Wszystko gotowe — kliknij „Zbuduj i uruchom”."
            : $"{plan}Wszystko gotowe! Kliknij „Opublikuj aplikację”, a resztę zrobię za Ciebie.");
    }

    private static bool IsIntro(string name) =>
        new[] { "konsultac", "próbn", "probn", "pierwsz", "zapozn", "pomiar", "darmow", "bezpłat" }.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string Company(State s) => string.IsNullOrWhiteSpace(s.Company) ? "Twoją aplikację" : s.Company.Trim();
    private static string Money(decimal v) => v.ToString("N0", Pl) + " zł";
    private static string Lower(string s) => s.Length == 0 ? s : char.ToLower(s[0], Pl) + s[1..];
}
