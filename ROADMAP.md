# Roadmap — PTScheduler → Platforma dla trenerek

Dokument roboczy. Zapisuje ustalenia, decyzje architektoniczne i plan
budowy, żeby nie zniknęły między sesjami.

---

## 1. Wizja produktu

Aplikacja **modularna** dla trenerek personalnych/mentorek/dietetyczek —
łączy w jednej instancji:

- **Welcome / Landing page** — strona wejściowa właścicielki, edytowalna z admina.
- **Scheduler** — kalendarz, klienci, pakiety sesji, notatki, pomiary (to co już istnieje).
- **Learning Portal** — kursy → moduły → lekcje (wideo + tekst + PDF), postępy, czasowy dostęp.
- **Commerce (cienki)** — sprzedaż produktów cyfrowych, checkout redirect do bramki, webhook aktywuje dostęp.
- **Admin/Owner config** — włączanie i konfiguracja modułów per instancja.

**Filozofia:** modularny monolit z feature flagami. Każdy moduł da się
włączyć/wyłączyć w panelu Ownera. Nie budujemy klona Publigo — budujemy
skrojony pod jedną trenerkę produkt, który da się sprzedawać kolejnym.

---

## 2. Model biznesowy i pierwszy klient

**Trenerka #1:** Ana (`zmotywowana-ana.pl`) — mentoring żywieniowy.

**Flagowy produkt "odNowa":**
- 3000 PLN, dostęp na 6 miesięcy od daty zakupu.
- 22 lekcje wideo.
- Workbook PDF.
- Konsultacje grupowe raz w tygodniu (link zewnętrzny — Zoom/Meet — w panelu klientki).
- Dodatkowe narzędzia: dziennik jedzenia, skala głodu, listy produktów, jadłospisy, checklisty.

**Płatności obecnie:** Klarna + PayU. Docelowo wiele bramek (Stripe, tPay, Przelewy24…).

**Wideo:** hostowane poza serwerem (Google Drive dziś, docelowo też Bunny.net Stream).

**Konsultacje na żywo:** NIE budujemy live-streamingu; wystarczy link zewnętrzny w lekcji/panelu.

---

## 3. Infrastruktura

**VPS (per trenerka):** VPS-1 2027 — 2 vCores, 4 GB RAM, 40 GB dysk.
Wystarczy dla ~30–60 aktywnych klientek jednocześnie (Blazor Server trzyma
sesję w RAM). Jeśli baza klientek rośnie — większy VPS albo osobna instancja.

**Model dostarczania:** **Docker per klient** — pojedyncza instancja
single-tenant, izolacja fizyczna. To NIE jest klasyczny multi-tenant SaaS.
Konsekwencje:

- brak `tenant_id` w tabelach (prostszy kod)
- osobna baza per trenerka
- update: rebuild image → Watchtower pull → restart
- migracje EF odpalają się przy starcie kontenera (już wdrożone)

**Stack serwerowy (już postawiony):**
- Nginx Proxy Manager — reverse proxy + SSL
- Portainer — zarządzanie stackami
- Adminer — GUI do baz Postgres
- Watchtower — auto-pull nowych obrazów z GHCR
- Dozzle — live logs
- Uptime Kuma — monitoring + alerty (Telegram/Discord)

**Obraz aplikacji:** `ghcr.io/graba994/ptscheduler.web:latest` — publikowany z GitHub Actions.

---

## 4. Kluczowe decyzje architektoniczne

### 4.1 Subdomeny vs ścieżki

**Wybór: ścieżki** (`domena.pl/academy`, `domena.pl/booking`, `domena.pl/shop`).

Powód: cookie sesji Blazor Server / SignalR / wspólne menu / prosty NPM config.
Subdomeny łamią single sign-on i podwajają footprint w RAM.

### 4.2 Wideo — dwa źródła

Encja `AcademyLesson` przechowuje:
- `VideoProvider` (enum: `GoogleDrive`, `BunnyStream`, `Vimeo`, `None`)
- `VideoRef` (ID pliku Drive / GUID Bunny)

Renderowanie w Blazor: dynamiczny `<iframe>` zależnie od providera. Ruch
wideo omija VPS (streaming z Google CDN / Bunny CDN bezpośrednio do
przeglądarki). Autoryzacja dostępu do lekcji po stronie komponentu — jeśli
brak uprawnień, iframe nie renderuje się w ogóle.

Wzorzec dla Google Drive:
```html
<iframe src="https://drive.google.com/file/d/@Model.GoogleDriveId/preview"
        width="100%" height="450"
        allow="autoplay; fullscreen"
        allowfullscreen frameborder="0"></iframe>
```

### 4.3 Role

- **Admin** — dev/ops (Ty). Widzi DB config, backup, restart. Panel `/admin/settings`, `/admin/backup`.
- **Trainer (Owner)** — trenerka. Edytuje treści, produkty, ceny, klientów, płatności, branding. Może zarządzać rolami tylko poniżej siebie (`Subordinate`, `Client`).
- **Subordinate** — asystentka trenerki (linkowana przez `SupervisorId`).
- **Client** — kursantka.

Rola egzekwowana **dwuwarstwowo**: `[Authorize(Roles=...)]` na stronie +
`callerRole`-based enforcement w `UserManagementService` (defense in depth).

### 4.4 Setup nowej instancji

Na razie: czysta instalacja + ręczna konfiguracja przez Ownera w admin panelu.
W przyszłości (jak nabierze popularności): dedykowany "Setup Wizard" dla klienta.

---

## 5. Plan budowy (etapy)

### ✅ Krok 0 — Fundament (zrobione)

Commit: `2291134`

- Rozdzielenie ról Admin/Trainer na istniejących stronach + logika w serwisie.
- `db.Database.Migrate()` na starcie aplikacji.
- Persystentne DataProtection keys (`/app/data/keys`) — cookies przeżywają restart kontenera.
- `UseForwardedHeaders` dla NPM (poprawne scheme za reverse-proxy).
- `Dockerfile` multi-stage .NET 10 (+ `postgresql-client` dla `pg_dump`).
- `.github/workflows/build-image.yml` — build i push do GHCR na `main`.
- `docker-compose.yml` gotowy do wklejenia w Portainer stack.

### ✅ Krok 1 — Learning Portal MVP (zrobione — czeka na weryfikację CI)

**Cel:** trenerka wrzuca 22 lekcje, klientki oglądają, widać postępy, dostęp wygasa po X miesiącach.

Zaimplementowane:

- **Domena:** `AcademyCourse` → `AcademyModule` → `AcademyLesson`, `AcademyEnrollment` (dostęp + `ExpiresAt` + `IsRevoked`), `AcademyLessonProgress` (per klientka/lekcja). Enum `VideoProvider` (None/GoogleDrive/BunnyStream/Vimeo).
- **Application:** DTO + `IAcademyCatalogService` (Owner) i `IAcademyStudentService` (kursantka, każda metoda egzekwuje dostęp po `applicationUserId`).
- **Infrastructure:** DbSets + Fluent config (kaskady, unikalne indeksy `(User,Course)` i `(User,Lesson)`), oba serwisy, rejestracja w DI. Migracja `20260717120000_AddAcademy` napisana ręcznie (brak `dotnet` w środowisku dev — CI to zweryfikuje).
- **Owner UI:** `/academy/courses` (lista+nowy), `/academy/courses/{id}` (dane kursu + moduły/lekcje inline), `/academy/lessons/{id}` (edytor lekcji z wyborem providera wideo + podgląd iframe), `/academy/enrollments` (nadaj/cofnij/usuń dostęp).
- **Klientka UI:** `/academy` (moje kursy + pasek postępu), `/academy/{courseId}` (drzewko z odhaczaniem), `/academy/lesson/{id}` (iframe wideo + treść + workbook + prev/next + „ukończona”).
- **NavMenu:** sekcja Akademia dla Ownera (Kursy, Zapisy) i „Moje kursy” dla kursantki.

**Bezpieczeństwo iframe:** `VideoRef` renderowany w `src` jest whitelistowany znakowo (`^[A-Za-z0-9_-]+$`, dla Bunny dodatkowo `/`) — brak możliwości wstrzyknięcia obcego origin/atrybutu. Autoryzacja dostępu do lekcji po stronie serwisu — bez aktywnego zapisu iframe się nie renderuje.

**Znane założenie:** treść tekstowa lekcji renderowana jako `MarkupString` (HTML) — autorem jest zaufany Trainer (single-tenant), więc OK w MVP. Gdyby treści miały pochodzić z niezaufanego źródła, dołożyć sanitizer.

**Decyzja podjęta:** model `AcademyEnrollment` per (kursantka × kurs) obsługuje OBA warianty — jeden kurs = jeden zapis, wiele kursów = wiele zapisów z niezależnymi datami. Elastyczne domyślnie.

### ✅ Krok 2 — Commerce cienki (zrobione — czeka na weryfikację CI)

**Cel:** klientka kupuje produkt, płaci przez PayU, system automatycznie tworzy konto i nadaje dostęp do kursu.

Zaimplementowane:

- **Domena:** `Product` (nazwa, opis, cena/waluta, opcjonalny `CourseId` → powiązanie z kursem, `AccessDurationDays`, `IsActive`, `SortOrder`). `Order` (produkt, e-mail/imię klienta, kwota, status, `PaymentProvider`, `ExternalPaymentId`, `ApplicationUserId`, `PaidAt`). Enum `OrderStatus` (Pending/Paid/Failed/Refunded/Cancelled).
- **Application:** `IShopService` (CRUD produktów, zamówienia, checkout flow, fulfillment). `IPaymentGateway` (abstrakcja bramki: `CreatePaymentAsync` → redirect URL, `ParseNotificationAsync` → weryfikacja podpisu + parsowanie). DTOs: `ProductDto`, `OrderDto`, `CheckoutRequest`, `PaymentRequest`, `PaymentRedirect`, `PaymentNotification`.
- **Infrastructure:** `ShopService` (pełny CRUD + fulfillment: tworzenie konta klientki z losowym hasłem, nadanie roli Client, enrollment na kurs). `PayUGateway` (OAuth2 token, REST API `v2_1/orders`, weryfikacja podpisu MD5/SHA256 z `SecondKey`).
- **Endpointy (Program.cs):** `POST /api/checkout` (formularz → tworzenie zamówienia → redirect do PayU), `POST /api/payu/notify` (webhook PayU → mark paid → fulfill).
- **Owner UI:** `/shop/products` (lista + nowy), `/shop/products/{id}` (edytor: nazwa, opis, cena, waluta, powiązany kurs, czas dostępu, aktywność), `/shop/orders` (historia zamówień z filtrami statusów).
- **Publiczny UI (static SSR, `PublicLayout`):** `/shop` (listing aktywnych produktów), `/shop/{id}` (detail + formularz zakupu: e-mail + imię → POST do `/api/checkout`), `/shop/thank-you` (podziękowanie po płatności).
- **Klientka UI:** `/shop/my-orders` (moje zamówienia z datami i statusami).
- **NavMenu:** sekcja Sklep dla Ownera (Produkty, Zamówienia) i klientki (Moje zamówienia) gated `ShopEnabled`. Usunięty badge „wkrótce" z togglea Shop w `/admin/site`.
- **Migracja:** `20260718120000_AddShop` — tabele `Products` i `Orders`, FK `Products.CourseId → AcademyCourses` (SetNull), FK `Orders.ProductId → Products` (Restrict), indeks na `ExternalPaymentId`.

**Konfiguracja PayU:** w `appsettings.json` lub env vars: `PayU:BaseUrl`, `PayU:PosId`, `PayU:SecondKey`, `PayU:ClientId`, `PayU:ClientSecret`. Sandbox: `https://secure.snd.payu.com`. Produkcja: `https://secure.payu.com`.

**Na później:** dodanie Stripe/Klarna (analogicznie do `PayUGateway`, osobna implementacja `IPaymentGateway`), konfiguracja bramki z admin UI, wysyłka maili z danymi logowania (dziś: hasło tymczasowe zapisane w `Order.Notes`), Fakturownia webhook.

### ✅ Krok 3 — Welcome Page (zrobione — czeka na weryfikację CI)

- Publiczny landing pod `/` — **statyczny SSR** (bez `@rendermode`), więc anonimowi goście nie otwierają obwodu SignalR (oszczędność RAM na 4GB VPS).
- Osobny `PublicLayout` (bez sidebara, górny pasek z logo + „Zaloguj się", stopka).
- Treść w pełni edytowalna z panelu Ownera (`/admin/site`): hero (nagłówek, podtytuł, obrazek, CTA), dowolny blok HTML, e-mail kontaktowy.
- Zalogowany użytkownik wchodząc na `/` jest przekierowany do `/panel`. Anonimowy przy wyłączonym module Welcome → `/Account/Login`.
- **Dashboard przeniesiony z `/` na `/panel`** (Home.razor) — zaktualizowane linki w NavMenu (brand + Dashboard) i mobilnej nawigacji.

### ✅ Krok 4 — Feature flags + config panel (zrobione — czeka na weryfikację CI)

- Encja-singleton `SiteSettings` (Id=1) z przełącznikami modułów: Welcome / Scheduler / Academy / Shop. `ISiteSettingsService` get-or-create (leniwe tworzenie wiersza z domyślnymi wartościami).
- Panel `/admin/site` (`Admin,Trainer`) — przełączniki modułów + edytor treści landingu.
- `NavMenu` respektuje flagi: wyłączony moduł znika z menu (Kalendarz/Pierwsza wizyta pod `SchedulerEnabled`, Kursy/Zapisy/Moje kursy pod `AcademyEnabled`).
- Migracja `20260717130000_AddSiteSettings` (ręczna).

**Zostało w tym kroku (na potem):** konfiguracja bramek płatności (przy Commerce), rozbudowa `Branding` (favicon/meta).

### ✅ Krok 5 — Scheduler integration (zrobione — czeka na weryfikację CI)

**Cel:** wpięcie schedulera w nowy model ról i tenancy — trener widzi tylko swoich klientów i dane, nie globalne.

Zaimplementowane:

- **Calendar auth fix:** `Calendar.razor` zmieniony z `[Authorize]` na `[Authorize(Roles = "Admin,Trainer,Subordinate")]` — klientka nie ma dostępu do kalendarza trenera.
- **Client.TrainerUserId wiring:** dodano `TrainerUserId` do `CreateClientDto`, ustawiane w `ClientService.CreateClientAsync`. `ClientNew.razor` pobiera `currentUserId` z `AuthenticationStateProvider` i przekazuje jako `TrainerUserId` przy tworzeniu klienta.
- **Dashboard scoping:**
  - `GetPendingClientsAsync(trainerUserId)` — filtruje oczekujących klientów po `Client.TrainerUserId`.
  - `GetExpiringAsync(daysAhead, trainerUserId)` — filtruje wygasające pakiety po `Client.TrainerUserId`.
  - `TrainerDashboard.razor` przekazuje `TrainerUserId` do obu metod.
- **GetUpcomingAsync fix:** dodano rozwinięcie o subordinate'ów (analogicznie do `GetSessionsAsync`), trener widzi sesje swoje + swoich asystentów.

### ✅ Krok 6 — Module Guard (zrobione — czeka na weryfikację CI)

**Cel:** twarde blokowanie tras wyłączonych modułów — samo ukrycie linku w menu nie wystarczy, bo użytkownik może wpisać URL ręcznie.

Zaimplementowane:

- **ModuleGuardMiddleware** — middleware ASP.NET Core sprawdzający `SiteSettings` flagi przed każdym requestem. Mapowanie tras do modułów:
  - `/calendar`, `/clients`, `/trainer/intro-config`, `/my` → `SchedulerEnabled`
  - `/academy/*` → `AcademyEnabled`
  - `/shop/*`, `/api/checkout`, `/api/payu/notify` → `ShopEnabled`
- Wyłączony moduł → redirect do `/panel` (zalogowany) lub `/` (anonim). Endpointy API → 404.
- Zarejestrowany w `Program.cs` po `UseAntiforgery()`, przed endpointami.

---

## 6. Świadomie NIE robimy w MVP

Rzeczy z Publigo, które **nie wchodzą** do pierwszej wersji:

- Aplikacja mobilna z brandingiem (PWA wystarczy).
- Własny system fakturowy (używamy Fakturownia/inFakt via webhook).
- Program partnerski / afiliacja.
- Certyfikaty automatyczne.
- Znakowanie PDF-ów danymi kursanta.
- Wewnętrzna społeczność / forum (Discord/FB grupy załatwiają temat).
- Quizy i testy (chyba że trenerka bardzo poprosi).
- Upselle / cross-selle / BUMP-y w koszyku.
- Produkty fizyczne + integracja InPost.
- Subskrypcje rekurencyjne (na razie single purchase).
- VAT OSS dla sprzedaży zagranicznej.
- Wielojęzyczność.
- Live streaming wewnątrz aplikacji.

Wszystko powyżej można dodać później, jak produkt się przyjmie i będą realne
prośby od klientek Ana albo od kolejnych trenerek.

---

## 7. Zasady operacyjne

- **Deploy = push do `main`** → CI buduje obraz → Watchtower na VPS podciąga i restartuje kontener → migracje EF odpalają się przy starcie.
- **Backup:** dzienny `pg_dump` (endpoint `/admin/backup/download` już działa).
- **Monitoring:** Uptime Kuma pinguje healthcheck kontenera, alert na Telegram/Discord.
- **Logi:** Dozzle → live tail w przeglądarce.
- **Zmiany connection stringa:** przez `/admin/settings` (zapisuje do `connections.json`, hot-reload bez restartu).

---

## 8. Otwarte pytania / decyzje do podjęcia w trakcie

- Jeden kurs per klientka czy wiele niezależnych zapisów?
- Czy Owner ma widzieć zakupy/płatności historycznie z Publigo (import CSV) czy zaczynamy "od dziś"?
- Provider mailingowy dla transakcyjnych maili (potwierdzenie zakupu, dane logowania) — SMTP Fastmail? Resend? Postmark? Brevo?
- Domena aplikacji: subdomena (`app.zmotywowana-ana.pl`) czy własna (`panel.zmotywowana-ana.pl`)? Marketingowy landing pozostaje na `zmotywowana-ana.pl`?

---

*Ostatnia aktualizacja: koniec Kroku 6. Wszystkie kroki 0–6 zaimplementowane.*
