#!/usr/bin/env bash
set -euo pipefail

# ─── PTScheduler — aktualizacja Guardiana bez docker compose ─────────────────
# Dla serwerów bez wtyczki „docker compose” (np. Unraid). Skrypt:
#   1. znajduje repozytorium po wolumenie obecnego kontenera Guardiana,
#   2. pobiera zmiany (git fetch + fast-forward),
#   3. buduje nowy obraz ptguardian:latest,
#   4. odtwarza kontener z tą samą konfiguracją (zmienne, wolumeny, sieci, porty),
#      dokładając dostęp do hosta (host.docker.internal) potrzebny do sprawdzania instancji,
#   5. sprawdza /health — jeśli nowy nie wstanie, przywraca poprzedni kontener.
#
# Użycie (na serwerze):
#   bash /ścieżka/do/repo/deploy/update-guardian.sh
# Opcjonalnie: CONTAINER=ptguardian IMAGE=ptguardian:latest bash update-guardian.sh
# ──────────────────────────────────────────────────────────────────────────────

CONTAINER="${CONTAINER:-ptguardian}"
IMAGE="${IMAGE:-ptguardian:latest}"
REPO_TARGET="/opt/ptscheduler/repo"

say()  { printf '\033[1;34m▶\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m✔\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m✖\033[0m %s\n' "$*" >&2; exit 1; }

command -v docker >/dev/null || fail "Brak polecenia docker."
docker inspect "$CONTAINER" >/dev/null 2>&1 || fail "Nie ma kontenera '$CONTAINER'. Podaj nazwę: CONTAINER=nazwa bash $0"

# ── 1. Repozytorium ─────────────────────────────────────────────────────────
REPO_DIR="${REPO_DIR:-$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "'"$REPO_TARGET"'"}}{{.Source}}{{end}}{{end}}' "$CONTAINER")}"
if [[ -z "$REPO_DIR" || ! -d "$REPO_DIR/.git" ]]; then
    SCRIPT_REPO="$(cd "$(dirname "$0")/.." && pwd)"
    [[ -d "$SCRIPT_REPO/.git" ]] && REPO_DIR="$SCRIPT_REPO" || fail "Nie znalazłem repozytorium. Podaj: REPO_DIR=/ścieżka bash $0"
fi
ok "Repozytorium: $REPO_DIR"

say "Pobieram zmiany..."
BRANCH="$(git -C "$REPO_DIR" rev-parse --abbrev-ref HEAD)"
git -C "$REPO_DIR" fetch origin "$BRANCH"
if ! git -C "$REPO_DIR" merge --ff-only "origin/$BRANCH"; then
    fail "Repozytorium ma lokalne zmiany albo inną historię niż GitHub — sprawdź: git -C $REPO_DIR status"
fi
ok "Kod: $(git -C "$REPO_DIR" log -1 --format='%h %s')"

# ── 2. Obraz ────────────────────────────────────────────────────────────────
say "Buduję obraz $IMAGE (kilka minut)..."
docker build -t "$IMAGE" \
    --build-arg BUILD_COMMIT="$(git -C "$REPO_DIR" rev-parse HEAD)" \
    --build-arg BUILD_TIME="$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    -f "$REPO_DIR/PTScheduler.Guardian/Dockerfile" "$REPO_DIR"
ok "Obraz zbudowany."

# ── 3. Konfiguracja obecnego kontenera ─────────────────────────────────────
ARGS=(--name "$CONTAINER" -d)

RESTART="$(docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$CONTAINER")"
[[ -n "$RESTART" && "$RESTART" != "no" ]] && ARGS+=(--restart "$RESTART") || ARGS+=(--restart unless-stopped)

HAS_TENANT_HOST=0
while IFS= read -r line; do
    [[ -z "$line" ]] && continue
    case "$line" in
        PATH=*|DOTNET_*|ASPNET_VERSION=*|APP_UID=*|HOME=*|HOSTNAME=*) continue ;; # z obrazu
        GUARDIAN_TENANT_HOST=*) HAS_TENANT_HOST=1 ;;
    esac
    ARGS+=(-e "$line")
done < <(docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$CONTAINER")
[[ $HAS_TENANT_HOST -eq 0 ]] && ARGS+=(-e "GUARDIAN_TENANT_HOST=host.docker.internal")

while IFS='|' read -r type src dst rw; do
    [[ -z "$dst" ]] && continue
    if [[ "$type" == "volume" ]]; then spec="$src:$dst"; else spec="$src:$dst"; fi
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

# ── 4. Podmiana z możliwością powrotu ──────────────────────────────────────
BACKUP="$CONTAINER-old-$(date +%Y%m%d%H%M%S)"
say "Zatrzymuję obecnego Guardiana (zostaje jako $BACKUP)..."
docker stop -t 20 "$CONTAINER" >/dev/null
docker rename "$CONTAINER" "$BACKUP"

restore() {
    printf '\033[1;31m✖\033[0m %s — przywracam poprzedni kontener.\n' "$1" >&2
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    docker rename "$BACKUP" "$CONTAINER" && docker start "$CONTAINER" >/dev/null
    exit 1
}

say "Uruchamiam nowego Guardiana..."
docker run "${ARGS[@]}" "$IMAGE" >/dev/null || restore "Nowy kontener się nie uruchomił"
for net in $EXTRA_NETS; do docker network connect "$net" "$CONTAINER" || true; done

say "Sprawdzam, czy odpowiada..."
HOST_PORT="$(docker inspect -f '{{range $cp, $b := .HostConfig.PortBindings}}{{if eq $cp "9090/tcp"}}{{(index $b 0).HostPort}}{{end}}{{end}}' "$CONTAINER")"
for _ in $(seq 1 30); do
    sleep 2
    [[ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER")" == "true" ]] || { docker logs --tail 30 "$CONTAINER" >&2; restore "Nowy Guardian się zatrzymał"; }
    if docker exec "$CONTAINER" curl -fsS -m 3 http://localhost:9090/health >/dev/null 2>&1 \
       || { [[ -n "$HOST_PORT" ]] && curl -fsS -m 3 "http://127.0.0.1:$HOST_PORT/health" >/dev/null 2>&1; }; then
        docker rm "$BACKUP" >/dev/null
        ok "Guardian zaktualizowany i działa. Poprzedni kontener usunięty."
        { docker exec "$CONTAINER" curl -fsS http://localhost:9090/health 2>/dev/null \
          || { [[ -n "$HOST_PORT" ]] && curl -fsS "http://127.0.0.1:$HOST_PORT/health" 2>/dev/null; }; } >&1 || true
        echo
        exit 0
    fi
done
docker logs --tail 30 "$CONTAINER" >&2
restore "Nowy Guardian nie odpowiedział w 60 s"
