#!/usr/bin/env bash
set -euo pipefail

# ─── PTScheduler SaaS — Upgrade All Tenants ──────────────────────────────────
# Usage:  ./upgrade.sh [--build-only] [--tenant <slug>]
#
# Steps:
#   1. Rebuilds ptscheduler-web:latest image from the repo
#   2. Restarts each tenant's web container (rolling, one at a time)
#   3. EF migrations run automatically on startup
# ──────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
TENANTS_DIR="$SCRIPT_DIR/tenants"

BUILD_ONLY=false
TARGET_TENANT=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --build-only) BUILD_ONLY=true; shift ;;
        --tenant) TARGET_TENANT="$2"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

echo "╔══════════════════════════════════════════════════════════╗"
echo "║  PTScheduler — Upgrade                                  ║"
echo "╚══════════════════════════════════════════════════════════╝"

# ── Rebuild image ────────────────────────────────────────────────────────────
echo ">>> Building ptscheduler-web:latest..."
docker build -t ptscheduler-web:latest "$REPO_ROOT"
echo "    ✓ Image rebuilt"

if $BUILD_ONLY; then
    echo ">>> --build-only: skipping tenant restarts"
    exit 0
fi

# ── Restart tenants ──────────────────────────────────────────────────────────
if [[ -n "$TARGET_TENANT" ]]; then
    DIRS=("$TENANTS_DIR/$TARGET_TENANT")
    if [[ ! -d "${DIRS[0]}" ]]; then
        echo "ERROR: Tenant '$TARGET_TENANT' not found"
        exit 1
    fi
else
    DIRS=("$TENANTS_DIR"/*)
fi

TOTAL=0
UPGRADED=0
FAILED=0

for TENANT_DIR in "${DIRS[@]}"; do
    [[ -f "$TENANT_DIR/docker-compose.yml" ]] || continue
    SLUG=$(basename "$TENANT_DIR")
    TOTAL=$((TOTAL + 1))

    echo ""
    echo ">>> Upgrading tenant: $SLUG"

    if docker compose -f "$TENANT_DIR/docker-compose.yml" \
        --project-name "pt-$SLUG" \
        up -d --force-recreate web 2>&1; then
        echo "    ✓ $SLUG upgraded"
        UPGRADED=$((UPGRADED + 1))
    else
        echo "    ✗ $SLUG FAILED"
        FAILED=$((FAILED + 1))
    fi
done

echo ""
echo "═══════════════════════════════════════════════════════════"
echo "  Upgraded: $UPGRADED/$TOTAL   Failed: $FAILED"
echo "═══════════════════════════════════════════════════════════"
