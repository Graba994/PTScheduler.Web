# PTScheduler

Aplikacja webowa do zarządzania harmonogramem trenera personalnego. Umożliwia planowanie sesji, zarządzanie klientami, pakietami treningowymi, dostępnością trenera oraz rezerwacjami (jednorazowymi i cyklicznymi).

## Technologia

- **Frontend/Backend:** Blazor Server (.NET 10, InteractiveServer)
- **Baza danych:** PostgreSQL + Npgsql 10 (EF Core 10)
- **Autentykacja:** ASP.NET Core Identity z rolami
- **ORM:** Entity Framework Core 10

> **Uwaga:** Aplikacja używa `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` — daty są zapisywane jako czas lokalny (nie UTC). Wszystkie porównania czasowe muszą używać `DateTime.Now`, nie `DateTime.UtcNow`.

---

## Role użytkowników

| Rola | Opis |
|------|------|
| `Admin` | Pełny dostęp — zarządzanie użytkownikami, ustawieniami, logami, brandigiem, backupem |
| `Trainer` | Zarządzanie klientami, sesjami, dostępnością, pakietami |
| `Subordinate` | Menadżer — uprawnienia analogiczne do Trainera, podlega konkretnemu trenerowi |
| `Client` | Widok własnych wizyt, grafiku, dashboardu; opcjonalnie samodzielna rezerwacja |

---

## Moduły

### Kalendarz (`/calendar`)
Widok tygodniowy (FullCalendar) ze wszystkimi sesjami trenera. Kliknięcie w sesję otwiera panel boczny z akcjami: ukończ, no-show, anuluj (z powodem), przeplanuj.

### Wizyty (`/sessions`)
Lista sesji z podziałem na nadchodzące i archiwum. Panel boczny z akcjami jak w kalendarzu. Dla klientów z włączoną samodzielną rezerwacją — przycisk "Umów wizytę" z:
- wyborem dnia i dostępnych slotów (pobieranych z kalendarza trenera),
- opcją rezerwacji **serii** (np. co poniedziałek o 8:00) z podglądem kolizji i zużycia kredytów pakietu.

### Klienci (`/clients`)
Lista klientów przypisanych do trenera. Profil klienta zawiera: dane osobowe, historię sesji, pomiary ciała, notatki trenera, pakiety, ustawienia konta.

### Dashboard
- **Trener:** statystyki, nadchodzące sesje, lista klientów
- **Klient:** aktywne pakiety (wszystkie), najbliższe wizyty

### Dostępność trenera (`/trainer/availability`)
Definiowanie okien dostępności (cykliczne wg dnia tygodnia lub jednorazowe wg daty). Konfiguracja:
- przerwa techniczna między sesjami (minuty),
- granularność slotów (15/30/45/60 min),
- przełącznik "klienci widzą listę kontaktów" (peer discovery).

### Pary klientów (`/trainer/contacts`)
Łączenie klientów w pary do wspólnych rezerwacji (treningi duet). Trener dodaje i usuwa pary; klienci z włączonym peer discovery widzą kontakty.

### Pakiety (`/packages`)
Zarządzanie pakietami sesyjnymi klientów. Statusy: `Active`, `Expired`, `Cancelled`. Sesje wykraczające poza dostępne kredyty pakietu otrzymują status `AwaitingPackage` — termin jest zablokowany w kalendarzu, trener widzi ostrzeżenie.

### Statusy sesji

| Status | Opis |
|--------|------|
| `Scheduled` | Zaplanowana |
| `Completed` | Ukończona |
| `Cancelled` | Anulowana |
| `NoShow` | Klient nie przyszedł |
| `AwaitingPackage` | Termin zablokowany, brak aktywnego pakietu |

### Logi audytu (`/admin/audit-logs`)
Tabela wszystkich akcji z możliwością przeszukiwania i filtrowania po typie encji. Logowane są: zmiany statusów sesji, tworzenie/anulowanie rezerwacji, operacje na pakietach i in.

### Ustawienia admina
- **Branding** — logo, nazwa firmy, kolory
- **Użytkownicy** — tworzenie, edycja, przypisywanie ról
- **Backup** — eksport bazy danych
- **Ustawienia** — połączenie z bazą danych

---

## Demo i reset danych (`/admin/demo`)

Strona dostępna wyłącznie dla roli `Admin`. Zawiera dwa narzędzia do szybkiego zasilenia lub wyczyszczenia bazy.

### Załaduj dane demonstracyjne

Jednym kliknięciem tworzy kompletne środowisko testowe:

| Rola | Login | Hasło |
|------|-------|-------|
| Trener | `jan.kowalski@demo.pl` | `trener1` |
| Menadżer | `anna@demo.pl` | `menedzer1` |
| Klient | `marek@demo.pl` | `klient1` |
| Klient | `katarzyna@demo.pl` | `klient1` |
| Klient | `piotr@demo.pl` | `klient1` |
| Klient | `alicja@demo.pl` | `klient1` |

Seed tworzy dodatkowo:
- dostępność trenera (pon–pt 8:00–18:00, sob 9:00–13:00)
- konfigurację trenera (przerwa 15 min, slot 30 min, peer discovery włączone)
- pakiety treningowe (aktywny, aktywny, wyczerpany)
- sesje historyczne i przyszłe w różnych statusach (Completed, NoShow, Cancelled, Scheduled, AwaitingPackage)
- pomiary ciała i notatki trenera dla wybranych klientów
- parę klientów Marek & Katarzyna

Operacja jest **idempotentna** — jeśli dane demo już istnieją, nie nadpisze niczego.

Po załadowaniu strona wyświetla tabelkę z loginami i hasłami wszystkich utworzonych użytkowników.

### Reset bazy danych

Usuwa **wszystkie dane** (klientów, sesje, pakiety, użytkowników) i tworzy jedno konto administratora:

| Login | Hasło |
|-------|-------|
| `root@admin.local` | `password` |

> Operacja wymaga podwójnego potwierdzenia. Po resecie bieżące konto zostaje usunięte — aplikacja wyloguje użytkownika automatycznie.

---

## Uruchomienie

### Wymagania
- .NET 10 SDK
- PostgreSQL (testowany na 15+)

### Konfiguracja połączenia

Ustaw connection string w `PTScheduler.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ptscheduler;Username=postgres;Password=yourpassword"
  }
}
```

Lub przez zmienną środowiskową (używaną przez migracje EF):

```
PTSCHEDULER_CONN=Host=...;Database=ptscheduler;Username=postgres;Password=...
```

### Migracje bazy danych

```bash
dotnet ef migrations add <NazwaMigracji> \
  --project PTScheduler.Infrastructure \
  --startup-project PTScheduler.Infrastructure

$env:PTSCHEDULER_CONN = "Host=...;..."
dotnet ef database update \
  --project PTScheduler.Infrastructure \
  --startup-project PTScheduler.Infrastructure
```

### Uruchomienie aplikacji

```bash
dotnet run --project PTScheduler.Web
```

Przy starcie aplikacja automatycznie seeduje role (`Admin`, `Trainer`, `Subordinate`, `Client`) oraz podstawowe typy sesji.

---

## Struktura projektu

```
PTScheduler.Web.slnx
├── PTScheduler.Domain          # Encje, enumy, stałe (bez zależności)
├── PTScheduler.Application     # Interfejsy serwisów, DTO
├── PTScheduler.Infrastructure  # Implementacje serwisów, EF Core, migracje
└── PTScheduler.Web             # Blazor Server — strony, layouty, komponenty
```
