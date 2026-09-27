using PTScheduler.Application.Interfaces;

namespace PTScheduler.Web.Services;

/// <summary>
/// Nawigacja klienta w kilku sekcjach zamiast kilkunastu pozycji menu.
/// Każda sekcja ma zakładki (np. Pakiety: moje / kup / karnety / bony / zamówienia),
/// więc żadna funkcja nie znika — jest tylko o jedno dotknięcie dalej.
/// Jedno źródło dla menu bocznego, dolnego paska na telefonie i zakładek nad stroną.
/// </summary>
public sealed class ClientNav(
    EntitlementService entitlements,
    IModuleSettingsService modules,
    IMarketingSettingsService marketing)
{
    /// <param name="Short">Krótszy podpis na telefon (zakładki w jednym pasku bez przewijania).</param>
    public sealed record Tab(string Href, string Label, string Icon, bool Exact = false, string? Short = null);
    public sealed record Section(string Key, string Label, string Icon, string Href, IReadOnlyList<Tab> Tabs);

    private List<Section>? _cache;
    public bool ReferralsEnabled { get; private set; }

    public async Task<IReadOnlyList<Section>> GetSectionsAsync()
    {
        if (_cache is not null) return _cache;

        var coursesEnabled = true;
        var vouchersEnabled = false;
        try { coursesEnabled = (await modules.GetAsync()).CoursesEnabled; } catch { /* domyślnie widoczne */ }
        try
        {
            var m = await marketing.GetAsync();
            vouchersEnabled = m.VouchersEnabled;
            ReferralsEnabled = entitlements.IsAllowed("ReferralProgram") && m.ReferralEnabled;
        }
        catch { /* bez bonów i poleceń */ }

        var plans = entitlements.IsAllowed("TrainingPlansEnabled");
        var measurements = entitlements.IsAllowed("BodyMeasurements");

        var sections = new List<Section>
        {
            new("start", "Start", "bi-house-fill", "/app", [new("/app", "Start", "bi-house-fill", Exact: true)]),
            new("visits", "Wizyty", "bi-calendar-check-fill", "/my",
            [
                new("/my", "Grafik", "bi-calendar-week", Exact: true),
                new("/sessions", "Historia", "bi-clock-history"),
                new("/my/stats", "Statystyki", "bi-graph-up")
            ])
        };

        var training = new List<Tab>();
        if (plans) training.Add(new("/train", "Trenuj", "bi-lightning-charge-fill"));
        if (plans) training.Add(new("/my/workouts", "Postępy", "bi-graph-up-arrow"));
        if (measurements) training.Add(new("/my/measurements", "Pomiary", "bi-rulers"));
        if (training.Count > 0)
            sections.Add(new("training", "Trening", "bi-lightning-charge-fill", training[0].Href, training));

        sections.Add(new("chat", "Wiadomości", "bi-chat-dots-fill", "/chat", [new("/chat", "Wiadomości", "bi-chat-dots-fill")]));

        var shop = new List<Tab>
        {
            new("/my/packages", "Moje pakiety", "bi-box-seam", Short: "Moje"),
            new("/packages", "Kup pakiet", "bi-bag-plus", Exact: true, Short: "Kup"),
            new("/my/memberships", "Karnety", "bi-arrow-repeat")
        };
        if (vouchersEnabled) shop.Add(new("/my/vouchers", "Bony", "bi-ticket-perforated"));
        shop.Add(new("/my/orders", "Zamówienia", "bi-receipt", Short: "Zakupy"));
        sections.Add(new("shop", "Pakiety", "bi-box-seam-fill", "/my/packages", shop));

        if (coursesEnabled)
            sections.Add(new("courses", "Kursy", "bi-mortarboard-fill", "/my/courses",
            [
                new("/my/courses", "Moje kursy", "bi-mortarboard", Short: "Moje"),
                new("/courses", "Katalog", "bi-collection-play")
            ]));

        return _cache = sections;
    }

    /// <summary>Sekcja i zakładka dla bieżącego adresu (base-relative, bez parametrów).</summary>
    public static (Section? Section, Tab? Tab) Match(IReadOnlyList<Section> sections, string path)
    {
        path = "/" + path.Split('?', '#')[0].Trim('/');
        foreach (var s in sections)
            foreach (var t in s.Tabs)
                if (string.Equals(path, t.Href, StringComparison.OrdinalIgnoreCase))
                    return (s, t);
        foreach (var s in sections)
            foreach (var t in s.Tabs)
                if (!t.Exact && path.StartsWith(t.Href + "/", StringComparison.OrdinalIgnoreCase))
                    return (s, t);
        return (null, null);
    }
}
