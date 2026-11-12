#!/usr/bin/env bash
set -euo pipefail

# ─── PTScheduler — aktualizacja Portalu bez docker compose (np. Unraid) ──────
# Kontener ptportal tworzy Guardian zwykłym „docker run”, więc compose go nie zna. Skrypt:
#   1. znajduje repozytorium po wolumenie kontenera, pobiera zmiany (fast-forward),
#   2. buduje obraz ptportal:latest (SKIP_BUILD=1 — bez budowania, sam restart z nową konfiguracją),
#   3. odtwarza kontener z tą samą konfiguracją (zmienne, wolumeny, sieci, porty),
#   4. sprawdza /health — jeśli nowy nie wstanie, przywraca poprzedni kontener.
#
# Awaryjny dostęp do Panelu (nowe hasło administratora w logach):
#   RECOVER_ADMIN=admin@twojadomena.pl bash deploy/update-portal.sh
# Po zalogowaniu — zdjęcie zmiennej (bez budowania):
#   SKIP_BUILD=1 bash deploy/update-portal.sh
# ──────────────────────────────────────────────────────────────────────────────

CONTAINER="${CONTAINER:-ptportal}"
IMAGE="${IMAGE:-ptportal:latest}"
REPO_TARGET="/opt/ptscheduler/repo"
RECOVER_ADMIN="${RECOVER_ADMIN:-}"

say()  { printf '\033[1;34m▶\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m✔\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m✖\033[0m %s\n' "$*" >&2; exit 1; }

command -v docker >/dev/null || fail "Brak polecenia docker."
docker inspect "$CONTAINER" >/dev/null 2>&1 || fail "Nie ma kontenera '$CONTAINER'. Podaj nazwę: CONTAINER=nazwa bash $0"

# ── 1. Repozytorium i obraz ────────────────────────────────────────────────
REPO_DIR="${REPO_DIR:-$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "'"$REPO_TARGET"'"}}{{.Source}}{{end}}{{end}}' "$CONTAINER")}"
if [[ -z "$REPO_DIR" || ! -d "$REPO_DIR/.git" ]]; then
    SCRIPT_REPO="$(cd "$(dirname "$0")/.." && pwd)"
    [[ -d "$SCRIPT_REPO/.git" ]] && REPO_DIR="$SCRIPT_REPO" || fail "Nie znalazłem repozytorium. Podaj: REPO_DIR=/ścieżka bash $0"
fi
ok "Repozytorium: $REPO_DIR"

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
    say "Pobieram zmiany..."
    BRANCH="$(git -C "$REPO_DIR" rev-parse --abbrev-ref HEAD)"
    git -C "$REPO_DIR" fetch origin "$BRANCH"
    git -C "$REPO_DIR" merge --ff-only "origin/$BRANCH" || fail "Repozytorium ma lokalne zmiany — sprawdź: git -C $REPO_DIR status"
    ok "Kod: $(git -C "$REPO_DIR" log -1 --format='%h %s')"

    say "Buduję obraz $IMAGE (kilka minut)..."
    docker build -t "$IMAGE" \
        --build-arg BUILD_COMMIT="$(git -C "$REPO_DIR" rev-parse HEAD)" \
        --build-arg BUILD_TIME="$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        --build-arg BUILD_BRANCH="$(git -C "$REPO_DIR" rev-parse --abbrev-ref HEAD)" \
        -f "$REPO_DIR/PTScheduler.Portal/Dockerfile" "$REPO_DIR"
    ok "Obraz zbudowany."
fi

# ── 2. Konfiguracja obecnego kontenera ─────────────────────────────────────
ARGS=(--name "$CONTAINER" -d)

RESTART="$(docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$CONTAINER")"
[[ -n "$RESTART" && "$RESTART" != "no" ]] && ARGS+=(--restart "$RESTART") || ARGS+=(--restart unless-stopped)

while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    case "$line" in
        PATH=*|DOTNET_*|ASPNET_VERSION=*|APP_UID=*|HOME=*|HOSTNAME=*|PTS_BUILD_*) continue ;; # z obrazu
        Portal__RecoverAdmin=*) continue ;; # dokładamy niżej tylko na życzenie
    esac
    ARGS+=(-e "$line")
done < <(docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$CONTAINER")
if [[ -n "$RECOVER_ADMIN" ]]; then
    ARGS+=(-e "Portal__RecoverAdmin=$RECOVER_ADMIN")
    ok "Odzyskiwanie dostępu dla: $RECOVER_ADMIN"
fi

while IFS='|' read -r type src dst rw; do
    [[ -z "$dst" ]] && continue
    spec="$src:$dst"
    [[ "$rw" == "false" ]] && spec="$spec:ro"
    ARGS+=(-v "$spec")
done < <(docker inspect -f '{{range .Mounts}}{{.Type}}|{{if eq .Type "volume"}}{{.Name}}{{else}}{{.Source}}{{end}}|{{.Destination}}|{{.RW}}{{println}}{{end}}' "$CONTAINER")

while IFS= read -r p; do
    [[ -n "$p" ]] && ARGS+=(-p "$p")
done < <(docker inspect -f '{{range $cp, $binds := .HostConfig.PortBindings}}{{range $binds}}{{if .HostIp}}{{.HostIp}}:{{end}}{{.HostPort}}:{{$cp}}{{println}}{{end}}{{end}}' "$CONTAINER")

PRIMARY_NET="$(docker inspect -f '{{.HostConfig.NetworkMode}}' "$CONTAINER")"
[[ -n "$PRIMARY_NET" && "$PRIMARY_NET" != "default" ]] && ARGS+=(--network "$PRIMARY_NET")
EXTRA_NETS="$(docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{println $k}}{{end}}' "$CONTAINER" | grep -vx "$PRIMARY_NET" || true)"

HOSTS="$(docker inspect -f '{{range .HostConfig.ExtraHosts}}{{println .}}{{end}}' "$CONTAINER")"
grep -q '^host.docker.internal:' <<<"$HOSTS" || ARGS+=(--add-host "host.docker.internal:host-gateway")
while IFS= read -r h; do [[ -n "$h" ]] && ARGS+=(--add-host "$h"); done <<<"$HOSTS"

# ── 3. Podmiana z możliwością powrotu ──────────────────────────────────────
BACKUP="$CONTAINER-old-$(date +%Y%m%d%H%M%S)"
say "Zatrzymuję obecny Portal (zostaje jako $BACKUP)..."
docker stop -t 20 "$CONTAINER" >/dev/null
docker rename "$CONTAINER" "$BACKUP"

restore() {
    printf '\033[1;31m✖\033[0m %s — przywracam poprzedni kontener.\n' "$1" >&2
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    docker rename "$BACKUP" "$CONTAINER" && docker start "$CONTAINER" >/dev/null
    exit 1
}

say "Uruchamiam nowy Portal..."
docker run "${ARGS[@]}" "$IMAGE" >/dev/null || restore "Nowy kontener się nie uruchomił"
for net in $EXTRA_NETS; do docker network connect "$net" "$CONTAINER" || true; done

say "Sprawdzam, czy odpowiada (do 2 minut)..."
for _ in $(seq 1 60); do
    sleep 2
    [[ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER")" == "true" ]] || { docker logs --tail 30 "$CONTAINER" >&2; restore "Nowy Portal się zatrzymał"; }
    if docker exec "$CONTAINER" curl -fsS -m 3 http://localhost:8081/health >/dev/null 2>&1; then
        docker rm "$BACKUP" >/dev/null
        ok "Portal działa. Poprzedni kontener usunięty."
        if [[ -n "$RECOVER_ADMIN" ]]; then
            echo
            docker logs "$CONTAINER" 2>&1 | grep "ODZYSKIWANIE" | tail -1 || true
            echo
            printf '\033[1;33m!\033[0m Po zalogowaniu zdejmij odzyskiwanie: SKIP_BUILD=1 bash %s\n' "$0"
        fi
        exit 0
    fi
done
docker logs --tail 30 "$CONTAINER" >&2
restore "Nowy Portal nie odpowiedział w 2 minuty"
