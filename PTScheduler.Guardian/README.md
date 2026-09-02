# PTScheduler Guardian

Niezalezny mikroserwis Docker odpowiedzialny za bezpieczne aktualizacje portalu PTScheduler i obrazow trenerskich. Dziala jako watchdog — buduje, testuje, podmienia i automatycznie cofa zmiany jesli nowa wersja nie przechodzi health checka.

## Dlaczego Guardian?

Portal nie moze sam bezpiecznie aktualizowac swojego kontenera — jesli nowy obraz sie nie uruchomi, nie ma nikogo kto przywroci stary. Guardian jest niezaleznym procesem ktory:

1. Buduje nowy obraz **obok** dzialajacego portalu
2. Uruchamia kontener testowy i sprawdza czy startuje
3. Dopiero po pozytywnym tescie podmienia kontener produkcyjny
4. Jesli po podmianie portal nie odpowiada — automatycznie cofa do poprzedniej wersji

---

## Architektura

```
┌──────────────────────────────────────────────────────────┐
│                    Serwer (Kurvinox)                     │
│                                                          │
│  ┌─────────────┐   HTTP (port 8081)   ┌──────────────┐  │
│  │   Guardian   │◄────────────────────►│    Portal     │  │
│  │  (port 9090) │   X-Guardian-Secret  │  (ptportal)   │  │
│  └──────┬───────┘                      └──────┬───────┘  │
│         │                                     │          │
│         │ Docker Socket                       │          │
│         │ (/var/run/docker.sock)              │          │
│         ▼                                     ▼          │
│  ┌─────────────────────────────────────────────────────┐ │
│  │                Docker Engine                        │ │
│  │  ptportal, ptportal-db, ptguardian, pt-*-web/db     │ │
│  └─────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### Pipeline aktualizacji portalu (6 etapow)

```
Queued → Pulling → Building → Testing → Swapping → Verifying → Done
                                                        │
                                                    (fail?)
                                                        │
                                                        ▼
                                               Auto-Rollback
                                          (ptportal:previous)
```

| Etap | Co robi |
|------|---------|
| **Queued** | Sprawdza warunki wstepne (repo, kontener, commit) |
| **Pulling** | `git fetch origin` + `git pull origin {branch}` |
| **Building** | `docker build -t ptportal:pending` (10-15 min) |
| **Testing** | Uruchamia kontener testowy, czeka 20s na stabilnosc + health check |
| **Swapping** | Taguje `latest→previous`, zatrzymuje stary, taguje `pending→latest`, startuje nowy |
| **Verifying** | Sprawdza `/health` nowego portalu (max 90s) — jesli nie odpowiada → rollback |
| **Done** | Czysci obrazy tymczasowe |

---

## Instalacja na serwerze

### Wymagania

- Docker Engine 20+ z Docker Compose v2
- Git zainstalowany na hoscie
- Sklonowane repozytorium PTScheduler.Web
- Siec Docker `ptscheduler` (tworzona przez compose)

### Krok 1 — Przygotuj zmienne srodowiskowe

Edytuj plik `.env.prod` w katalogu repozytorium i dodaj:

```bash
# === Guardian (nowe zmienne) ===
GUARDIAN_SECRET=twoj-silny-secret-min-32-znaki
GUARDIAN_PORT=9090

# === Istniejace zmienne (upewnij sie ze sa ustawione) ===
PORTAL_DB_PASSWORD=twoje-haslo-db
BUILD_BRANCH=master
REPO_DIR=/opt/ptscheduler/repo
```

**Wygeneruj bezpieczny secret:**
```bash
openssl rand -hex 32
```

### Krok 2 — Pull najnowszego kodu

```bash
cd /opt/ptscheduler/repo    # albo gdzie masz sklonowane repo
git fetch origin
git pull origin master
```

### Krok 3 — Zbuduj i uruchom calego stacka

```bash
docker compose -f docker-compose.prod.yml up -d --build
```

To zbuduje i uruchomi:
- `ptportal-db` — PostgreSQL
- `ptportal` — Portal PTScheduler
- `ptguardian` — Guardian (nowy)

**Albo zbuduj samego Guardiana** (jesli portal juz dziala):

```bash
docker compose -f docker-compose.prod.yml up -d --build guardian
```

### Krok 4 — Sprawdz czy Guardian dziala

```bash
# Health check (bez autoryzacji)
curl http://localhost:9090/health

# Odpowiedz:
# {"status":"healthy","portalHealthy":true,"uptime":"0.00:01:23"}

# Status API (wymaga secretu)
curl -H "X-Guardian-Secret: twoj-secret" http://localhost:9090/api/status
```

### Krok 5 — Polacz Portal z Guardianem

Wejdz do portalu PTScheduler → **Panel admina** → **Ustawienia** i ustaw:

| Ustawienie | Wartosc |
|------------|---------|
| `guardian_url` | `http://ptguardian:9090` |
| `guardian_secret` | ten sam secret co w `.env.prod` |

Alternatywnie — jesli Portal jest w tym samym Docker network co Guardian, domyslny URL `http://ptguardian:9090` powinien dzialac automatycznie.

### Krok 6 — Przetestuj polaczenie

Przejdz do **Panel** → **Aktualizacje** w portalu. Na gorze powinna pojawic sie zielona plakietka:

```
✓ Guardian: Polaczony
```

---

## Interfejsy zarzadzania

Guardian mozna obsugiwac na **dwa sposoby**:

### 1. Z poziomu Portalu (zalecane)

**Panel → Aktualizacje** — pelna integracja:
- Status Guardiana (polaczony / niedostepny)
- Wersje portalu, obrazu trenera, repozytorium
- Przycisk "Aktualizuj przez Guardiana" — uruchamia bezpieczny pipeline
- Przycisk "Rollback Portal" — przywraca poprzednia wersje
- Live progress — pasek postepu z autoodswiezaniem co 3s
- Historia aktualizacji z rozwijalnymi logami

### 2. Panel awaryjny Guardiana

Dostepny pod `http://twoj-serwer:9090` — ciemny interfejs terminalowy.

Uzyj go gdy:
- Portal nie dziala (crashuje, nie startuje)
- Chcesz wykonac rollback gdy portal jest nieosiagalny
- Potrzebujesz diagnostyki bezposrednio z Guardiana

Logowanie: wpisz Guardian Secret i kliknij "Autoryzuj".

---

## Zmienne srodowiskowe Guardiana

| Zmienna | Opis | Domyslna |
|---------|------|----------|
| `GUARDIAN_SECRET` | Secret do autoryzacji API | **wymagany** |
| `GUARDIAN_PORTAL_URL` | URL portalu (wewnatrz sieci Docker) | `http://ptportal:8081` |
| `GUARDIAN_BRANCH` | Branch git do pullowania | `master` |
| `GUARDIAN_LOG_DIR` | Katalog na logi JSON | `/opt/ptscheduler/guardian/logs` |
| `GUARDIAN_REPO_DIR` | Sciezka do repozytorium | `/opt/ptscheduler/repo` |
| `GUARDIAN_PORTAL_CONTAINER` | Nazwa kontenera portalu | `ptportal` |
| `GUARDIAN_PORTAL_IMAGE` | Obraz portalu | `ptportal:latest` |
| `GUARDIAN_TENANT_IMAGE` | Obraz trenera | `ptscheduler-web:latest` |
| `GUARDIAN_PORTAL_PORT` | Port wewnetrzny portalu | `8081` |

---

## API Endpoints

Wszystkie endpointy `/api/*` wymagaja naglowka `X-Guardian-Secret`.

| Metoda | Endpoint | Opis |
|--------|----------|------|
| GET | `/health` | Health check (bez auth) |
| GET | `/api/status` | Status Guardiana, portalu, aktywne operacje |
| POST | `/api/upgrade/portal` | Rozpocznij aktualizacje portalu |
| POST | `/api/upgrade/tenant` | Rozpocznij rebuild obrazu trenera |
| POST | `/api/upgrade/tenant?rebuild=false` | Tylko git pull, bez buildu |
| POST | `/api/upgrade/tenants/rolling` | Rolling update kontenerow trenerskich (JSON body) |
| GET | `/api/upgrade/active` | Aktywna operacja (jesli jest) |
| GET | `/api/upgrade/jobs/{id}` | Szczegoly konkretnej operacji |
| GET | `/api/upgrade/history?limit=20` | Historia operacji |
| POST | `/api/rollback/portal` | Rollback portalu do ptportal:previous |

### Przyklad — aktualizacja portalu z CLI

```bash
SECRET="twoj-secret"

# Rozpocznij aktualizacje
curl -X POST -H "X-Guardian-Secret: $SECRET" http://localhost:9090/api/upgrade/portal
# {"started":true,"jobId":"20260902120000-portal"}

# Sledz postep
curl -H "X-Guardian-Secret: $SECRET" http://localhost:9090/api/upgrade/active
# {"active":true,"job":{"id":"...","stage":"Building","status":"Running","log":[...]}}

# Po zakonczeniu — sprawdz historie
curl -H "X-Guardian-Secret: $SECRET" http://localhost:9090/api/upgrade/history?limit=5
```

---

## Struktura plikow

```
PTScheduler.Guardian/
├── Dockerfile                     # Multi-stage build (sdk → aspnet + git + docker-cli)
├── PTScheduler.Guardian.csproj    # .NET 10, jedyna zaleznosc: Docker.DotNet
├── Program.cs                     # Minimal API, DI, middleware auth, endpointy
├── Models.cs                      # DTO: UpgradeJob, LogEntry, GuardianStatus, enumy
├── Services/
│   ├── HealthWatcher.cs           # BackgroundService: sprawdza /health portalu co 30s
│   ├── LogStore.cs                # Persystencja logow w plikach JSON
│   └── UpgradeOrchestrator.cs     # Cala logika aktualizacji, testowania, rollbacku
└── wwwroot/
    └── index.html                 # Awaryjny panel webowy
```

---

## Scenariusze uzycia

### Normalna aktualizacja portalu

1. Push nowego kodu na branch `master` w GitHub
2. Wejdz do Portalu → Panel → Aktualizacje
3. Kliknij **"Aktualizuj Portal (Guardian)"**
4. Obserwuj progress na zywo (odswiezanie co 3s)
5. Po 10-15 min status zmieni sie na "Success"

### Aktualizacja obrazu trenera

1. Kliknij **"Buduj Obraz Trenera (Guardian)"** w Portalu
2. Guardian zbuduje nowy obraz `ptscheduler-web:latest`
3. Zaznacz "Reprovisioning po zakonczeniu" zeby zaktualizowac dzialajace kontenery trenerow

### Rolling update tenantow (10-30+ trenerow)

Bezpieczna aktualizacja wielu kontenerow trenerskich jednoczesnie:

1. Zbuduj nowy obraz trenera (opcja powyzej)
2. Zaznacz **"Rolling update"** w sekcji trenera
3. Ustaw concurrency (ile rownoczesnie, domyslnie 3)
4. Opcjonalnie zaznacz "Zatrzymaj po pierwszym bledzie"
5. Kliknij **"Aktualizuj aplikacje trenera"**

Guardian dla kazdego tenanta:
- Zapisuje konfiguracje kontenera (env, porty, sieci, mounty)
- Zatrzymuje i usuwa stary kontener
- Tworzy nowy z tym samym configiem ale nowym obrazem
- Sprawdza `/health` (max 60s)
- Jesli health check nie przejdzie — **automatyczny rollback** do poprzedniego obrazu

Wynik: per-tenant status (OK / Rollback / Blad / Pominieto) z progressem na zywo.

```bash
# Przyklad z CLI
curl -X POST -H "X-Guardian-Secret: $SECRET" \
     -H "Content-Type: application/json" \
     -d '{"tenants":[{"slug":"jan","port":5001},{"slug":"anna","port":5002}],"concurrency":3}' \
     http://localhost:9090/api/upgrade/tenants/rolling
```

### Rollback portalu

Jesli cos poszlo nie tak:

1. Kliknij **"Rollback Portal"** w Portalu lub panelu Guardiana
2. Guardian przywroci obraz `ptportal:previous`
3. Portal wstaje na poprzedniej wersji

**Automatyczny rollback**: Jesli po podmianie kontener portalu nie przechodzi health checka w 90s, Guardian automatycznie wraca do poprzedniej wersji.

### Portal nie dziala — uzyj panelu awaryjnego

1. Wejdz na `http://twoj-serwer:9090`
2. Wpisz Guardian Secret
3. Kliknij "Rollback Portal"
4. Jesli rollback tez nie pomoze — sprawdz logi w historii

---

## Logi i diagnostyka

Guardian przechowuje logi kazdej operacji jako pliki JSON:

```bash
# Na hoscie (w woluminie Docker):
docker exec ptguardian ls /opt/ptscheduler/guardian/logs/
# 20260902120000-portal.json
# 20260902143000-tenant.json

# Podejrzyj log konkretnej operacji:
docker exec ptguardian cat /opt/ptscheduler/guardian/logs/20260902120000-portal.json | python3 -m json.tool
```

Guardian co 30 sekund sprawdza health portalu. Jesli portal przestaje odpowiadac:
- Loguje zmiane stanu
- Pole `portalHealthy` w `/api/status` zmienia sie na `false`
- W panelu Portalu (jesli jeszcze dziala) pojawi sie czerwony status

---

## Bezpieczenstwo

- Cala komunikacja Guardian ↔ Portal odbywa sie przez naglowek `X-Guardian-Secret`
- Guardian nie ma bazy danych — zero danych wrazliwych do wycieku
- Docker socket jest montowany read-write (niezbedny do zarzadzania kontenerami)
- Panel awaryjny Guardiana powinien byc dostepny **tylko z sieci wewnetrznej** — nie wystawiaj portu 9090 na internet bez reverse proxy z autoryzacja
- Secret powinien miec minimum 32 znaki — uzyj `openssl rand -hex 32`

### Firewall (zalecane)

```bash
# Zablokuj dostep do Guardiana z zewnatrz (tylko localhost / siec wewnetrzna)
ufw allow from 192.168.0.0/16 to any port 9090
ufw deny 9090
```

Albo w Nginx Proxy Manager: nie twórz proxy hosta dla portu 9090.

---

## Rozwiazywanie problemow

### Guardian nie widzi portalu

```bash
# Sprawdz czy oba kontenery sa w tej samej sieci
docker network inspect ptscheduler

# Sprawdz polaczenie z kontenera Guardiana
docker exec ptguardian curl -s http://ptportal:8081/health
```

### Docker build nie dziala w Guardianie

```bash
# Sprawdz czy docker.sock jest zamontowany
docker exec ptguardian docker ps

# Sprawdz czy repozytorium jest zamontowane
docker exec ptguardian ls /opt/ptscheduler/repo/
```

### Guardian nie startuje

```bash
# Sprawdz logi kontenera
docker logs ptguardian --tail 50

# Najczestszy problem: brak GUARDIAN_SECRET w .env.prod
```

### Rollback nie dziala

```bash
# Sprawdz czy istnieje obraz previous
docker images | grep ptportal
# ptportal  latest    abc123  ...
# ptportal  previous  def456  ...    ← musi istniec

# Jesli nie ma "previous" — rollback nie jest mozliwy
# Obraz "previous" tworzony jest automatycznie przy kazdej aktualizacji
```

---

## Wymagania systemowe

| Zasob | Minimum |
|-------|---------|
| RAM (Guardian idle) | ~30-50 MB |
| RAM (podczas buildu) | +1-2 GB (docker build) |
| Dysk | ~500 MB na logi i obrazy tymczasowe |
| Siec | Doker bridge `ptscheduler` |
