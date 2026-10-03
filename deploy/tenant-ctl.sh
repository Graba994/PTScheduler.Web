#!/usr/bin/env bash
set -euo pipefail

# ─── PTScheduler SaaS — Tenant Control ──────────────────────────────────────
# Usage:  ./tenant-ctl.sh <command> [slug]
#
# Commands:
#   list                    — list all tenants with status
#   status  <slug>          — show tenant status
#   start   <slug>          — start tenant containers
#   stop    <slug>          — stop tenant containers
#   restart <slug>          — restart tenant containers
#   logs    <slug> [lines]  — show web container logs (default 100 lines)
#   shell   <slug>          — open bash in web container
#   dbshell <slug>          — open psql in db container
#   destroy <slug>          — stop and remove tenant (asks for confirmation)
# ──────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TENANTS_DIR="$SCRIPT_DIR/tenants"

CMD="${1:?Usage: $0 <command> [slug]}"
SLUG="${2:-}"
LINES="${3:-100}"

compose_cmd() {
    local slug="$1"; shift
    docker compose -f "$TENANTS_DIR/$slug/docker-compose.yml" \
        --project-name "pt-$slug" "$@"
}

case "$CMD" in

    list)
        echo ""
        printf "%-20s %-30s %-7s %-10s\n" "SLUG" "DOMAIN" "PORT" "STATUS"
        echo "─────────────────────────────────────────────────────────────────────"
        for DIR in "$TENANTS_DIR"/*/; do
            [[ -f "$DIR/.env" ]] || continue
            S=$(basename "$DIR")
            DOMAIN=$(grep -oP 'TENANT_DOMAIN=\K.*' "$DIR/.env" 2>/dev/null || echo "?")
            PORT=$(grep -oP 'APP_PORT=\K.*' "$DIR/.env" 2>/dev/null || echo "?")

            WEB_STATUS=$(docker inspect --format='{{.State.Status}}' "pt-${S}-web" 2>/dev/null || echo "not found")
            DB_STATUS=$(docker inspect --format='{{.State.Status}}' "pt-${S}-db" 2>/dev/null || echo "not found")

            if [[ "$WEB_STATUS" == "running" && "$DB_STATUS" == "running" ]]; then
                STATUS="● running"
            elif [[ "$WEB_STATUS" == "not found" ]]; then
                STATUS="○ stopped"
            else
                STATUS="◐ partial"
            fi

            printf "%-20s %-30s %-7s %-10s\n" "$S" "$DOMAIN" "$PORT" "$STATUS"
        done
        echo ""
        ;;

    status)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 status <slug>"; exit 1; }
        compose_cmd "$SLUG" ps
        ;;

    start)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 start <slug>"; exit 1; }
        echo "Starting $SLUG..."
        compose_cmd "$SLUG" up -d
        echo "✓ Started"
        ;;

    stop)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 stop <slug>"; exit 1; }
        echo "Stopping $SLUG..."
        compose_cmd "$SLUG" stop
        echo "✓ Stopped"
        ;;

    restart)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 restart <slug>"; exit 1; }
        echo "Restarting $SLUG..."
        compose_cmd "$SLUG" restart
        echo "✓ Restarted"
        ;;

    logs)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 logs <slug> [lines]"; exit 1; }
        compose_cmd "$SLUG" logs --tail="$LINES" -f web
        ;;

    shell)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 shell <slug>"; exit 1; }
        docker exec -it "pt-${SLUG}-web" bash || docker exec -it "pt-${SLUG}-web" sh
        ;;

    dbshell)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 dbshell <slug>"; exit 1; }
        docker exec -it "pt-${SLUG}-db" psql -U ptscheduler -d ptscheduler
        ;;

    destroy)
        [[ -n "$SLUG" ]] || { echo "Usage: $0 destroy <slug>"; exit 1; }
        TENANT_DIR="$TENANTS_DIR/$SLUG"
        [[ -d "$TENANT_DIR" ]] || { echo "Tenant '$SLUG' not found"; exit 1; }

        echo "⚠ This will STOP and REMOVE all containers and volumes for '$SLUG'."
        echo "  Database data will be LOST unless you have a backup."
        echo ""
        read -rp "Type the slug to confirm: " CONFIRM
        if [[ "$CONFIRM" != "$SLUG" ]]; then
            echo "Cancelled."
            exit 1
        fi

        compose_cmd "$SLUG" down -v
        rm -rf "$TENANT_DIR"

        # Remove from registry
        REGISTRY="$SCRIPT_DIR/tenants.csv"
        if [[ -f "$REGISTRY" ]]; then
            sed -i "/^${SLUG},/d" "$REGISTRY"
        fi

        echo "✓ Tenant '$SLUG' destroyed"
        ;;

    *)
        echo "Unknown command: $CMD"
        echo "Commands: list, status, start, stop, restart, logs, shell, dbshell, destroy"
        exit 1
        ;;
esac
