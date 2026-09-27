using Microsoft.AspNetCore.Components.Authorization;
using PTScheduler.Application.Interfaces;
using PTScheduler.Domain.Constants;

namespace PTScheduler.Web.Components.Layout;

/// <summary>Pozycja ustawień trenera: menu boczne i kafelek na stronie „Zarządzanie”.</summary>
public sealed record AdminMenuItem(
    string Href,
    string Icon,
    string Title,
    /// <summary>Jedno zdanie pod tytułem kafelka.</summary>
    string Description,
    /// <summary>Dłuższe wyjaśnienie pod ikoną (i): co to jest i kiedy się przydaje.</summary>
    string Info,
    string? Permission = null,
    string? PlanFlag = null,
    string? PlanName = null,
    /// <summary>Tylko właściciel studia (administrator albo trener), nie asystent.</summary>
    bool OwnerOnly = false,
    /// <summary>Ukryte w instancji zarządzanej przez Portal (tam robi to platforma).</summary>
    bool HideWhenManaged = false,
    /// <summary>Klucz stanu wyliczanego na stronie „Zarządzanie” (np. liczba typów treningów).</summary>
    string? StatusKey = null);

/// <param name="Advanced">Rzadko używane — na stronie „Zarządzanie” schowane pod „Więcej ustawień”.</param>
public sealed record AdminMenuGroup(string Title, string Icon, string Tone, IReadOnlyList<AdminMenuItem> Items, bool Advanced = false);

/// <summary>Kto co widzi w ustawieniach (rola i uprawnienia nadane w „Uprawnieniach”).</summary>
public sealed class AdminAccess
{
    public bool IsAdmin { get; init; }
    public bool IsTrainer { get; init; }
    public HashSet<string> Granted { get; init; } = [];

    public bool IsOwner => IsAdmin || IsTrainer;
    public bool Can(string? permission) => permission is null || IsAdmin || Granted.Contains(permission);

    public bool Sees(AdminMenuItem item) =>
        Can(item.Permission)
        && (!item.OwnerOnly || IsOwner)
        // Strony dostępne tylko dla wybranych ról — bez tego kafelek prowadził do „Brak dostępu”.
        && (IsAdmin || !AdminMenu.AdminOnly.Contains(item.Href))
        && (IsOwner || !AdminMenu.AdminOrTrainer.Contains(item.Href))
        && (!item.HideWhenManaged || !PTScheduler.Infrastructure.Services.PlatformConnection.IsManaged);

    public static async Task<AdminAccess> LoadAsync(AuthenticationStateProvider authState, IPermissionService permissions)
    {
        var auth = await authState.GetAuthenticationStateAsync();
        if (auth.User.IsInRole(Roles.Admin)) return new AdminAccess { IsAdmin = true };

        var role = auth.User.IsInRole(Roles.Trainer) ? Roles.Trainer
                 : auth.User.IsInRole(Roles.Subordinate) ? Roles.Subordinate
                 : string.Empty;
        var granted = new HashSet<string>();
        if (role.Length > 0)
            foreach (var p in Permissions.All)
                if (await permissions.HasPermissionAsync(role, p.Key))
                    granted.Add(p.Key);
        return new AdminAccess { IsTrainer = role == Roles.Trainer, Granted = granted };
    }
}

/// <summary>
/// Jedna lista ustawień dla menu bocznego i strony „Zarządzanie” — nazwy i opisy językiem trenera,
/// pogrupowane według tego, czym trener się zajmuje (oferta, sprzedaż, klienci, marka…).
/// </summary>
public static class AdminMenu
{
    /// <summary>Strony z atrybutem Authorize(Roles = "Admin").</summary>
    public static readonly HashSet<string> AdminOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin/session-types", "admin/modules", "admin/email", "admin/email-templates", "admin/push", "admin/surveys",
        "admin/branding", "admin/site", "admin/users", "admin/permissions", "admin/backup", "admin/audit-logs",
        "admin/settings", "admin/demo"
    };

    /// <summary>Strony z atrybutem Authorize(Roles = "Admin,Trainer") — bez asystenta.</summary>
    public static readonly HashSet<string> AdminOrTrainer = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin/coupons", "admin/sklep", "admin/sms", "admin/google-meet", "admin/video", "admin/export"
    };

    public static readonly IReadOnlyList<AdminMenuGroup> Groups =
    [
        new("Oferta i grafik", "bi-calendar-week", "blue",
        [
            new("trainer/availability", "bi-clock-fill", "Godziny pracy",
                "Kiedy klienci mogą się zapisać",
                "Ustaw dni i godziny, w których przyjmujesz klientów. Klienci widzą w aplikacji tylko wolne terminy z tych godzin — nie musisz niczego potwierdzać ręcznie.",
                StatusKey: "availability"),
            new("admin/session-types", "bi-lightning-charge-fill", "Rodzaje treningów",
                "Nazwa, czas trwania i cena zajęć",
                "Np. „Trening personalny 60 min — 150 zł” albo „Konsultacja online 30 min”. Klient wybiera rodzaj przy rezerwacji, a aplikacja sama dopasuje długość terminu.",
                Permissions.ManageSessionTypes, StatusKey: "sessionTypes"),
            new("admin/package-offers", "bi-box-seam-fill", "Pakiety treningów",
                "Np. 10 treningów w lepszej cenie",
                "Pakiety klient kupuje online (albo dopisujesz je ręcznie). Aplikacja sama odlicza wykorzystane treningi i przypomina, gdy pakiet się kończy.",
                Permissions.ManagePackages, StatusKey: "packages"),
            new("admin/memberships", "bi-arrow-repeat", "Karnety miesięczne",
                "Stała liczba treningów co miesiąc",
                "Karnet odnawia się co miesiąc — np. 8 treningów za 640 zł. Dobre dla stałych klientów: płacą z góry, a Ty masz przewidywalny przychód.",
                Permissions.ManagePackages, StatusKey: "memberships"),
            new("admin/modules", "bi-toggles", "Funkcje aplikacji",
                "Włącz albo ukryj moduły",
                "Ukryj to, z czego nie korzystasz (np. kursy albo plany treningowe) — klienci zobaczą prostszą aplikację. Możesz to zmienić w każdej chwili.",
                Permissions.ManageBranding)
        ]),
        new("Sprzedaż i płatności", "bi-wallet2", "green",
        [
            new("admin/payments", "bi-credit-card-fill", "Płatności online",
                "BLIK, karta i przelew na Twoje konto",
                "Podłączasz swoje konto PayU albo Przelewy24 — pieniądze za pakiety i karnety trafiają prosto do Ciebie. Platforma nie pobiera prowizji od Twoich płatności.",
                Permissions.ManagePayments, "PaymentsEnabled", "Starter", StatusKey: "payments"),
            new("admin/coupons", "bi-tag-fill", "Kupony rabatowe",
                "Kody zniżkowe na pakiety",
                "Np. kod WIOSNA20 na 20% zniżki. Ustawiasz limit użyć i datę ważności — dobre na promocje i dla nowych klientów.",
                Permissions.ManagePayments, "Coupons", "Pro", StatusKey: "coupons"),
            new("admin/vouchers", "bi-ticket-perforated-fill", "Bony podarunkowe",
                "Prezent na kwotę albo pakiet",
                "Bon w PDF do wręczenia — na święta, urodziny, Dzień Matki. Obdarowana osoba realizuje go przy zakupie.",
                Permissions.ManagePayments),
            new("admin/referrals", "bi-gift-fill", "Program poleceń",
                "Nagroda za poleconego klienta",
                "Każdy klient dostaje swój link. Gdy polecona osoba odbędzie pierwszy trening, aplikacja sama przyzna nagrodę (np. darmową sesję).",
                Permissions.ManagePayments, "ReferralProgram", "Pro"),
            new("admin/sklep", "bi-shop", "Sklep usług",
                "Dokup SMS-y, miejsce na wideo, pomoc",
                "Dodatki do Twojego abonamentu: pakiety SMS, więcej miejsca i transferu na wideo, pomoc przy konfiguracji. Płacisz tylko za to, czego potrzebujesz.",
                OwnerOnly: true)
        ]),
        new("Klienci i powiadomienia", "bi-bell", "violet",
        [
            new("admin/sms", "bi-chat-dots-fill", "Przypomnienia SMS",
                "SMS dzień przed treningiem",
                "Klient dostaje SMS-a dzień przed treningiem — mniej nieobecności. SMS-y idą z limitu Twojego planu, a gdy się skończą, z dokupionych pakietów.",
                Permissions.ManageSms, "SmsReminders", "Pro"),
            new("admin/email", "bi-envelope-fill", "E-maile",
                "Nadawca i wysyłka wiadomości",
                "Potwierdzenia rezerwacji, przypomnienia i faktury wychodzą e-mailem. Tu ustawiasz, od kogo przychodzą i sprawdzasz, czy wysyłka działa.",
                Permissions.ManageEmail),
            new("admin/email-templates", "bi-file-earmark-richtext-fill", "Treść wiadomości",
                "Własne teksty e-maili",
                "Zmień treść e-maili do klientów (np. powitanie, przypomnienie) na swoją — z imieniem klienta i szczegółami treningu wstawianymi automatycznie.",
                Permissions.ManageEmail, "CustomEmailTemplates", "Pro"),
            new("admin/push", "bi-bell-fill", "Powiadomienia push",
                "Powiadomienia na telefonie klienta",
                "Klient, który zainstalował aplikację na telefonie, dostaje powiadomienia jak z każdej innej aplikacji — o treningu, wiadomości czy płatności.",
                Permissions.ManageEmail, "PushNotifications", "Pro"),
            new("admin/surveys", "bi-clipboard2-pulse", "Ankiety",
                "Wywiad zdrowotny i ocena treningu",
                "Ankieta zdrowotna przed pierwszym treningiem (ze zgodą RODO) i krótka ocena po treningu. Odpowiedzi widzisz w profilu klienta.",
                Permissions.ManageBranding),
            new("admin/reviews", "bi-star-half", "Opinie klientów",
                "Zbieraj opinie na swoją stronę",
                "Aplikacja poprosi klienta o opinię po kilku treningach i podsunie link do opinii w Google. Wybrane opinie pokażesz na swojej stronie.",
                Permissions.ManageBranding)
        ]),
        new("Twoja marka", "bi-palette", "pink",
        [
            new("admin/branding", "bi-palette-fill", "Wygląd",
                "Logo, kolory i nazwa aplikacji",
                "Klienci widzą aplikację z Twoim logo i w Twoich kolorach — także ikonę na ekranie telefonu po instalacji.",
                Permissions.ManageBranding, StatusKey: "branding"),
            new("admin/site", "bi-globe", "Twoja strona",
                "Oferta, opinie i „Umów trening”",
                "Publiczna strona pod Twoim adresem: kim jesteś, oferta, opinie i przycisk do umówienia pierwszego treningu. Link wrzucasz na Instagram i do wizytówki Google.",
                Permissions.ManageBranding)
        ]),
        new("Integracje i treści", "bi-plug", "teal",
        [
            new("admin/google-calendar", "bi-calendar2-week-fill", "Kalendarz Google",
                "Wizyty w Twoim kalendarzu",
                "Treningi trafiają do Twojego Kalendarza Google, a prywatne wydarzenia z kalendarza blokują te godziny w grafiku — nikt nie zapisze się na Twój dentystę.",
                PlanFlag: "IntegrationGoogleMeet", PlanName: "Pro"),
            new("admin/google-meet", "bi-camera-video-fill", "Google Meet",
                "Linki do treningów online",
                "Do treningów online aplikacja sama dołącza link do spotkania Google Meet.",
                Permissions.ManageBranding, "IntegrationGoogleMeet", "Pro", HideWhenManaged: true),
            new("admin/courses", "bi-mortarboard-fill", "Kursy online",
                "Programy i kursy do sprzedaży",
                "Sprzedawaj kursy wideo i programy treningowe online — klient kupuje dostęp i ogląda lekcje w aplikacji.",
                Permissions.ManageCourses, "CoursesEnabled", "Pro", StatusKey: "courses"),
            new("admin/video", "bi-camera-reels-fill", "Wideo",
                "Miejsce na filmy do kursów",
                "Twoje filmy (lekcje, instruktaże ćwiczeń) — ile miejsca i transferu zostało w Twoim planie. Więcej dokupisz w Sklepie usług.",
                Permissions.ManageCourses, "CoursesEnabled", "Pro", OwnerOnly: true)
        ]),
        new("Zespół", "bi-people", "orange",
        [
            new("admin/users", "bi-people-fill", "Użytkownicy",
                "Trenerzy, asystenci i konta klientów",
                "Dodawaj trenerów i asystentów do swojego studia, resetuj hasła klientów, blokuj konta.",
                Permissions.ManageUsers, StatusKey: "users"),
            new("admin/permissions", "bi-shield-lock-fill", "Uprawnienia",
                "Co może asystent, a co trener",
                "Decydujesz, co widzą i mogą zmieniać asystenci i inni trenerzy — np. grafik tak, finanse nie.",
                Permissions.ManageUsers, "RoleBasedAccess", "Business")
        ], Advanced: true),
        new("Dane i bezpieczeństwo", "bi-shield-check", "slate",
        [
            new("admin/backup", "bi-archive-fill", "Kopie zapasowe",
                "Kopia danych na wszelki wypadek",
                "Kopie Twoich danych robią się automatycznie. Tu możesz zrobić dodatkową kopię albo pobrać ją na dysk.",
                Permissions.ManageBackup),
            new("admin/export", "bi-download", "Eksport danych",
                "Klienci, wizyty i płatności do Excela",
                "Pobierz dane do arkusza — np. dla księgowej albo do własnych analiz.",
                Permissions.DataExport, "DataExport", "Pro"),
            new("admin/audit-logs", "bi-journal-text", "Historia zmian",
                "Kto i co zmienił",
                "Lista ważnych zdarzeń: logowania, zmiany ustawień, usunięte wizyty — przydaje się, gdy pracujesz z zespołem.",
                Permissions.ViewAuditLogs, "AuditLog", "Business"),
            new("admin/settings", "bi-database-fill", "Baza danych",
                "Połączenie z bazą (instalacja własna)",
                "Ustawienia bazy danych dla instalacji na własnym serwerze.",
                Permissions.ManageBackup, HideWhenManaged: true),
            new("admin/demo", "bi-database-fill-gear", "Dane demo i reset",
                "Przykładowe dane do wypróbowania",
                "Wgraj przykładowych klientów i wizyty, żeby zobaczyć aplikację w akcji — albo wyczyść je przed startem.",
                Permissions.ManageBackup)
        ], Advanced: true)
    ];

    /// <summary>Pozycja i grupa dla bieżącej ścieżki (np. „admin/branding/…”).</summary>
    public static (AdminMenuGroup Group, AdminMenuItem Item)? Find(string path)
    {
        path = path.Trim('/');
        foreach (var g in Groups)
            foreach (var i in g.Items)
                if (path.Equals(i.Href, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(i.Href + "/", StringComparison.OrdinalIgnoreCase))
                    return (g, i);
        return null;
    }
}
