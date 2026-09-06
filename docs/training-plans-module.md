# Moduł: Kreator planów treningowych

> Dokument projektowy. Powstał w trakcie planowania modułu (rozmowa z właścicielem).
> Służy jako źródło prawdy przy implementacji — kolejne sesje mają go wczytać
> i kontynuować, zamiast odtwarzać decyzje od zera.
>
> **Status:** Faza 1 (fundament danych) zaimplementowana — patrz sekcja 7.
> Pozostałe fazy zaprojektowane, nie zaimplementowane. Decyzje poniżej są
> ustalone z właścicielem (Patryk), chyba że oznaczono jako „otwarte".

## 1. Cel i pozycjonowanie

Odpowiednik modułu „kreator planów treningowych" z aplikacji konkurencyjnych:
katalog ćwiczeń, budowanie planów, śledzenie postępów klienta, wykresy objętości,
dziennik aktywności. Ma być wygodny **na telefonie i na komputerze** (mobile-first
dla logowania na siłowni, desktop dla budowania planów).

Kluczowa zasada strategiczna: **właściciel produkuje ZERO treści wideo i ponosi
koszt bazy tylko RAZ.** Cały custom-content to praca i koszt trenera.

## 2. Decyzje ustalone

### 2.1 Baza ćwiczeń
- **Źródło startowe: Free Exercise DB** (~870 ćwiczeń ze zdjęciami).
  - Licencja **Unlicense (public domain)** → redystrybucja komercyjna dozwolona.
  - Repo: `yuhonas/free-exercise-db` (JSON + obrazy).
- **Tłumaczenie na PL**: warstwa polskich nazw i opisów (do wygenerowania jednorazowo
  jako dane seed). Oryginalny opis **EN zostaje** i rozwija się po kliknięciu flagi 🇬🇧.
- **Trener tworzy własne ćwiczenia** — z własnymi zdjęciami, wideo, opisami.

### 2.2 Wideo — strategia „zero produkcji"
- **Baza: linki YouTube (embed)**. Tylko osadzanie (`youtube.com/embed/<id>`),
  **nigdy pobieranie/re-hosting** (łamie ToS YouTube). Zero storage, zero kosztu.
- **Custom trenera: YouTube (link) LUB własny plik na Bunny.**
  - Bunny jest już zintegrowany (kursy) — reużywamy.
  - Wgrywanie na Bunny **liczone do limitu GB w planie trenera** (patrz 4).
- Właściciel nie kręci i nie hostuje żadnego wideo bazowego.

### 2.3 Widoczność i personalizacja ćwiczeń
- Flaga widoczności: **„moje"** (owner = trener) / **„publiczne"** (baza lub udostępnione).
- Trener może oznaczać ćwiczenia jako **„interesujące mnie"** (ulubione / szybki wybór).
- **„Ostatnio używane"** — automatycznie z użycia w planach.
- Filtr katalogu: moje / publiczne / wszystkie.

### 2.4 Zakres MVP
Katalog + kreator planu (serie/powt./ciężar) + logowanie wykonania przez klienta
na telefonie **+ wykresy objętości** (od razu, bo to mocny argument sprzedażowy).

## 3. Strategia przechowywania danych (warstwowa)

| Warstwa | Gdzie | Uzasadnienie |
|---|---|---|
| Zdjęcia bazy (Free Exercise DB) | **Współdzielone, raz** — w obrazie aplikacji lub własny CDN/hosting, read-only | ~kilkaset MB, identyczne dla wszystkich tenantów. NIE duplikować per-tenant. Koszt raz. |
| Wideo bazy | **Nigdzie** — tylko URL YouTube w bazie | Zero storage |
| Custom trenera (zdjęcia + wideo) | **Bunny, per-tenant, limit GB w planie** | Koszt przeniesiony na plan trenera. Bunny już zintegrowany. |
| Custom linki YT trenera | tylko URL | Zero storage |

**Sedno multi-tenant:** baza wspólna = koszt właściciela raz; custom = koszt trenera
przez limit GB. Koszty właściciela nie rosną z liczbą trenerów.

### Uwaga o katalogu bazowym w multi-tenant
Baza ćwiczeń jest **globalna/współdzielona i read-only** — te same rekordy dla
wszystkich tenantów. Rozważyć: czy trzymać jako dane seed w każdej bazie tenanta
(prościej, ale duplikacja ~870 rekordów × N tenantów — akceptowalne, to małe wiersze;
zdjęcia i tak współdzielone przez URL), czy w osobnym współdzielonym magazynie.
**Rekomendacja na start: seed do bazy tenanta** (wiersze są tanie; obrazy serwowane
z jednego współdzielonego miejsca po URL). Prościej niż osobna baza współdzielona.

## 4. Wpływ na plany (Entitlements)

- Dodać **limit GB na media ćwiczeń** per plan (np. `ExerciseMediaGb`) + **licznik
  zużycia Bunny per-tenant**. Wpina się w istniejący system planów/limitów
  (`EntitlementService`, `Limit(...)`).
- Rozważyć feature-flag `TrainingPlansEnabled` per plan (moduł jako element wyższego
  pakietu, jak inne funkcje w `AdminNavMenu` z `PlanBadge`).

## 5. Szkic modelu danych

> Wstępny — do doprecyzowania przy implementacji. Konwencja czasu wg
> `PTScheduler.Application/Interfaces/IAppClock.cs`: znaczniki utworzenia =
> instant (UTC); data treningu widziana przez człowieka = zegar ścienny / `DateOnly`.

### Exercise (ćwiczenie)
- `Id`
- `OwnerTrainerUserId` (string?, null = ćwiczenie bazowe/systemowe)
- `Visibility` (enum: Public | Mine)
- `NamePl`, `NameEn`
- `DescriptionPl`, `DescriptionEn` (EN pod flagą 🇬🇧)
- `PrimaryMuscles`, `SecondaryMuscles` (do wykresów „per partia") — z tagów Free Exercise DB
- `Equipment`, `Category`, `Level` (z Free Exercise DB)
- `ImageUrls` (lista — bazowe wskazują na współdzielony magazyn; custom na Bunny/URL)
- `VideoType` (enum: None | YouTube | Bunny), `VideoRef` (id/URL)
- `SourceKey` (string?, klucz z Free Exercise DB — do dedup przy re-seedzie)
- `CreatedAt` (instant)

### TrainerExercisePref (nakładka per-trener — nie duplikuje ćwiczeń)
- `TrainerUserId`, `ExerciseId`
- `IsFavorite` (bool) — „interesujące mnie"
- `LastUsedAt` (instant?, do „ostatnio używane")

### TrainingPlan → PlanDay → PlanExercise
- **TrainingPlan**: `Id`, `TrainerUserId`, `ClientId?`, `Name`, `Notes`, `CreatedAt`,
  ewentualnie `IsTemplate` (szablon vs przypisany klientowi)
- **PlanDay**: `Id`, `PlanId`, `Order`, `Label` (np. „Dzień A — push")
- **PlanExercise**: `Id`, `PlanDayId`, `ExerciseId`, `Order`, `Sets`, `Reps`,
  `TargetWeightKg?`, `Tempo?`, `RestSeconds?`, `Notes?`

### WorkoutLog (wykonanie klienta) → WorkoutSetLog
- **WorkoutLog**: `Id`, `ClientId`, `PlanExerciseId?` (lub luźne), `WorkoutDate`
  (`DateOnly` — dzień treningu, zegar ścienny), `CreatedAt` (instant)
- **WorkoutSetLog**: `Id`, `WorkoutLogId`, `SetNumber`, `Reps`, `WeightKg`
- **Objętość** = Σ(`Reps` × `WeightKg`) — agregowana po czasie i po partii mięśniowej
  (przez `Exercise.PrimaryMuscles`). Jednostki spójnie **kg / powtórzenia** od początku.

## 6. Mobile / offline

Klient loguje serie na siłowni, często przy słabym zasięgu. Aplikacja ma już **PWA** —
warto, żeby log wykonania działał **offline i syncował później** (local queue → sync).
To realny wyróżnik vs konkurencja. Do rozważenia w fazie 2/3, nie musi być w pierwszym MVP.

## 7. Fazy implementacji

1. **Fundament danych** ✅ (zrobione): encje (Exercise, TrainerExercisePref,
   TrainingPlan/Day/Exercise, WorkoutLog/SetLog) + enumy (ExerciseVisibility,
   ExerciseVideoType, ExerciseCategory, ExerciseLevel, MuscleGroup) + konfiguracja
   EF i migracja `20260906120000_AddTrainingModule` + reguły domenowe
   `Domain.Rules.Muscles` (parsowanie partii z CSV Free Exercise DB) i
   `Domain.Rules.VolumeCalculator` (objętość serii/wykonania i „per partia")
   z testami. **Do zrobienia w tej fazie osobno:** właściwy import ~870 ćwiczeń
   z Free Exercise DB (JSON) + tłumaczenia PL jako seed — model i klucz dedup
   (`Exercise.SourceKey`) już na to gotowe.
2. **Katalog ćwiczeń**: przeglądanie, wyszukiwanie, filtr moje/publiczne, ulubione,
   ostatnio używane, flaga 🇬🇧 dla EN. Dodawanie własnego ćwiczenia (zdjęcie/YT/Bunny).
3. **Kreator planu**: plan → dni → ćwiczenia (serie/powt./ciężar/tempo/przerwa),
   przypisanie klientowi, szablony.
4. **Logowanie wykonania (mobile-first)**: klient wpisuje serie; widok „dziś trenuję".
5. **Wykresy objętości + dziennik aktywności**: objętość w czasie, rekordy,
   centralny widok aktywności podopiecznych.
6. **Limit GB Bunny + entitlement** + ewentualnie offline PWA sync.

## 8. Licencje i zgodność — checklista

- Free Exercise DB: **Unlicense (public domain)** — OK do redystrybucji komercyjnej.
  Zachować atrybucję dobrym obyczajem (nie wymagana, ale warto w „O aplikacji").
- YouTube: **wyłącznie embed** przez oficjalny player. Nie pobierać, nie re-hostować,
  nie skrobać. Przechowywać tylko id/URL.
- Custom media trenera na Bunny: własność/odpowiedzialność trenera (zapis w regulaminie).

## 9. Pytania otwarte

- Duplikacja bazy per-tenant vs magazyn współdzielony — patrz 3 (rekomendacja: seed per-tenant).
- Czy plany mają być udostępnialne między trenerami (marketplace szablonów)? — poza MVP.
- Czy klient widzi wideo bazowe (YT) czy tylko trener? — domyślnie klient też (to instruktaż).
- Model rozliczenia limitu GB (twardy limit vs miękki z dopłatą) — do decyzji z cennikiem.
