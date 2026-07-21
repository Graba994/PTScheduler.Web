#!/usr/bin/env bash
set -euo pipefail

# ─── PTScheduler SaaS — Tenant Provisioning ──────────────────────────────────
# Usage:  ./provision.sh <slug> <domain> [npm-api-url]
#
# Examples:
#   ./provision.sh jan-kowalski jan.ptscheduler.pl
#   ./provision.sh jan-kowalski jan.ptscheduler.pl http://localhost:81
#
# What it does:
#   1. Creates tenants/<slug>/ directory with docker-compose + .env
#   2. Generates a random DB password
#   3. Assigns next free port (starting at 9001)
#   4. Builds the shared ptscheduler-web image (if not yet built)
#   5. Starts the tenant containers (db + web)
#   6. Optionally registers a proxy host in Nginx Proxy Manager via API
#
# After provisioning, the app is available at http://localhost:<port>
# Default login: root@admin.local / password  (CHANGE IMMEDIATELY)
# ──────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
TENANTS_DIR="$SCRIPT_DIR/tenants"

SLUG="${1:?Usage: $0 <slug> <domain> [npm-api-url]}"
DOMAIN="${2:?Usage: $0 <slug> <domain> [npm-api-url]}"
NPM_API="${3:-}"

# ── Validate slug ────────────────────────────────────────────────────────────
if [[ ! "$SLUG" =~ ^[a-z0-9][a-z0-9-]*[a-z0-9]$ ]]; then
    echo "ERROR: Slug must be lowercase alphanumeric with hyphens (e.g. jan-kowalski)"
    exit 1
fi

TENANT_DIR="$TENANTS_DIR/$SLUG"

if [[ -d "$TENANT_DIR" ]]; then
    echo "ERROR: Tenant '$SLUG' already exists at $TENANT_DIR"
    exit 1
fi

# ── Find next free port ──────────────────────────────────────────────────────
BASE_PORT=9001
USED_PORTS=$(find "$TENANTS_DIR" -name '.env' -exec grep -h APP_PORT {} \; 2>/dev/null | cut -d= -f2 | sort -n)
PORT=$BASE_PORT
for USED in $USED_PORTS; do
    if [[ "$PORT" -eq "$USED" ]]; then
        PORT=$((PORT + 1))
    fi
done

# ── Generate DB password ─────────────────────────────────────────────────────
DB_PASS=$(openssl rand -base64 24 | tr -d '/+=' | head -c 32)

echo "╔══════════════════════════════════════════════════════════╗"
echo "║  PTScheduler — Provisioning Tenant                      ║"
echo "╠══════════════════════════════════════════════════════════╣"
echo "║  Slug:     $SLUG"
echo "║  Domain:   $DOMAIN"
echo "║  Port:     $PORT"
echo "║  Dir:      $TENANT_DIR"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# ── Create tenant directory ──────────────────────────────────────────────────
mkdir -p "$TENANT_DIR"

# ── Generate docker-compose.yml from template ────────────────────────────────
sed -e "s|{{TENANT_SLUG}}|$SLUG|g" \
    -e "s|{{TENANT_DOMAIN}}|$DOMAIN|g" \
    -e "s|{{APP_PORT}}|$PORT|g" \
    -e "s|{{DB_PASSWORD}}|$DB_PASS|g" \
    "$SCRIPT_DIR/docker-compose.tenant.template.yml" > "$TENANT_DIR/docker-compose.yml"

# ── Generate .env ────────────────────────────────────────────────────────────
cat > "$TENANT_DIR/.env" <<EOF
TENANT_SLUG=$SLUG
TENANT_DOMAIN=$DOMAIN
APP_PORT=$PORT
DB_PASSWORD=$DB_PASS
EOF

# ── Build shared image if needed ─────────────────────────────────────────────
if ! docker image inspect ptscheduler-web:latest &>/dev/null; then
    echo ">>> Building ptscheduler-web:latest image..."
    docker build -t ptscheduler-web:latest "$REPO_ROOT"
else
    echo ">>> Image ptscheduler-web:latest already exists (use upgrade.sh to rebuild)"
fi

# ── Start tenant ─────────────────────────────────────────────────────────────
echo ">>> Starting containers for '$SLUG'..."
docker compose -f "$TENANT_DIR/docker-compose.yml" \
    --project-name "pt-$SLUG" up -d

echo ">>> Waiting for web container to be healthy..."
for i in $(seq 1 30); do
    if curl -sf "http://localhost:$PORT" >/dev/null 2>&1; then
        echo "    ✓ Web is up on port $PORT"
        break
    fi
    sleep 2
done

# ── Register in NPM (optional) ───────────────────────────────────────────────
if [[ -n "$NPM_API" ]]; then
    echo ">>> Registering proxy host in Nginx Proxy Manager..."

    NPM_TOKEN_FILE="$SCRIPT_DIR/.npm-token"

    if [[ ! -f "$NPM_TOKEN_FILE" ]]; then
        echo "    NPM token not found. Logging in..."
        echo -n "    NPM email: "; read -r NPM_EMAIL
        echo -n "    NPM password: "; read -rs NPM_PASS; echo

        TOKEN=$(curl -sf "$NPM_API/api/tokens" \
            -H "Content-Type: application/json" \
            -d "{\"identity\":\"$NPM_EMAIL\",\"secret\":\"$NPM_PASS\"}" \
            | python3 -c "import sys,json;print(json.load(sys.stdin)['token'])")

        echo "$TOKEN" > "$NPM_TOKEN_FILE"
        chmod 600 "$NPM_TOKEN_FILE"
    fi

    TOKEN=$(cat "$NPM_TOKEN_FILE")
    SERVER_IP=$(hostname -I | awk '{print $1}')

    RESPONSE=$(curl -sf "$NPM_API/api/nginx/proxy-hosts" \
        -H "Authorization: Bearer $TOKEN" \
        -H "Content-Type: application/json" \
        -d "{
            \"domain_names\": [\"$DOMAIN\"],
            \"forward_scheme\": \"http\",
            \"forward_host\": \"$SERVER_IP\",
            \"forward_port\": $PORT,
            \"block_exploits\": true,
            \"allow_websocket_upgrade\": true,
            \"ssl_forced\": true,
            \"http2_support\": true,
            \"meta\": {\"letsencrypt_agree\": true},
            \"certificate_id\": \"new\",
            \"advanced_config\": \"\"
        }" 2>&1) && {
        echo "    ✓ Proxy host created for $DOMAIN → localhost:$PORT"
    } || {
        echo "    ⚠ Failed to create proxy host (create manually in NPM)"
        echo "    Response: $RESPONSE"
    }
fi

# ── Save tenant registry entry ───────────────────────────────────────────────
REGISTRY="$SCRIPT_DIR/tenants.csv"
if [[ ! -f "$REGISTRY" ]]; then
    echo "slug,domain,port,created_at,status" > "$REGISTRY"
fi
echo "$SLUG,$DOMAIN,$PORT,$(date -u +%Y-%m-%dT%H:%M:%SZ),active" >> "$REGISTRY"

# ── Done ─────────────────────────────────────────────────────────────────────
echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║  ✓ Tenant '$SLUG' provisioned successfully!             "
echo "╠══════════════════════════════════════════════════════════╣"
echo "║  URL:       http://$DOMAIN (after DNS + NPM setup)      "
echo "║  Direct:    http://localhost:$PORT                       "
echo "║  Login:     root@admin.local / password                  "
echo "║  ⚠ CHANGE THE DEFAULT PASSWORD IMMEDIATELY!             "
echo "╚══════════════════════════════════════════════════════════╝"
