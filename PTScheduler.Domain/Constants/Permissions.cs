namespace PTScheduler.Domain.Constants;

public static class Permissions
{
    public const string ManageClients = "ManageClients";
    public const string ManageSessions = "ManageSessions";
    public const string ManagePackages = "ManagePackages";
    public const string ViewStats = "ViewStats";
    public const string ViewFinance = "ViewFinance";
    public const string ManageAvailability = "ManageAvailability";
    public const string ManageSeries = "ManageSeries";
    public const string ManageContacts = "ManageContacts";
    public const string ManageIntroConfig = "ManageIntroConfig";
    public const string ViewAuditLogs = "ViewAuditLogs";
    public const string ManageUsers = "ManageUsers";
    public const string ManageBranding = "ManageBranding";
    public const string ManageSessionTypes = "ManageSessionTypes";
    public const string ManageBackup = "ManageBackup";
    public const string ManageEmail = "ManageEmail";
    public const string ManagePayments = "ManagePayments";
    public const string ViewHiddenFinance = "ViewHiddenFinance";
    public const string ManageCourses = "ManageCourses";
    public const string DataExport = "DataExport";

    public static readonly (string Key, string Label, string Description)[] All =
    [
        (ManageClients,      "Klienci",           "Wyświetlanie i zarządzanie klientami"),
        (ManageSessions,     "Wizyty",            "Tworzenie, edycja i anulowanie wizyt"),
        (ManagePackages,     "Pakiety",           "Tworzenie i zarządzanie pakietami sesji"),
        (ViewStats,          "Statystyki",        "Dostęp do strony statystyk"),
        (ViewFinance,        "Finanse",           "Dostęp do strony finansów"),
        (ViewHiddenFinance,  "Ukryte finanse",    "Dostęp do ukrytego modułu finansów (wymaga PIN)"),
        (ManageAvailability, "Dostępność",        "Zarządzanie dostępnością trenera"),
        (ManageSeries,       "Serie wizyt",       "Zarządzanie seriami cyklicznymi"),
        (ManageContacts,     "Pary klientów",     "Zarządzanie parami/kontaktami klientów"),
        (ManageIntroConfig,  "Pierwsza wizyta",   "Konfiguracja pierwszej wizyty"),
        (ViewAuditLogs,      "Logi audytu",       "Przeglądanie logów audytu"),
        (ManageUsers,        "Użytkownicy",       "Zarządzanie użytkownikami i rolami"),
        (ManageBranding,     "Wygląd",            "Konfiguracja motywu i brandingu"),
        (ManageSessionTypes, "Typy sesji",        "Zarządzanie typami sesji"),
        (ManageBackup,       "Backup / Reset",    "Operacje backup i reset bazy"),
        (ManageEmail,        "E-mail",            "Konfiguracja ustawień e-mail"),
        (ManagePayments,     "Płatności",         "Konfiguracja płatności online (PayU)"),
        (ManageCourses,      "Kursy",             "Zarządzanie kursami, treścią i dostępami"),
        (DataExport,         "Eksport danych",    "Eksport danych klientów, sesji i pomiarów"),
    ];

    public static readonly Dictionary<string, string[]> Defaults = new()
    {
        [Roles.Admin] = All.Select(p => p.Key).ToArray(),
        [Roles.Trainer] =
        [
            ManageClients, ManageSessions, ManagePackages,
            ViewStats, ViewFinance,
            ManageAvailability, ManageSeries, ManageContacts, ManageIntroConfig,
            ManageCourses, ManagePayments, DataExport
        ],
        [Roles.Subordinate] =
        [
            ManageClients, ManageSessions, ManagePackages,
            ViewStats, ManageAvailability
        ],
        [Roles.Client] = []
    };
}
