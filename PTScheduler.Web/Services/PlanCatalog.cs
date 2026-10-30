namespace PTScheduler.Web.Services;

/// <summary>
/// Plany platformy (od najniższego) i to, co każdy z nich daje — językiem korzyści dla trenera.
/// Jedno źródło dla strony „Ulepsz plan”, przycisku „Ulepsz” w menu i podpowiedzi o wyższym planie:
/// kto ma najwyższy plan, nie dostaje propozycji ulepszenia.
/// </summary>
public static class PlanCatalog
{
    public sealed record Benefit(string Icon, string Title, string Text);

    public sealed record Plan(string Id, string Name, decimal Price, string Tagline, int MaxClients,
        IReadOnlyList<(Func<Entitlements, bool> Missing, Benefit Benefit)> Gains);

    // Korzyść, którą plan daje, gdy obecny plan jej nie ma.
    private static (Func<Entitlements, bool>, Benefit) G(Func<Entitlements, bool> missing, string icon, string title, string text) =>
        (missing, new Benefit(icon, title, text));

    private static readonly (Func<Entitlements, bool>, Benefit)[] StarterGains =
    [
        G(e => !e.PaymentsEnabled, "bi-credit-card", "Płatności online", "Klient płaci BLIK-iem albo kartą przy zakupie pakietu — pieniądze od razu u Ciebie, bez przypominania."),
        G(e => !e.TrainingPlansEnabled, "bi-clipboard-pulse", "Plany treningowe w aplikacji", "Klient ćwiczy z Twoim planem w telefonie, a Ty widzisz ciężary i postępy."),
        G(e => !e.CustomLogo, "bi-palette", "Twoje logo i kolory", "Klienci widzą aplikację Twojego studia, także ikonę na telefonie."),
        G(e => !e.FinancialReports, "bi-graph-up-arrow", "Finanse i paczka dla księgowej", "Przychody, sprzedane pakiety i zaległości — bez arkusza w Excelu."),
        G(e => !e.Documents, "bi-file-earmark-check", "Dokumenty do podpisu", "Regulamin i zgody klient podpisuje w aplikacji — bez papieru."),
        G(e => !e.Surveys, "bi-clipboard2-pulse", "Ankiety dla klientów", "Pytasz o samopoczucie i opinie — odpowiedzi widzisz w profilu."),
    ];

    private static readonly (Func<Entitlements, bool>, Benefit)[] ProGains =
    [
        G(e => !e.AiAssistant, "bi-stars", "Asystent AI", "Propozycja planu z historii klienta, poranny brief i szkice wiadomości — Ty decydujesz."),
        G(e => !e.Wearables, "pt-ico-watch", "Zegarki klientów (Garmin)", "Gotowość klienta przed treningiem i plan wysłany prosto na zegarek."),
        G(e => !e.SalesTools, "bi-arrow-repeat", "Karnety i vouchery", "Karnety odnawiane co miesiąc i bony na prezent — stały przychód."),
        G(e => !e.Automations, "bi-magic", "Automatyzacje", "Powitanie, „dawno Cię nie było” i urodziny wysyłają się same."),
        // Bez bramki SMS na platformie nie obiecujemy SMS-ów.
        G(e => !e.SmsReminders && e.SmsAvailable, "bi-chat-dots", "Przypomnienia SMS", "SMS dla klientów bez aplikacji — mniej nieobecności i pustych okienek."),
        G(e => !e.CoursesEnabled, "bi-mortarboard", "Kursy wideo", "Sprzedajesz programy online — zarabiasz także wtedy, gdy nie prowadzisz treningu."),
        G(e => !e.Coupons, "bi-tag", "Kupony rabatowe", "Promocje na start i na święta, które przyciągają nowych klientów."),
        G(e => !e.ReferralProgram, "bi-gift", "Program poleceń", "Klienci przyprowadzają znajomych, a aplikacja sama przyznaje nagrody."),
        G(e => !e.AdvancedAnalytics, "bi-bar-chart-line", "Statystyki studia", "Widzisz, kto zaczyna odpuszczać — zanim zrezygnuje."),
        G(e => !e.IntegrationGoogleMeet, "bi-camera-video", "Kalendarz Google i Meet", "Grafik w Twoim kalendarzu, link do spotkania dołącza się sam."),
        G(e => !e.ClientReports, "bi-file-earmark-pdf", "Raporty dla klientów (PDF)", "Miesięczne podsumowanie postępów, które klient chętnie pokaże znajomym."),
    ];

    private static readonly (Func<Entitlements, bool>, Benefit)[] BusinessGains =
    [
        G(e => !e.RoleBasedAccess, "bi-people", "Zespół w studiu", "Dodajesz trenerów i asystentów i decydujesz, co każdy może zobaczyć."),
        G(e => !e.WhiteLabel, "bi-patch-check", "Tylko Twoja marka", "Klienci nie widzą wzmianek o Hantly — aplikacja jest w pełni Twoja."),
        G(e => !e.AuditLog, "bi-journal-text", "Historia zmian", "Wiesz, kto i co zmienił — spokój przy pracy z zespołem."),
        G(e => !e.PrioritySupport, "bi-headset", "Priorytetowe wsparcie", "Pomagamy w pierwszej kolejności — z rozmową wdrożeniową."),
    ];

    // Ceny i limity dla nowych klientów (cennik 2026-11); obecni mają swoją cenę zablokowaną w Portalu.
    public static readonly IReadOnlyList<Plan> Plans =
    [
        new("start", "Start", 0, "Na spokojny początek", 3, []),
        new("starter", "Solo", 59, "Trener, który przyjmuje klientów online", 20, StarterGains),
        new("pro", "Pro", 119, "Wszystko, żeby klienci wracali i polecali Cię dalej", 75, [.. StarterGains, .. ProGains]),
        new("studio", "Business", 249, "Studio z zespołem, Twoja marka i opieka wdrożeniowa", int.MaxValue, [.. StarterGains, .. ProGains, .. BusinessGains]),
    ];

    /// <summary>Plany wyższe od obecnego (po identyfikatorze, a gdy go nie znamy — po cenie).</summary>
    public static IReadOnlyList<Plan> HigherThan(Entitlements current)
    {
        if (current.Id == "unlimited") return []; // instalacja bez planu z Portalu — wszystko odblokowane
        var index = Plans.ToList().FindIndex(p => string.Equals(p.Id, current.Id, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(p.Name, current.Name, StringComparison.OrdinalIgnoreCase));
        return index >= 0
            ? Plans.Skip(index + 1).ToList()
            : Plans.Where(p => p.Price > current.MonthlyPrice).ToList();
    }

    /// <summary>
    /// W okresie próbnym (np. Pro na start) porównujemy z planem, który zostanie potem — inaczej strona „Ulepsz”
    /// nie miałaby czego pokazać, bo okres próbny ma już wszystko.
    /// </summary>
    public static Entitlements AfterTrial(Entitlements current) =>
        current.TrialEndsAt is not { } end || end <= DateTime.UtcNow ? current : new Entitlements
        {
            Id = Plans.FirstOrDefault(p => string.Equals(p.Name, current.BasePlanName, StringComparison.OrdinalIgnoreCase))?.Id ?? "start",
            Name = current.BasePlanName ?? "Start",
            MonthlyPrice = 0,
            MaxClients = Plans.FirstOrDefault(p => string.Equals(p.Name, current.BasePlanName, StringComparison.OrdinalIgnoreCase))?.MaxClients ?? 3,
            PaymentsEnabled = false, TrainingPlansEnabled = false, CustomLogo = false, FinancialReports = false, Documents = false, Surveys = false,
            AiAssistant = false, Wearables = false, SalesTools = false, Automations = false, SmsReminders = false, CoursesEnabled = false,
            Coupons = false, ReferralProgram = false, AdvancedAnalytics = false, IntegrationGoogleMeet = false, ClientReports = false,
            RoleBasedAccess = false, WhiteLabel = false, AuditLog = false, PrioritySupport = false, SmsAvailable = current.SmsAvailable
        };

    /// <summary>Czy jest jeszcze co ulepszać — tylko wtedy pokazujemy „Ulepsz” i podpowiedzi planu.</summary>
    public static bool HasUpgrade(Entitlements current) => HigherThan(AfterTrial(current)).Count > 0;

    /// <summary>Co konkretnie zyskasz, przechodząc na dany plan (tylko rzeczy, których dziś nie masz).</summary>
    public static IReadOnlyList<Benefit> GainsFor(Plan plan, Entitlements current)
    {
        var list = new List<Benefit>();
        if (plan.MaxClients > current.MaxClients)
            list.Add(plan.MaxClients == int.MaxValue
                ? new("bi-infinity", "Bez limitu klientów", "Studio może rosnąć bez pilnowania liczby kont.")
                : new("bi-people-fill", $"Do {plan.MaxClients} klientów", $"Miejsce na {plan.MaxClients - Math.Min(current.MaxClients, plan.MaxClients)} klientów więcej niż dziś."));
        foreach (var (missing, benefit) in plan.Gains)
            if (missing(current) && list.All(b => b.Title != benefit.Title)) list.Add(benefit);
        return list;
    }
}
