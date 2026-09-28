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
        G(e => !e.CustomLogo, "bi-palette", "Twoje logo i kolory", "Klienci widzą aplikację Twojego studia, także ikonę na telefonie."),
        G(e => !e.FinancialReports, "bi-graph-up-arrow", "Finanse w jednym miejscu", "Przychody, sprzedane pakiety i zaległości — bez arkusza w Excelu."),
        G(e => !e.Coupons, "bi-tag", "Kupony rabatowe", "Promocje na start i na święta, które przyciągają nowych klientów."),
    ];

    private static readonly (Func<Entitlements, bool>, Benefit)[] ProGains =
    [
        G(e => !e.SmsReminders, "bi-chat-dots", "Przypomnienia SMS i push", "Klient dostaje SMS dzień przed treningiem — dużo mniej nieobecności i pustych okienek."),
        G(e => !e.TrainingPlansEnabled, "bi-clipboard-pulse", "Plany treningowe w aplikacji", "Klient ćwiczy z Twoim planem w telefonie, a Ty widzisz ciężary i postępy."),
        G(e => !e.CoursesEnabled, "bi-mortarboard", "Kursy wideo", "Sprzedajesz programy online — zarabiasz także wtedy, gdy nie prowadzisz treningu."),
        G(e => !e.ReferralProgram, "bi-gift", "Program poleceń", "Klienci przyprowadzają znajomych, a aplikacja sama przyznaje nagrody."),
        G(e => !e.AdvancedAnalytics, "bi-bar-chart-line", "Statystyki studia", "Widzisz, kto zaczyna odpuszczać — zanim zrezygnuje."),
        G(e => !e.IntegrationGoogleMeet, "bi-camera-video", "Treningi online z Google Meet", "Link do spotkania dołącza się do wizyty sam."),
        G(e => !e.CustomEmailTemplates, "bi-envelope-heart", "E-maile Twoim językiem", "Własne powitania i przypomnienia z imieniem klienta."),
        G(e => !e.DataExport, "bi-download", "Eksport danych", "Zestawienie dla księgowej jednym kliknięciem."),
    ];

    private static readonly (Func<Entitlements, bool>, Benefit)[] BusinessGains =
    [
        G(e => !e.RoleBasedAccess, "bi-people", "Zespół w studiu", "Dodajesz trenerów i asystentów i decydujesz, co każdy może zobaczyć."),
        G(e => !e.AuditLog, "bi-journal-text", "Historia zmian", "Wiesz, kto i co zmienił — spokój przy pracy z zespołem."),
        G(e => !e.ClientReports, "bi-file-earmark-pdf", "Raporty dla klientów (PDF)", "Miesięczne podsumowanie postępów, które klient chętnie pokaże znajomym."),
        G(_ => true, "bi-headset", "Priorytetowe wsparcie", "Pomagamy w pierwszej kolejności — także przy konfiguracji."),
    ];

    public static readonly IReadOnlyList<Plan> Plans =
    [
        new("start", "Start", 0, "Na spokojny początek", 3, []),
        new("starter", "Starter", 49, "Podstawowe narzędzia, żeby przyjmować klientów online", 15, StarterGains),
        new("pro", "Pro", 99, "Wszystko, żeby klienci wracali i polecali Cię dalej", 50, [.. StarterGains, .. ProGains]),
        new("studio", "Business", 199, "Studio z zespołem — bez limitów i z priorytetowym wsparciem", int.MaxValue, [.. StarterGains, .. ProGains, .. BusinessGains]),
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

    /// <summary>Czy jest jeszcze co ulepszać — tylko wtedy pokazujemy „Ulepsz” i podpowiedzi planu.</summary>
    public static bool HasUpgrade(Entitlements current) => HigherThan(current).Count > 0;

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
