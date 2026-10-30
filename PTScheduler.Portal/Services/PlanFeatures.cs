using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Portal.Data;
using PTScheduler.Portal.Entities;

namespace PTScheduler.Portal.Services;

public enum PlanFeatureKind { Flag, Limit, Choice }

/// <summary>Jedna funkcja aplikacji w planach: pole planu, nazwa i korzyść językiem trenera.</summary>
/// <param name="Key">Nazwa pola w <see cref="Plan"/> (i w uprawnieniach studia).</param>
/// <param name="Benefit">Jedno zdanie do zajawki i cennika.</param>
/// <param name="Unit">Jednostka limitu (np. „klientów”).</param>
/// <param name="Options">Wartości wyboru od najsłabszej (np. branding).</param>
public sealed record PlanFeature(string Key, string Group, string Name, string Benefit, PlanFeatureKind Kind = PlanFeatureKind.Flag,
    string? Unit = null, string[]? Options = null)
{
    /// <summary>Ikona Bootstrap Icons (albo pt-ico-*) — w cenniku i na stronie „Ulepsz plan”.</summary>
    public string Icon => PlanFeatureCatalog.Icons.GetValueOrDefault(Key, "bi-check2-circle");

    /// <summary>Warto pokazać w cenniku i zajawkach (techniczne drobiazgi jak bramki płatności czy 2FA — nie).</summary>
    public bool Pitch => !PlanFeatureCatalog.NotPitched.Contains(Key);
}

/// <summary>
/// Katalog funkcji — jedno źródło dla panelu planów (macierz), cennika i uprawnień studiów. Nowa funkcja w aplikacji
/// = nowe pole w <see cref="Plan"/> i jeden wpis tutaj.
/// </summary>
public static class PlanFeatureCatalog
{
    public const int Unlimited = int.MaxValue;

    public static readonly string[] Groups =
        ["Klienci i grafik", "Trening", "Sprzedaż i płatności", "Komunikacja", "AI", "Marka", "Raporty i księgowość", "Zespół i bezpieczeństwo", "Miejsce na dane"];

    public static readonly PlanFeature[] All =
    [
        new("MaxClients", "Klienci i grafik", "Klienci", "Aktywni klienci w aplikacji.", PlanFeatureKind.Limit, "klientów"),
        new("MaxSessionsPerMonth", "Klienci i grafik", "Wizyty w miesiącu", "Ile wizyt zapiszesz w grafiku w miesiącu.", PlanFeatureKind.Limit, "wizyt"),
        new("RecurringSessions", "Klienci i grafik", "Serie wizyt", "Stałe terminy klienta ustawione raz na cały miesiąc."),
        new("IntegrationGoogleMeet", "Klienci i grafik", "Kalendarz Google i Meet", "Grafik w Twoim Kalendarzu Google, link do spotkania dołącza się sam."),
        new("BodyMeasurements", "Klienci i grafik", "Pomiary i zdjęcia sylwetki", "Postępy klienta czarno na białym — wymiary, waga, zdjęcia."),
        new("Surveys", "Klienci i grafik", "Ankiety dla klientów", "Pytasz klientów o samopoczucie i opinie — odpowiedzi widzisz w profilu."),
        new("Documents", "Klienci i grafik", "Dokumenty do podpisu", "Klient podpisuje regulamin i zgody w aplikacji — bez papieru."),

        new("TrainingPlansEnabled", "Trening", "Plany treningowe i trening na żywo", "Klient ćwiczy z Twoim planem w telefonie, Ty widzisz ciężary i postępy."),
        new("Wearables", "Trening", "Zegarki klientów (Garmin)", "Gotowość klienta przed treningiem i plan wysłany prosto na zegarek."),
        new("CoursesEnabled", "Trening", "Kursy wideo", "Sprzedajesz programy online — zarabiasz także bez prowadzenia treningu."),
        new("MaxCourses", "Trening", "Liczba kursów", "Ile kursów wideo możesz prowadzić.", PlanFeatureKind.Limit, "kursów"),
        new("VideoProvider", "Trening", "Wideo", "Filmy z YouTube albo własny odtwarzacz bez reklam.", PlanFeatureKind.Choice, Options: ["none", "youtube", "bunny"]),
        new("MaxVideoStorageGB", "Trening", "Miejsce na wideo", "Filmy ćwiczeń i lekcje kursów na naszym serwerze.", PlanFeatureKind.Limit, "GB"),
        new("MaxVideoBandwidthGBPerMonth", "Trening", "Transfer wideo", "Ile wideo klienci obejrzą w miesiącu.", PlanFeatureKind.Limit, "GB/mies."),

        new("PaymentsEnabled", "Sprzedaż i płatności", "Płatności online", "BLIK i karta przy zakupie pakietu — pieniądze od razu u Ciebie."),
        new("IntegrationPayU", "Sprzedaż i płatności", "PayU", "Bramka PayU na Twoje konto."),
        new("IntegrationPrzelewy24", "Sprzedaż i płatności", "Przelewy24", "Bramka Przelewy24 na Twoje konto."),
        new("SalesTools", "Sprzedaż i płatności", "Karnety i vouchery", "Karnety odnawiane co miesiąc i vouchery na prezent — stały przychód."),
        new("Coupons", "Sprzedaż i płatności", "Kody rabatowe", "Promocje na start i na święta, które przyciągają nowych klientów."),
        new("ReferralProgram", "Sprzedaż i płatności", "Program poleceń", "Klienci przyprowadzają znajomych, a aplikacja sama przyznaje nagrody."),

        new("EmailReminders", "Komunikacja", "Przypomnienia e-mail", "Klient dostaje e-mail dzień przed treningiem."),
        new("PushNotifications", "Komunikacja", "Powiadomienia na telefonie", "Przypomnienia i wiadomości jak z każdej aplikacji — za darmo."),
        new("SmsReminders", "Komunikacja", "Przypomnienia SMS", "SMS dla klientów bez aplikacji — mniej nieobecności."),
        new("MaxSmsPerMonth", "Komunikacja", "SMS-y w cenie", "SMS-y w miesiącu w abonamencie (dodatkowe w sklepie).", PlanFeatureKind.Limit, "SMS/mies."),
        new("Automations", "Komunikacja", "Automatyzacje i retencja", "Powitanie, „dawno Cię nie było” i urodziny wysyłają się same."),
        new("CustomEmailTemplates", "Komunikacja", "E-maile Twoim językiem", "Własne powitania i przypomnienia z imieniem klienta."),

        new("AiAssistant", "AI", "Asystent AI", "Propozycja planu z historii klienta, poranny brief i szkice wiadomości — Ty decydujesz."),
        new("MaxAiActionsPerMonth", "AI", "Akcje AI w miesiącu", "Ile razy w miesiącu asystent AI coś przygotuje.", PlanFeatureKind.Limit, "akcji/mies."),

        new("BrandingTier", "Marka", "Poziom brandingu", "Jak bardzo aplikacja wygląda jak Twoja.", PlanFeatureKind.Choice, Options: ["preview", "basic", "full", "premium"]),
        new("CustomLogo", "Marka", "Twoje logo i kolory", "Klienci widzą aplikację Twojego studia."),
        new("CustomFavicon", "Marka", "Ikona aplikacji", "Twoja ikona na ekranie telefonu klienta."),
        new("CustomDomain", "Marka", "Własna domena", "Aplikacja i strona pod adresem np. twojestudio.pl."),
        new("WhiteLabel", "Marka", "Bez marki Hantly", "Klienci widzą tylko Twoją markę — bez wzmianek o Hantly."),

        new("BasicAnalytics", "Raporty i księgowość", "Podstawowe statystyki", "Wizyty i klienci w liczbach."),
        new("AdvancedAnalytics", "Raporty i księgowość", "Statystyki studia", "Kto trenuje regularnie, kto odpuszcza i co się sprzedaje."),
        new("FinancialReports", "Raporty i księgowość", "Finanse i paczka dla księgowej", "Przychody, zaległości i zestawienie dla księgowej bez Excela."),
        new("ClientReports", "Raporty i księgowość", "Raporty dla klientów (PDF)", "Miesięczne podsumowanie postępów z Twoim logo."),
        new("DataExport", "Raporty i księgowość", "Eksport danych", "Wszystkie dane w CSV/JSON jednym kliknięciem."),

        new("MaxTrainers", "Zespół i bezpieczeństwo", "Trenerzy w zespole", "Trenerzy z własnym grafikiem i klientami.", PlanFeatureKind.Limit, "trenerów"),
        new("MaxSubordinates", "Zespół i bezpieczeństwo", "Asystenci", "Recepcja albo asystent z ograniczonym dostępem.", PlanFeatureKind.Limit, "osób"),
        new("RoleBasedAccess", "Zespół i bezpieczeństwo", "Role i uprawnienia", "Decydujesz, co każdy w zespole może zobaczyć."),
        new("AuditLog", "Zespół i bezpieczeństwo", "Historia zmian", "Wiesz, kto i co zmienił."),
        new("TwoFactorAuth", "Zespół i bezpieczeństwo", "Logowanie dwuetapowe", "Kod z telefonu przy logowaniu."),
        new("PrioritySupport", "Zespół i bezpieczeństwo", "Priorytetowe wsparcie", "Pomagamy w pierwszej kolejności, z rozmową wdrożeniową."),

        new("MaxStorageGB", "Miejsce na dane", "Miejsce na pliki", "Zdjęcia, dokumenty i załączniki.", PlanFeatureKind.Limit, "GB"),
    ];

    public static readonly Dictionary<string, string> Icons = new()
    {
        ["MaxClients"] = "bi-people-fill", ["MaxSessionsPerMonth"] = "bi-calendar-check", ["RecurringSessions"] = "bi-repeat",
        ["IntegrationGoogleMeet"] = "bi-camera-video", ["BodyMeasurements"] = "bi-rulers", ["Surveys"] = "bi-clipboard2-pulse",
        ["Documents"] = "bi-file-earmark-check", ["TrainingPlansEnabled"] = "bi-clipboard-pulse", ["Wearables"] = "pt-ico-watch",
        ["CoursesEnabled"] = "bi-mortarboard", ["MaxCourses"] = "bi-mortarboard", ["VideoProvider"] = "bi-play-btn",
        ["MaxVideoStorageGB"] = "bi-camera-reels", ["MaxVideoBandwidthGBPerMonth"] = "bi-broadcast", ["PaymentsEnabled"] = "bi-credit-card",
        ["IntegrationPayU"] = "bi-credit-card-2-front", ["IntegrationPrzelewy24"] = "bi-credit-card-2-front", ["SalesTools"] = "bi-arrow-repeat",
        ["Coupons"] = "bi-tag", ["ReferralProgram"] = "bi-gift", ["EmailReminders"] = "bi-envelope", ["PushNotifications"] = "bi-bell",
        ["SmsReminders"] = "bi-chat-dots", ["MaxSmsPerMonth"] = "bi-chat-dots", ["Automations"] = "bi-magic",
        ["CustomEmailTemplates"] = "bi-envelope-heart", ["AiAssistant"] = "bi-stars", ["MaxAiActionsPerMonth"] = "bi-stars",
        ["BrandingTier"] = "bi-palette", ["CustomLogo"] = "bi-palette", ["CustomFavicon"] = "bi-app", ["CustomDomain"] = "bi-globe2",
        ["WhiteLabel"] = "bi-patch-check", ["BasicAnalytics"] = "bi-bar-chart", ["AdvancedAnalytics"] = "bi-bar-chart-line",
        ["FinancialReports"] = "bi-graph-up-arrow", ["ClientReports"] = "bi-file-earmark-pdf", ["DataExport"] = "bi-download",
        ["MaxTrainers"] = "bi-person-plus", ["MaxSubordinates"] = "bi-person-badge", ["RoleBasedAccess"] = "bi-people",
        ["AuditLog"] = "bi-journal-text", ["TwoFactorAuth"] = "bi-shield-lock", ["PrioritySupport"] = "bi-headset", ["MaxStorageGB"] = "bi-hdd",
    };

    /// <summary>Funkcje, których nie wyliczamy w cenniku i zajawkach — są wszędzie, techniczne albo opisane innym wierszem.</summary>
    public static readonly HashSet<string> NotPitched =
    [
        "MaxSessionsPerMonth", "TwoFactorAuth", "BasicAnalytics", "EmailReminders", "CustomFavicon", "IntegrationPayU", "IntegrationPrzelewy24",
        "VideoProvider", "BrandingTier", "MaxVideoBandwidthGBPerMonth", "MaxStorageGB", "MaxSubordinates", "MaxCourses", "MaxAiActionsPerMonth",
        "MaxSmsPerMonth", "MaxVideoStorageGB", "MaxClients",
    ];

    /// <summary>Kolejność w cenniku i zajawkach — najpierw to, co najbardziej przekonuje; reszta w kolejności katalogu.</summary>
    public static readonly string[] PitchOrder =
    [
        "AiAssistant", "Wearables", "TrainingPlansEnabled", "PaymentsEnabled", "SalesTools", "Automations", "SmsReminders",
        "MaxTrainers", "WhiteLabel", "CoursesEnabled", "PushNotifications", "IntegrationGoogleMeet", "CustomLogo", "CustomDomain",
        "AdvancedAnalytics", "FinancialReports", "Coupons", "ReferralProgram", "RoleBasedAccess", "PrioritySupport",
    ];

    public static int PitchRank(string key) => Array.IndexOf(PitchOrder, key) is var i and >= 0 ? i : PitchOrder.Length + Array.FindIndex(All, f => f.Key == key);

    public static PlanFeature? Find(string key) => All.FirstOrDefault(f => f.Key == key);

    private static PropertyInfo Prop(string key) =>
        typeof(Plan).GetProperty(key) ?? throw new InvalidOperationException($"Plan nie ma pola {key}.");

    public static object? Get(Plan plan, string key) => Prop(key).GetValue(plan);
    public static void Set(Plan plan, string key, object? value) => Prop(key).SetValue(plan, value);

    public static bool Has(Plan plan, PlanFeature f) => f.Kind switch
    {
        PlanFeatureKind.Flag => Get(plan, f.Key) is true,
        PlanFeatureKind.Limit => Get(plan, f.Key) is int n && n > 0,
        _ => Rank(f, Get(plan, f.Key) as string) > 0
    };

    public static int Rank(PlanFeature f, string? value) => f.Options is null || value is null ? 0 : Math.Max(0, Array.IndexOf(f.Options, value));

    public static string Describe(Plan plan, PlanFeature f) => f.Kind switch
    {
        PlanFeatureKind.Flag => Get(plan, f.Key) is true ? "✓" : "—",
        PlanFeatureKind.Limit => Get(plan, f.Key) is int n ? n >= Unlimited ? "bez limitu" : n <= 0 ? "—" : $"{n} {f.Unit}" : "—",
        _ => Get(plan, f.Key) as string ?? "—"
    };

    /// <summary>Kopia planu (bez powiązań) — do łączenia z podłogą i okresem próbnym.</summary>
    public static Plan Clone(Plan plan)
    {
        var copy = new Plan();
        foreach (var p in typeof(Plan).GetProperties().Where(p => p.CanWrite && p.Name != nameof(Plan.Tenants)))
            p.SetValue(copy, p.GetValue(plan));
        return copy;
    }

    /// <summary>
    /// Plan + podłoga studia: włącznik — gdy jest w którymkolwiek, limit — większy, wybór — mocniejszy.
    /// Ceny i nazwa zostają z planu.
    /// </summary>
    public static Plan ApplyFloor(Plan plan, string? floorJson)
    {
        if (string.IsNullOrWhiteSpace(floorJson)) return plan;
        Dictionary<string, JsonElement>? floor;
        try { floor = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(floorJson); }
        catch (JsonException) { return plan; }
        return floor is null || floor.Count == 0 ? plan : Grant(plan, floor);
    }

    /// <summary>Wyjątki studia (aktywne) — tak samo jak podłoga: dokładają, nigdy nie zabierają.</summary>
    public static Plan ApplyExceptions(Plan plan, IEnumerable<TenantFeatureException>? exceptions, DateTime utcNow)
    {
        // Po kolei — kilka wyjątków na ten sam limit daje największy, nie ostatni.
        var result = plan;
        foreach (var x in exceptions ?? [])
        {
            if (!x.IsActive(utcNow)) continue;
            try { result = Grant(result, new() { [x.Key] = JsonDocument.Parse(x.ValueJson).RootElement.Clone() }); }
            catch (JsonException) { }
        }
        return result;
    }

    /// <summary>Wartość wyjątku jako JSON dla danej funkcji (włącznik — true, limit — liczba, wybór — tekst).</summary>
    public static string ValueJson(PlanFeature f, object value) => JsonSerializer.Serialize(f.Kind switch
    {
        PlanFeatureKind.Flag => (object)true,
        PlanFeatureKind.Limit => Convert.ToInt32(value),
        _ => value.ToString() ?? ""
    });

    private static Plan Grant(Plan plan, Dictionary<string, JsonElement> values)
    {
        var byKey = new Dictionary<string, JsonElement>(values, StringComparer.OrdinalIgnoreCase);
        var result = Clone(plan);
        foreach (var f in All)
        {
            if (!byKey.TryGetValue(f.Key, out var v)) continue;
            switch (f.Kind)
            {
                case PlanFeatureKind.Flag when v.ValueKind == JsonValueKind.True:
                    Set(result, f.Key, true);
                    break;
                case PlanFeatureKind.Limit when v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n):
                    var limit = (int)Math.Clamp(n, 0, Unlimited);
                    if (limit > (int)Get(result, f.Key)!) Set(result, f.Key, limit);
                    break;
                case PlanFeatureKind.Choice when v.ValueKind == JsonValueKind.String:
                    if (Rank(f, v.GetString()) > Rank(f, Get(result, f.Key) as string)) Set(result, f.Key, v.GetString());
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Plan, z którego studio korzysta teraz: okres próbny wyższego planu, podłoga z poprzednich pakietów
    /// i wyjątki ustawione w panelu dla tego studia.
    /// </summary>
    public static Plan Effective(Tenant tenant, Plan plan, Plan? trialPlan, DateTime utcNow, IEnumerable<TenantFeatureException>? exceptions = null)
    {
        var basePlan = trialPlan is not null && tenant.TrialPlanEndsAt > utcNow ? trialPlan : plan;
        return ApplyExceptions(ApplyFloor(basePlan, tenant.FeatureFloor), exceptions, utcNow);
    }

    public static async Task<Plan?> EffectiveAsync(PortalDbContext db, Tenant tenant)
    {
        var plan = tenant.Plan ?? await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tenant.PlanId);
        if (plan is null) return null;
        var now = DateTime.UtcNow;
        var trial = tenant.TrialPlanId is { } tid && tenant.TrialPlanEndsAt > now
            ? await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == tid) : null;
        var exceptions = await db.TenantFeatureExceptions.AsNoTracking().Where(x => x.TenantId == tenant.Id && (x.ExpiresAt == null || x.ExpiresAt > now)).ToListAsync();
        return Effective(tenant, plan, trial, now, exceptions);
    }

    /// <summary>Najtańszy aktywny plan z daną funkcją — nazwa przy kłódce w aplikacji studia.</summary>
    public static Dictionary<string, string> UpgradePlans(IEnumerable<Plan> activePlans)
    {
        var ordered = activePlans.Where(p => p.IsActive).OrderBy(p => p.MonthlyPrice).ThenBy(p => p.SortOrder).ToList();
        var map = new Dictionary<string, string>();
        foreach (var f in All)
            if (ordered.FirstOrDefault(p => Has(p, f)) is { } p)
                map[f.Key] = p.Name;
        return map;
    }
}

/// <summary>Cena dla studia: zablokowana przy zakupie albo aktualna z cennika.</summary>
public static class PlanPricing
{
    public static decimal Monthly(Tenant t, Plan? plan) => t.LockedMonthlyPrice ?? plan?.MonthlyPrice ?? 0;

    public static decimal? Yearly(Tenant t, Plan? plan) => t.LockedMonthlyPrice is not null ? t.LockedYearlyPrice : plan?.YearlyPrice;

    /// <summary>Kwota za okres rozliczeniowy studia (rok albo miesiąc).</summary>
    public static decimal ForInterval(Tenant t, Plan? plan) =>
        t.BillingInterval == "yearly" && Yearly(t, plan) is > 0 and var y ? y : Monthly(t, plan);

    /// <summary>Zablokuj obecną cenę planu (przy zakupie albo zmianie planu). Darmowy plan — bez blokady.</summary>
    public static void Lock(Tenant t, Plan plan)
    {
        t.LockedMonthlyPrice = plan.MonthlyPrice > 0 ? plan.MonthlyPrice : null;
        t.LockedYearlyPrice = plan.MonthlyPrice > 0 ? plan.YearlyPrice : null;
    }
}
