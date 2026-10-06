#!/usr/bin/env bash
set -euo pipefail

# ─── PTScheduler SaaS — Backup All Tenant Databases ─────────────────────────
# Usage:  ./backup.sh [backup-dir]
#
# Creates a timestamped pg_dump for each tenant.
# Default backup dir: deploy/backups/
#
# Recommended cron (daily at 3am):
#   0 3 * * * /path/to/deploy/backup.sh >> /var/log/ptscheduler-backup.log 2>&1
# ──────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TENANTS_DIR="$SCRIPT_DIR/tenants"
BACKUP_DIR="${1:-$SCRIPT_DIR/backups}"
TIMESTAMP=$(date -u +%Y%m%d_%H%M%S)

mkdir -p "$BACKUP_DIR"

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Backup started"

TOTAL=0
OK=0
FAIL=0

for TENANT_DIR in "$TENANTS_DIR"/*/; do
    [[ -f "$TENANT_DIR/docker-compose.yml" ]] || continue
    SLUG=$(basename "$TENANT_DIR")

    # Read DB password from .env
    DB_PASS=$(grep -oP 'DB_PASSWORD=\K.*' "$TENANT_DIR/.env" 2>/dev/null || echo "")
    if [[ -z "$DB_PASS" ]]; then
        echo "  ⚠ $SLUG: no DB_PASSWORD in .env, skipping"
        FAIL=$((FAIL + 1))
        continue
    fi

    TOTAL=$((TOTAL + 1))
    DUMP_FILE="$BACKUP_DIR/${SLUG}_${TIMESTAMP}.sql.gz"
    CONTAINER="pt-${SLUG}-db"

    echo -n "  $SLUG → $DUMP_FILE ... "

    if docker exec "$CONTAINER" \
        pg_dump -U ptscheduler -d ptscheduler --no-owner --no-acl \
        2>/dev/null | gzip > "$DUMP_FILE"; then

        SIZE=$(du -sh "$DUMP_FILE" | cut -f1)
        echo "✓ ($SIZE)"
        OK=$((OK + 1))
    else
        echo "✗ FAILED"
        rm -f "$DUMP_FILE"
        FAIL=$((FAIL + 1))
    fi
done

# ── Cleanup old backups (keep 30 days) ───────────────────────────────────────
find "$BACKUP_DIR" -name "*.sql.gz" -mtime +30 -delete 2>/dev/null || true

echo "[$(date -u +%Y-%m-%dT%H:%M:%SZ)] Backup finished: $OK/$TOTAL ok, $FAIL failed"
