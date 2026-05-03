# Deployment

Instrukcja wdrożenia PTScheduler na serwerze z Dockerem — w szczególności na Unraid.

---

## Wymagania

- Docker 24+
- PostgreSQL 15+ (lub kontener z obrazu `postgres:17-alpine`)
- Reverse proxy (Nginx Proxy Manager, Traefik, Caddy) — do terminacji TLS

Kontener aplikacji nasłuchuje na porcie `8080` przez HTTP. TLS należy terminować na poziomie reverse proxy.

---

## Docker Compose (standardowy serwer)

```bash
cp .env.example .env
# Edytuj .env — ustaw DB_PASSWORD i opcjonalnie APP_PORT
```

`.env`:
```env
DB_PASSWORD=twoje_silne_haslo
APP_PORT=8080
```

```bash
docker compose up -d --build
```

Aplikacja dostępna pod `http://localhost:8080` (lub wybranym porcie). Dane bazy i upload plików są persystowane w named volumes (`pgdata`, `uploads`).

### Aktualizacja

```bash
docker compose pull          # opcjonalne, jeśli używasz zdalnych obrazów
docker compose up -d --build
```

Migracje EF Core są stosowane automatycznie przy starcie aplikacji.

---

## Unraid (bez docker-compose)

Unraid nie obsługuje natywnie `docker-compose`. Uruchom bazę i aplikację jako dwa oddzielne kontenery.

### 1. Kontener PostgreSQL

W Unraid → Docker → Add Container:

| Pole | Wartość |
|------|---------|
| Name | `ptscheduler-db` |
| Repository | `postgres:17-alpine` |
| Network Type | `Custom: br0` (lub dedykowana sieć) |
| Port | `5432:5432` |

**Environment Variables:**

| Zmienna | Wartość |
|---------|---------|
| `POSTGRES_DB` | `ptscheduler` |
| `POSTGRES_USER` | `ptscheduler` |
| `POSTGRES_PASSWORD` | `twoje_silne_haslo` |

**Volume:**
```
/mnt/user/appdata/ptscheduler/postgres → /var/lib/postgresql/data
```

### 2. Budowanie obrazu aplikacji

Na lokalnym komputerze (lub na serwerze z Dockerem):

```bash
docker build -t ptscheduler:latest .
docker save ptscheduler:latest | gzip > ptscheduler.tar.gz
```

Prześlij `ptscheduler.tar.gz` na Unraid i załaduj:

```bash
docker load < ptscheduler.tar.gz
```

Lub skorzystaj z rejestru (GitHub Container Registry, Docker Hub):

```bash
docker pull ghcr.io/twoj-uzytkownik/ptscheduler:latest
```

### 3. Kontener aplikacji

W Unraid → Docker → Add Container:

| Pole | Wartość |
|------|---------|
| Name | `ptscheduler-web` |
| Repository | `ptscheduler:latest` |
| Network Type | `Custom: br0` (ta sama sieć co DB) |
| Port | `8080:8080` |

**Environment Variables:**

| Zmienna | Wartość |
|---------|---------|
| `ConnectionStrings__DefaultConnection` | `Host=ptscheduler-db;Port=5432;Database=ptscheduler;Username=ptscheduler;Password=twoje_silne_haslo` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

> Jeśli kontenery są w tej samej sieci Docker, użyj nazwy kontenera jako hosta (`ptscheduler-db`). Jeśli korzystasz z sieci `br0`, użyj IP kontenera bazy.

**Volume (upload plików):**
```
/mnt/user/appdata/ptscheduler/uploads → /app/wwwroot/branding
```

### 4. Reverse proxy na Unraid

Zalecany: **Nginx Proxy Manager** (dostępny w Community Applications).

Utwórz nowy Proxy Host:
- **Domain Names:** `ptscheduler.twojadomena.pl`
- **Forward Hostname/IP:** IP kontenera aplikacji lub `ptscheduler-web`
- **Forward Port:** `8080`
- **SSL:** włącz Let's Encrypt

---

## Pierwsze uruchomienie

1. Uruchom oba kontenery.
2. Przejdź do `http://serwer:8080/admin/demo`.
3. Kliknij **Reset bazy danych** i potwierdź — tworzy konto `root@admin.local` / `password`.
4. Zaloguj się i skonfiguruj branding, e-mail i utwórz pierwszych użytkowników.

---

## Backup i przywracanie

### Eksport

W panelu admina (`/admin/backup`) pobierz dump bazy PostgreSQL (`.sql`).

Lub ręcznie:

```bash
docker exec ptscheduler-db pg_dump -U ptscheduler ptscheduler > backup_$(date +%Y%m%d).sql
```

### Przywracanie

```bash
docker exec -i ptscheduler-db psql -U ptscheduler ptscheduler < backup_20260502.sql
```

---

## Zmienne środowiskowe — pełna lista

| Zmienna | Opis | Domyślna |
|---------|------|----------|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string | — (wymagana) |
| `ASPNETCORE_ENVIRONMENT` | `Production` / `Development` | `Production` |
| `ASPNETCORE_URLS` | Adresy nasłuchu | `http://+:8080` |
| `DB_PASSWORD` | Hasło DB (docker-compose) | — (wymagana) |
| `APP_PORT` | Port hosta (docker-compose) | `8080` |
