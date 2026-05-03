# PTScheduler

Aplikacja webowa do zarządzania harmonogramem trenera personalnego. Umożliwia planowanie sesji, zarządzanie klientami, pakietami treningowymi, dostępnością trenera oraz rezerwacjami (jednorazowymi i cyklicznymi). Klienci mogą samodzielnie rezerwować wizyty spośród dostępnych slotów trenera.

## Technologia

| Warstwa | Technologia |
|---------|-------------|
| Frontend / Backend | Blazor Server (.NET 10, `InteractiveServer`) |
| Baza danych | PostgreSQL 15+ · Npgsql 10 · EF Core 10 |
| Autentykacja | ASP.NET Core Identity z rolami |
| Widok kalendarza | FullCalendar 6.1.15 |
| Style | Bootstrap 5 + Bootstrap Icons 1.11 |
| Konteneryzacja | Docker (multi-stage build) |
| PWA | Service Worker + Web App Manifest |

> **Uwaga dot. czasu:** Aplikacja używa `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`. Daty są traktowane jako czas lokalny (nie UTC). Wszystkie porównania czasowe używają `DateTime.Now`, nie `DateTime.UtcNow`.

---

## Szybki start (Docker)

```bash
cp .env.example .env
# Uzupełnij DB_PASSWORD w .env

docker compose up -d
```

Aplikacja dostępna pod `http://localhost:8080`.

Przy pierwszym uruchomieniu wykonaj reset bazy (`/admin/demo`), który utworzy konto administratora:

| Login | Hasło |
|-------|-------|
| `root@admin.local` | `password` |

---

## Uruchomienie lokalne

### Wymagania

- .NET 10 SDK
- PostgreSQL 15+

### Konfiguracja

`PTScheduler.Web/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ptscheduler;Username=postgres;Password=yourpassword"
  }
}
```

Lub przez zmienną środowiskową:

```
ConnectionStrings__DefaultConnection=Host=...;Database=ptscheduler;Username=postgres;Password=...
```

### Migracje

```bash
dotnet ef migrations add <NazwaMigracji> \
  --project PTScheduler.Infrastructure \
  --startup-project PTScheduler.Infrastructure

dotnet ef database update \
  --project PTScheduler.Infrastructure \
  --startup-project PTScheduler.Infrastructure
```

### Uruchomienie

```bash
dotnet run --project PTScheduler.Web
```

Przy starcie aplikacja automatycznie seeduje role (`Admin`, `Trainer`, `Subordinate`, `Client`) i domyślne typy sesji oraz stosuje oczekujące migracje EF Core.

---

## Role użytkowników

| Rola | Opis |
|------|------|
| `Admin` | Pełny dostęp — użytkownicy, branding, audit logi, backup, ustawienia e-mail |
| `Trainer` | Zarządzanie klientami, sesjami, dostępnością, pakietami |
| `Subordinate` | Asystent trenera — uprawnienia jak Trainer, podlega konkretnemu trenerowi |
| `Client` | Podgląd własnych wizyt i pakietów; opcjonalnie samodzielna rezerwacja |

---

## Moduły i strony

### Kalendarz — `/calendar`

Widok tygodniowy (FullCalendar) ze wszystkimi sesjami. Kolory zależą od statusu sesji. Kliknięcie otwiera panel boczny z akcjami: ukończ, no-show, anuluj (z powodem), przeplanuj. Dostępny dla ról `Admin`, `Trainer`, `Subordinate`.

### Wizyty — `/sessions`

Lista sesji z podziałem na nadchodzące i archiwum. Panel boczny z tymi samymi akcjami co w kalendarzu. Dostępny dla wszystkich ról.

Dla klientów z włączoną samodzielną rezerwacją — przycisk **Umów wizytę** otwiera kreator 3-krokowy:
1. Wybór typu sesji
2. Wybór dnia i dostępnego slotu (generowane z kalendarza trenera, z uwzględnieniem przerw i granularności slotów)
3. Podsumowanie + opcjonalna notatka + ostateczna weryfikacja dostępności

Możliwa rezerwacja **serii cyklicznych** (np. co poniedziałek o 8:00) z podglądem kolizji i automatycznym zużyciem kredytów pakietu.

### Mój grafik — `/my` *(tylko rola Client)*

Uproszczony widok nadchodzących wizyt dla zalogowanego klienta. Zawiera kreator rezerwacji oraz możliwość anulowania własnych wizyt.

### Klienci — `/clients`

Lista klientów przypisanych do trenera z wyszukiwarką i filtrami statusu.

**Profil klienta** (`/clients/{id}`) zawiera:

- dane osobowe i status konta (oczekujący zatwierdzenia / aktywny),
- historię sesji z filtrowaniem po statusie,
- **pomiary ciała** — tabela wyników (waga, % tłuszczu, klatka piersiowa, talia, biodra, udo, ramię) z datami i strzałkami trendu (↑/↓, zielona = poprawa, czerwona = pogorszenie),
- notatki trenera,
- pakiety treningowe — aktywne, wygasłe, anulowane,
- ustawienia konta: włącz/wyłącz samodzielną rezerwację, resetuj hasło klienta.

### Dostępność trenera — `/trainer/availability`

Definiowanie okien dostępności:

- **cykliczne** — dzień tygodnia + opcjonalne daty ważności od/do,
- **jednorazowe** — konkretna data.

Konfiguracja trenera:
- przerwa techniczna po sesji (minuty),
- granularność slotów (15 / 30 / 45 / 60 min),
- przełącznik *Klienci widzą listę kontaktów* (peer discovery).

### Kontakty / pary klientów — `/trainer/contacts`

Łączenie klientów w pary do wspólnych treningów. Klienci z włączonym peer discovery widzą swoich partnerów treningowych.

### Serie — `/trainer/series`

Zarządzanie cyklicznymi seriami sesji. Przegląd aktywnych i zakończonych serii z możliwością anulowania całej serii jednym kliknięciem.

### Pakiety — (w profilu klienta)

Zarządzanie pakietami sesyjnymi. Statusy: `Active`, `Expired`, `Cancelled`. Sesje wykraczające poza kredyty pakietu otrzymują status `AwaitingPackage` — termin jest blokowany w kalendarzu, trener widzi ostrzeżenie.

### Statusy sesji

| Status | Opis |
|--------|------|
| `Scheduled` | Zaplanowana |
| `Completed` | Ukończona |
| `Cancelled` | Anulowana |
| `NoShow` | Klient nie przyszedł |
| `AwaitingPackage` | Termin zablokowany — brak aktywnego pakietu |

---

## Powiadomienia e-mail

Konfiguracja SMTP w panelu admina (`/admin/email`). Po włączeniu system automatycznie wysyła:

| Zdarzenie | Odbiorca |
|-----------|----------|
| Potwierdzenie rezerwacji | Klient |
| Potwierdzenie anulowania | Klient |
| Przypomnienie 24h przed sesją | Klient |

Wysyłka jest ochroniona blokiem `try/catch` — błąd SMTP nie przerywa operacji rezerwacji ani anulowania. Przypomnienia 24h wysyłane są przez `BackgroundService` sprawdzający sesje co godzinę (deduplikacja przez `HashSet` w pamięci).

---

## Audit log — `/admin/audit-logs`

Tabela wszystkich akcji w systemie z przeszukiwaniem i filtrowaniem po typie encji. Logowane zdarzenia:

- zmiany statusów sesji (ukończona, anulowana, no-show, przeplanowana)
- rezerwacje i anulowania (trener, klient)
- operacje na pakietach i płatnościach
- zmiany ról i blokady kont użytkowników
- operacje na dostępności trenera i konfiguracji
- zmiany ustawień brandingu

---

## Wygląd i motyw — `/admin/branding`

### Akcenty kolorystyczne

10 palety do wyboru: Ocean, Forest, Sunset, Crimson, Lavender, Slate, Rose, Teal, Amber, Indigo.

### Tryb wyświetlania

| Tryb | Opis |
|------|------|
| ☀️ Jasny | Zawsze jasne tło |
| 🌙 Ciemny | Zawsze ciemne tło |
| 💻 Systemowy | Automatycznie podąża za ustawieniem systemu (Android / iOS / Windows / macOS) — przełącza się na żywo przy zmianie preferencji systemu |

Motyw stosowany jest przez inline `<script>` w `<head>` przed załadowaniem arkuszy CSS — brak migotania przy ładowaniu strony (FOUC-free).

### Identyfikacja

Upload logo (PNG/SVG/JPG, maks. 2 MB) i favicon (PNG/ICO, maks. 512 KB).

---

## Panel administratora

| Strona | Opis |
|--------|------|
| `/admin/users` | Lista wszystkich użytkowników |
| `/admin/users/{id}` | Edycja roli, blokada konta, reset hasła, usunięcie |
| `/admin/branding` | Motyw, tryb dark/light/system, logo, favicon |
| `/admin/email` | Konfiguracja SMTP + wysyłanie maila testowego |
| `/admin/audit-logs` | Historia wszystkich operacji |
| `/admin/backup` | Eksport bazy danych |
| `/admin/settings` | Ustawienia połączenia z bazą |
| `/admin/demo` | Dane demonstracyjne / reset bazy |

---

## Demo i reset danych — `/admin/demo`

### Dane demonstracyjne

Jednym kliknięciem tworzy kompletne środowisko testowe:

| Rola | Login | Hasło |
|------|-------|-------|
| Trener | `jan.kowalski@demo.pl` | `trener1` |
| Menadżer | `anna@demo.pl` | `menedzer1` |
| Klient | `marek@demo.pl` | `klient1` |
| Klient | `katarzyna@demo.pl` | `klient1` |
| Klient | `piotr@demo.pl` | `klient1` |
| Klient | `alicja@demo.pl` | `klient1` |

Seed tworzy też: dostępność trenera (pon–pt 8:00–18:00, sob 9:00–13:00), pakiety, sesje historyczne i przyszłe we wszystkich statusach, pomiary ciała, notatki i parę klientów Marek & Katarzyna. Operacja jest **idempotentna**.

### Reset bazy danych

Usuwa wszystkie dane i tworzy jedno konto administratora:

| Login | Hasło |
|-------|-------|
| `root@admin.local` | `password` |

> Wymaga podwójnego potwierdzenia. Po resecie bieżące konto zostaje usunięte — aplikacja wyloguje użytkownika.

---

## PWA

Aplikacja działa jako Progressive Web App:
- manifest (`/manifest.json`) — ikona, skrót, orientacja,
- Service Worker (`/sw.js`) — cache statycznych zasobów,
- przycisk instalacji pojawia się na urządzeniach obsługujących `beforeinstallprompt`.

---

## Zmienne środowiskowe

| Zmienna | Opis | Wymagana |
|---------|------|----------|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string | ✅ |
| `DB_PASSWORD` | Hasło DB (docker-compose) | ✅ |
| `APP_PORT` | Port hosta kontenera (domyślnie `8080`) | ❌ |
| `ASPNETCORE_ENVIRONMENT` | `Production` / `Development` | ❌ |

---

## Struktura projektu

```
PTScheduler.Web.slnx
├── PTScheduler.Domain/           # Encje, enumy, stałe — bez zależności zewnętrznych
├── PTScheduler.Application/      # Interfejsy serwisów, DTO
├── PTScheduler.Infrastructure/   # Implementacje serwisów, EF Core, migracje, SMTP
└── PTScheduler.Web/              # Blazor Server — strony, layouty, komponenty
    ├── Components/
    │   ├── Layout/               # MainLayout, NavMenu, Sidebar
    │   ├── Pages/
    │   │   ├── Admin/            # Branding, Users, AuditLogs, Email, Backup, Demo, Settings
    │   │   ├── Clients/          # Lista klientów, profil, nowy klient
    │   │   ├── Dashboard/        # TrainerDashboard, ClientDashboard
    │   │   └── Trainer/          # Availability, Contacts, Series, IntroConfig
    │   └── Account/              # ASP.NET Identity pages
    ├── Services/                 # BackgroundService (przypomnienia e-mail)
    └── wwwroot/
        ├── app.css               # Style globalne + 10 motywów × tryb jasny/ciemny
        └── branding/             # Pliki uploadu (logo, favicon)
```

### Zależności warstw

```
Web → Application ← Infrastructure
          ↑
        Domain
```

`Domain` nie ma zależności NuGet. `Application` zna tylko `Domain`. `Infrastructure` implementuje interfejsy z `Application`. `Web` łączy wszystko przez DI w `Program.cs`.
