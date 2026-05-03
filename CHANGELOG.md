# Changelog

Wszystkie istotne zmiany w projekcie są dokumentowane w tym pliku.

Format oparty na [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.4.0] — 2026-05-02

### Dodano
- **Dark mode i tryb systemowy** — każdy z 10 akcentów kolorystycznych ma teraz wariant ciemny. W panelu Branding nowy selektor trybu: ☀️ Jasny / 🌙 Ciemny / 💻 Systemowy. Tryb systemowy śledzi preferencje systemu (Android, iOS, Windows, macOS) na żywo przez `matchMedia`. Motyw aplikowany inline przed załadowaniem CSS — brak migotania (FOUC-free).
- **Powiadomienia e-mail** — konfigurowalny serwer SMTP (`/admin/email`). Automatyczne wysyłanie e-maili przy: potwierdzeniu rezerwacji, potwierdzeniu anulowania, przypomnieniu 24h przed sesją. Przypomnienia realizowane przez `BackgroundService` z deduplikacją przez `HashSet` w pamięci.
- **Pomiary ciała klienta** — tabela pomiarów w profilu klienta: waga (kg), % tłuszczu, klatka piersiowa, talia, biodra, udo, ramię. Automatyczne strzałki trendu (↑↓) z kolorowaniem zielony/czerwony w zależności od kierunku pożądanej zmiany.
- **Kreator samodzielnej rezerwacji klientów** — klienci z włączoną flagą `AllowSelfBooking` mogą rezerwować wizyty przez 3-krokowy modal: wybór typu sesji → wybór slotu → potwierdzenie. Dwukrotna weryfikacja dostępności slotu (przed wyborem i przy zapisie) chroni przed race condition.
- **Pełne audit logi** — uzupełnienie logowania we wszystkich brakujących akcjach: zmiana roli/blokady użytkownika, reset hasła, usunięcie użytkownika, operacje na dostępności trenera, zmiany konfiguracji trenera, pomiary ciała.

### Zmieniono
- Panel Branding: zamiast 20 oddzielnych motywów (10 jasnych + 10 ciemnych) — 10 akcentów kolorystycznych × selektor trybu (jasny/ciemny/systemowy).
- `ThemeName` w bazie danych przechowuje teraz tylko nazwę akcentu (bez sufiksu `-dark`). Nowe pole `ThemeMode` przechowuje tryb.
- `App.razor`: atrybuty `data-theme` (akcent) + `data-mode` (tryb) na elemencie `<html>`.

### Baza danych
- Migracja `AddThemeMode` — dodanie kolumny `ThemeMode text NOT NULL DEFAULT 'light'` do tabeli `AppBrandings`.

---

## [0.3.0] — 2026-05-02

### Dodano
- **System dostępności trenera** — okna dostępności cykliczne (wg dnia tygodnia) i jednorazowe (wg daty). Konfiguracja przerwy technicznej, granularności slotów (15/30/45/60 min) i peer discovery. Strona `/trainer/availability`.
- **Generowanie dostępnych slotów** — serwis `ITrainerAvailabilityService.GetAvailableSlotsAsync` oblicza wolne terminy z uwzględnieniem istniejących sesji i przerw technicznych.
- **Serie cykliczne** — rezerwacja wielokrotnych sesji jedną operacją (np. co poniedziałek o 8:00). Podgląd kolizji i automatyczne zużycie kredytów pakietu. Strona `/trainer/series`.
- **Kontakty / pary klientów** — łączenie klientów w pary do treningów duet. Strona `/trainer/contacts`.
- **Audit log** — nowa tabela `AuditLogs`, serwis `IAuditLogService`, strona przeglądania `/admin/audit-logs`.
- **Zaproszenia do sesji** — `ISessionInvitationService` do zarządzania zaproszeniami na wspólne treningi.
- **Strona Mój grafik** (`/my`) — widok klienta z nadchodzącymi wizytami i możliwością anulowania.

### Baza danych
- Migracja `AddAuditLogs` — tabela `AuditLogs`.
- Migracja `AddSchedulingSystem` — tabele dostępności trenera, konfiguracji, serii sesji, kontaktów.

---

## [0.2.0] — 2026-04-30

### Dodano
- **Pakiety treningowe** (`ISessionPackageService`) — kredyty sesyjne, statusy `Active` / `Expired` / `Cancelled`. Sesje bez aktywnego pakietu otrzymują status `AwaitingPackage`.
- **Cykl życia klienta** — statusy konta (`PendingApproval`, `Active`), zatwierdzanie przez trenera, blokowanie samodzielnej rezerwacji.
- **Notatki trenera** — pole tekstowe notatek w profilu klienta.
- **Branding** (`IBrandingService`) — upload logo i favicon, wybór motywu kolorystycznego. Tabela `AppBrandings` w bazie.
- **Konfiguracja wstępna trenera** — strona `/trainer/intro-config`.

### Baza danych
- Migracja `PackagesAndClientLifecycle` — tabele pakietów, rozszerzenie profilu klienta.
- Migracja `AddAppBranding` — tabela `AppBrandings`.

---

## [0.1.0] — 2026-04-28

### Dodano
- **Inicjalna struktura projektu** — architektura Clean Architecture: Domain / Application / Infrastructure / Web.
- **Autentykacja i autoryzacja** — ASP.NET Core Identity z rolami `Admin`, `Trainer`, `Subordinate`, `Client`.
- **Zarządzanie użytkownikami** (`/admin/users`, `/admin/users/{id}`) — lista, edycja roli, blokada konta, reset hasła, usunięcie.
- **Kalendarz** (`/calendar`) — widok tygodniowy FullCalendar z sesjami trenera.
- **Wizyty** (`/sessions`) — lista z podziałem nadchodzące / archiwum, panel boczny z akcjami (ukończ, no-show, anuluj, przeplanuj).
- **Klienci** (`/clients`, `/clients/{id}`) — lista, profil, historia sesji.
- **Dashboard** — widoki dla trenera i klienta (`/dashboard`).
- **Demo i reset** (`/admin/demo`) — seeder danych demonstracyjnych z 6 kontami testowymi, reset bazy do stanu pierwotnego.
- **Backup** (`/admin/backup`) — eksport bazy PostgreSQL.
- **Ustawienia** (`/admin/settings`) — zmiana connection string.
- **PWA** — Service Worker, manifest, przycisk instalacji (`beforeinstallprompt`).
- **Docker** — multi-stage Dockerfile, docker-compose z PostgreSQL 17. Statyczne zasoby CDN (Bootstrap Icons, FullCalendar) pobierane do obrazu w czasie budowania — kontener nie wymaga dostępu do internetu.
- **Seed ról i typów sesji** — wykonywany automatycznie przy każdym starcie aplikacji.

### Baza danych
- Migracja `Initial` — pełna struktura: użytkownicy (Identity), klienci, sesje, typy sesji.
