#!/usr/bin/env bash
# PTScheduler — odtworzenie platformy na NOWYM serwerze z kopii Portalu.
#
# Użycie (z katalogu repozytorium, po git clone):
#   bash deploy/restore.sh portal_20261002_030000.tar.enc [--tenants KATALOG] [--yes]
#
#   plik         kopia Portalu z Portalu/Guardiana: .tar, .tar.enc, .sql.gz albo .sql.gz.enc
#                (z innego serwera SFTP albo pobrana z Dysku Google)
#   --tenants    opcjonalnie: katalog z kopiami trenerów (slug_….tar) — potrzebny tylko,
#                gdy kopia poza serwerem była wyłączona; inaczej Portal pobierze je sam
#   --yes        bez pytań (np. gdy baza Portalu na tym serwerze nie jest pusta)
#
# Co robi:
#   1. odszyfrowuje kopię (pyta o hasło szyfrowania kopii),
#   2. odtwarza .env.prod z kopii (jeśli jeszcze go nie ma),
#   3. uruchamia bazę Portalu i wgrywa do niej kopię,
#   4. buduje i uruchamia Portal, Guardiana i obraz aplikacji trenerów,
#   5. podpowiada ostatni krok: Portal → Kopie zapasowe → „Odtwórz trenerów bez kontenerów”.
set -euo pipefail

cd "$(dirname "$0")/.."
COMPOSE_FILE=docker-compose.prod.yml
ENV_FILE=.env.prod
DB_CONTAINER=ptportal-db

say()  { printf '\n\033[1;34m▶ %s\033[0m\n' "$*"; }
ok()   { printf '  \033[32m✓ %s\033[0m\n' "$*"; }
warn() { printf '  \033[33m! %s\033[0m\n' "$*"; }
die()  { printf '\n\033[31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

BACKUP=""; TENANTS_DIR=""; YES=0
while [ $# -gt 0 ]; do
    case "$1" in
        --tenants) TENANTS_DIR="${2:-}"; shift 2 ;;
        --yes|-y) YES=1; shift ;;
        -h|--help) sed -n '2,20p' "$0"; exit 0 ;;
        *) BACKUP="$1"; shift ;;
    esac
done
[ -n "$BACKUP" ] || die "Podaj plik kopii Portalu, np.: bash deploy/restore.sh portal_20261002_030000.tar.enc"
[ -f "$BACKUP" ] || die "Nie ma pliku $BACKUP"
[ -z "$TENANTS_DIR" ] || [ -d "$TENANTS_DIR" ] || die "Nie ma katalogu $TENANTS_DIR"
[ -f "$COMPOSE_FILE" ] || die "Uruchom skrypt w katalogu repozytorium (brak $COMPOSE_FILE)."

confirm() {
    [ "$YES" = 1 ] && return 0
    read -r -p "  $1 [t/N] " answer
    [[ "$answer" =~ ^[tTyY]$ ]]
}

say "Sprawdzam narzędzia"
for tool in docker tar gzip; do command -v "$tool" >/dev/null || die "Brak programu: $tool"; done
docker compose version >/dev/null 2>&1 || die "Brak docker compose (wtyczka compose do Dockera)."
docker info >/dev/null 2>&1 || die "Docker nie działa albo brak uprawnień (uruchom jako root lub dodaj użytkownika do grupy docker)."
ok "Docker i docker compose są dostępne"

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# ── 1. Odszyfrowanie i rozpakowanie ──────────────────────────────────────────
FILE="$BACKUP"
if [[ "$BACKUP" == *.enc ]]; then
    say "Odszyfrowuję kopię"
    command -v openssl >/dev/null || die "Brak programu openssl (apt install openssl)."
    read -r -s -p "  Hasło szyfrowania kopii: " PTS_BACKUP_PASS; echo
    export PTS_BACKUP_PASS
    FILE="$WORK/$(basename "${BACKUP%.enc}")"
    openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -in "$BACKUP" -out "$FILE" -pass env:PTS_BACKUP_PASS \
        || die "Nie da się odszyfrować — złe hasło albo uszkodzony plik."
    unset PTS_BACKUP_PASS
    ok "Odszyfrowano"
fi

DUMP="$WORK/database.sql.gz"
if [[ "$FILE" == *.tar ]]; then
    tar -xf "$FILE" -C "$WORK" || die "Nie da się rozpakować kopii."
    [ -f "$DUMP" ] || die "W kopii nie ma database.sql.gz — to nie jest kopia Portalu?"
    if [ -f "$WORK/manifest.json" ]; then
        grep -q '"kind": *"portal"' "$WORK/manifest.json" || die "To kopia trenera, nie Portalu. Kopie trenerów odtwarza Portal."
        ok "Kopia: $(grep -o '"createdAtUtc": *"[^"]*"' "$WORK/manifest.json" | cut -d'"' -f4) UTC, zrobił: $(grep -o '"createdBy": *"[^"]*"' "$WORK/manifest.json" | cut -d'"' -f4)"
    fi
else
    cp "$FILE" "$DUMP"
    warn "Starsza kopia — sama baza, bez konfiguracji platformy."
fi
gzip -t "$DUMP" || die "Zrzut bazy w kopii jest uszkodzony."

# ── 2. Konfiguracja ─────────────────────────────────────────────────────────
say "Konfiguracja platformy ($ENV_FILE)"
if [ -f "$ENV_FILE" ]; then
    ok "$ENV_FILE już jest — zostawiam go bez zmian"
else
    for candidate in env/env.prod env/env env/containers.env; do
        if [ -f "$WORK/$candidate" ]; then
            cp "$WORK/$candidate" "$ENV_FILE"; chmod 600 "$ENV_FILE"
            ok "Odtworzono $ENV_FILE z kopii ($candidate)"
            [ "$candidate" = env/containers.env ] && warn "Konfiguracja odczytana z kontenerów — sprawdź $ENV_FILE (np. FORWARD_HOST = adres tego serwera)."
            break
        fi
    done
    [ -f "$ENV_FILE" ] || die "Brak $ENV_FILE i nie ma go w kopii. Utwórz go (opis w docker-compose.prod.yml) i uruchom skrypt ponownie."
    # Plik z repozytorium mógł być niepełny (np. część zmiennych w Portainerze) — uzupełniamy z konfiguracji kontenerów.
    if [ -f "$WORK/env/containers.env" ]; then
        added=0
        while IFS= read -r line; do
            [[ "$line" =~ ^([A-Z_][A-Z0-9_]*)= ]] || continue
            grep -q "^${BASH_REMATCH[1]}=" "$ENV_FILE" && continue
            [ "$added" = 0 ] && printf '\n# Uzupełnione z konfiguracji kontenerów (restore.sh)\n' >> "$ENV_FILE"
            echo "$line" >> "$ENV_FILE"; added=$((added + 1))
        done < "$WORK/env/containers.env"
        [ "$added" = 0 ] || ok "Uzupełniono $added brakujących zmiennych z konfiguracji kontenerów"
    fi
fi
for required in PORTAL_DB_PASSWORD GUARDIAN_SECRET; do
    grep -q "^$required=" "$ENV_FILE" || die "W $ENV_FILE brakuje $required — dopisz ją i uruchom skrypt ponownie."
done
set -a; . "./$ENV_FILE"; set +a
DB_USER="${PORTAL_DB_USER:-ptportal}"
DB_NAME="${PORTAL_DB_NAME:-ptportal}"
[[ "$DB_USER" =~ ^[A-Za-z0-9_]+$ && "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] || die "Nietypowa nazwa bazy/użytkownika w $ENV_FILE."
compose() { docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" "$@"; }

# ── 3. Baza Portalu ─────────────────────────────────────────────────────────
say "Uruchamiam bazę Portalu"
compose up -d portal-db
for _ in $(seq 1 60); do
    docker exec "$DB_CONTAINER" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1 && break
    sleep 2
done
docker exec "$DB_CONTAINER" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1 || die "Baza Portalu nie wstała (docker logs $DB_CONTAINER)."
ok "Baza działa"

TABLES=$(docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -Atc "select count(*) from information_schema.tables where table_schema='public'" 2>/dev/null || echo 0)
if [ "${TABLES:-0}" -gt 0 ]; then
    warn "Baza Portalu na tym serwerze nie jest pusta ($TABLES tabel)."
    confirm "Zastąpić ją kopią?" || die "Przerwano — nic nie zmieniono."
fi

say "Wgrywam kopię bazy Portalu"
compose stop portal >/dev/null 2>&1 || true
docker exec "$DB_CONTAINER" psql -U "$DB_USER" -d postgres -q -v ON_ERROR_STOP=1 \
    -c "DROP DATABASE IF EXISTS \"$DB_NAME\" WITH (FORCE)" -c "CREATE DATABASE \"$DB_NAME\" OWNER \"$DB_USER\""
ERRORS=$(gunzip -c "$DUMP" | docker exec -i "$DB_CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -q -o /dev/null 2>&1 | grep -c 'ERROR:' || true)
[ "${ERRORS:-0}" -eq 0 ] && ok "Baza wgrana" || warn "Baza wgrana z $ERRORS błędami — sprawdź Portal po starcie."

# ── 4. Obrazy i kontenery ────────────────────────────────────────────────────
say "Buduję Portal, Guardiana i obraz aplikacji trenerów (to potrwa kilkanaście minut)"
export BUILD_COMMIT; BUILD_COMMIT=$(git rev-parse HEAD 2>/dev/null || echo unknown)
export BUILD_TIME; BUILD_TIME=$(date -u +%Y-%m-%dT%H:%M:%SZ)
compose build portal guardian
compose --profile build build tenant-image
ok "Obrazy gotowe"

say "Uruchamiam Portal i Guardiana"
compose up -d portal guardian
PORT="${PORTAL_PORT:-8081}"
for _ in $(seq 1 90); do
    curl -fsS -m 3 "http://127.0.0.1:$PORT/health" >/dev/null 2>&1 && break
    sleep 2
done
curl -fsS -m 3 "http://127.0.0.1:$PORT/health" >/dev/null 2>&1 && ok "Portal odpowiada na porcie $PORT" \
    || warn "Portal jeszcze nie odpowiada — sprawdź: docker logs ptportal"

# ── 5. Kopie trenerów (opcjonalnie) ─────────────────────────────────────────
if [ -n "$TENANTS_DIR" ]; then
    say "Kopiuję kopie trenerów do Portalu"
    count=0
    for f in "$TENANTS_DIR"/*.tar "$TENANTS_DIR"/*.sql.gz; do
        [ -f "$f" ] || continue
        name=$(basename "$f"); slug="${name%%_[0-9]*}"
        docker exec ptportal mkdir -p "/opt/ptscheduler/backups/tenants/$slug"
        docker cp "$f" "ptportal:/opt/ptscheduler/backups/tenants/$slug/$name" >/dev/null
        count=$((count + 1))
    done
    ok "Skopiowano $count plików (zaszyfrowane .enc odszyfruj wcześniej openssl-em)"
fi

cat <<EOF

$(printf '\033[1;32m')Portal odtworzony.$(printf '\033[0m')

Dalej:
  1. Uruchom Nginx Proxy Manager, jeśli jeszcze nie działa:
       docker compose -f deploy/docker-compose.infra.yml up -d
     i sprawdź w Portalu → Konfiguracja platformy, czy adres i dane NPM pasują do tego serwera.
  2. Zaloguj się do Portalu (http://ADRES-SERWERA:$PORT) tym samym kontem administratora co wcześniej.
  3. Kopie zapasowe → „Odtwórz trenerów bez kontenerów” — Portal założy instancje,
     pobierze ich kopie spoza serwera (SFTP / Dysk Google) i odtworzy bazy oraz pliki.
  4. Przestaw rekordy DNS domen (Portal i trenerzy) na adres tego serwera.
EOF
