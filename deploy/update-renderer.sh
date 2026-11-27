#!/usr/bin/env bash
set -euo pipefail

# ─── Hantly — renderer Studia reklam (kontener hantly-renderer) ─────────────────
# Na co dzień robi to Guardian (Portal → Aktualizacje → Renderer filmów). Ten skrypt to ręczna droga:
#   1. pobiera zmiany w repozytorium (fast-forward) i buduje obraz hantly-renderer:latest ze studio/Dockerfile,
#   2. podmienia kontener (pierwszy raz — zakłada go): sieć Portalu, bez portów na zewnątrz,
#      dane = miejsce na filmy Portalu (/opt/ptscheduler/studio w Portalu → /data w rendererze),
#   3. sprawdza /health — jeśli nowy nie wstanie, przywraca poprzedni kontener.
#
#   REPO_DIR=/mnt/user/appdata/hantly-repo bash /mnt/user/appdata/hantly-repo/deploy/update-renderer.sh
# Opcje: SKIP_BUILD=1 (sam restart), STUDIO_DIR=/mnt/user/hantly-studio, NETWORK=ptscheduler.
# ────────────────────────────────────────────────────────────────────────────────

CONTAINER="${CONTAINER:-hantly-renderer}"
IMAGE="${IMAGE:-hantly-renderer:latest}"
PORTAL="${PORTAL:-ptportal}"
STUDIO_TARGET="/opt/ptscheduler/studio"

say()  { printf '\033[1;34m▶\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m✔\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m✖\033[0m %s\n' "$*" >&2; exit 1; }

command -v docker >/dev/null || fail "Brak polecenia docker."

REPO_DIR="${REPO_DIR:-$(cd "$(dirname "$0")/.." && pwd)}"
[[ -d "$REPO_DIR/.git" && -f "$REPO_DIR/studio/Dockerfile" ]] || fail "Nie znalazłem repozytorium z studio/Dockerfile. Podaj: REPO_DIR=/ścieżka bash $0"
ok "Repozytorium: $REPO_DIR"

# Miejsce na dane i sieć — tak jak ma Portal (chyba że podane ręcznie)
if docker inspect "$PORTAL" >/dev/null 2>&1; then
    STUDIO_DIR="${STUDIO_DIR:-$(docker inspect -f '{{range .Mounts}}{{if eq .Destination "'"$STUDIO_TARGET"'"}}{{.Source}}{{end}}{{end}}' "$PORTAL")}"
    NETWORK="${NETWORK:-$(docker inspect -f '{{.HostConfig.NetworkMode}}' "$PORTAL")}"
fi
[[ -n "${STUDIO_DIR:-}" ]] || fail "Portal nie ma podpiętego miejsca na filmy. Najpierw: STUDIO_DIR=/mnt/user/hantly-studio bash deploy/update-portal.sh"
[[ -n "${NETWORK:-}" && "$NETWORK" != "default" && "$NETWORK" != "bridge" ]] || NETWORK="ptscheduler"
mkdir -p "$STUDIO_DIR"
ok "Dane: $STUDIO_DIR · sieć: $NETWORK"

if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
    say "Pobieram zmiany..."
    BRANCH="$(git -C "$REPO_DIR" rev-parse --abbrev-ref HEAD)"
    git -C "$REPO_DIR" fetch origin "$BRANCH"
    git -C "$REPO_DIR" merge --ff-only "origin/$BRANCH" || fail "Repozytorium ma lokalne zmiany — sprawdź: git -C $REPO_DIR status"
    ok "Kod: $(git -C "$REPO_DIR" log -1 --format='%h %s')"

    say "Buduję obraz $IMAGE (pierwszy raz ok. 10 min — pobiera Chromium)..."
    docker build -t "$IMAGE" --build-arg BUILD_COMMIT="$(git -C "$REPO_DIR" rev-parse HEAD)" \
        -f "$REPO_DIR/studio/Dockerfile" "$REPO_DIR"
    ok "Obraz zbudowany."
fi

ARGS=(--name "$CONTAINER" -d --restart unless-stopped
      --network "$NETWORK" --network-alias "$CONTAINER"
      --memory 3g --shm-size 1g --cpus 3
      -e RENDER_CONCURRENCY=2
      --label pl.hantly.role=renderer
      -v "$STUDIO_DIR:/data")

BACKUP=""
if docker inspect "$CONTAINER" >/dev/null 2>&1; then
    BUSY="$(docker exec "$CONTAINER" curl -fsS -m 3 http://127.0.0.1:3000/health 2>/dev/null | grep -o '"busy":"[a-z]*"' || true)"
    [[ -z "$BUSY" ]] || fail "Renderer właśnie pracuje ($BUSY) — spróbuj, gdy skończy."
    BACKUP="$CONTAINER-old-$(date +%Y%m%d%H%M%S)"
    say "Zatrzymuję obecny renderer (zostaje jako $BACKUP)..."
    docker stop -t 15 "$CONTAINER" >/dev/null
    docker rename "$CONTAINER" "$BACKUP"
fi

restore() {
    printf '\033[1;31m✖\033[0m %s\n' "$1" >&2
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    if [[ -n "$BACKUP" ]]; then
        echo "Przywracam poprzedni renderer." >&2
        docker rename "$BACKUP" "$CONTAINER" && docker start "$CONTAINER" >/dev/null
    fi
    exit 1
}

say "Uruchamiam renderer..."
docker run "${ARGS[@]}" "$IMAGE" >/dev/null || restore "Nowy kontener się nie uruchomił"

say "Sprawdzam, czy odpowiada (do 2 minut)..."
for _ in $(seq 1 60); do
    sleep 2
    [[ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER")" == "true" ]] || { docker logs --tail 30 "$CONTAINER" >&2; restore "Nowy renderer się zatrzymał"; }
    if docker exec "$CONTAINER" curl -fsS -m 3 http://127.0.0.1:3000/health >/dev/null 2>&1; then
        [[ -z "$BACKUP" ]] || docker rm "$BACKUP" >/dev/null
        ok "Renderer działa ($(docker exec "$CONTAINER" curl -fsS -m 3 http://127.0.0.1:3000/health))."
        exit 0
    fi
done
docker logs --tail 30 "$CONTAINER" >&2
restore "Nowy renderer nie odpowiedział w 2 minuty"
