# PTScheduler SaaS — Deployment Guide

## Architecture

Each trainer gets a fully isolated instance:
- **1 x .NET app container** (ptscheduler-web)
- **1 x PostgreSQL container** (postgres:17-alpine)
- **Nginx Proxy Manager** routes subdomains to each tenant

```
                    ┌─────────────────────────┐
                    │   Nginx Proxy Manager    │
                    │       :80 / :443         │
                    └──┬──────┬──────┬────────┘
          jan.pt.pl    │  ola.pt.pl  │  max.pt.pl
                       ▼      ▼      ▼
                    ┌─────┐┌─────┐┌─────┐
                    │:9001││:9002││:9003│  ← .NET
                    └──┬──┘└──┬──┘└──┬──┘
                    ┌──┴──┐┌──┴──┐┌──┴──┐
                    │ DB  ││ DB  ││ DB  │  ← PostgreSQL
                    └─────┘└─────┘└─────┘
```

## Quick Start

### 1. Start infrastructure (Nginx Proxy Manager)

```bash
cd deploy
docker compose -f docker-compose.infra.yml up -d
```

NPM dashboard: `http://YOUR_SERVER_IP:81`  
Default login: `admin@example.com` / `changeme`

### 2. Build the app image

```bash
# From repo root
docker build -t ptscheduler-web:latest .
```

### 3. Provision a new tenant

```bash
./provision.sh jan-kowalski jan.ptscheduler.pl
```

With auto NPM registration:
```bash
./provision.sh jan-kowalski jan.ptscheduler.pl http://localhost:81
```

### 4. DNS Setup

Point `*.ptscheduler.pl` (wildcard A record) to your server IP,
or add individual A records per tenant.

## Scripts

| Script | Description |
|--------|-------------|
| `provision.sh <slug> <domain> [npm-url]` | Create new tenant |
| `upgrade.sh [--tenant slug]` | Rebuild image & restart all/one tenant |
| `backup.sh [dir]` | pg_dump all tenant databases |
| `tenant-ctl.sh list` | List all tenants with status |
| `tenant-ctl.sh start/stop/restart <slug>` | Control tenant |
| `tenant-ctl.sh logs <slug>` | View tenant logs |
| `tenant-ctl.sh shell <slug>` | Shell into web container |
| `tenant-ctl.sh dbshell <slug>` | psql into tenant DB |
| `tenant-ctl.sh destroy <slug>` | Remove tenant (with confirmation) |

## Setup Wizard

When a fresh instance starts, the Welcome page redirects to `/setup`.

The setup wizard offers two modes:
- **Self-setup (free)** — trainer configures name, email, password
- **Admin setup (paid)** — displays contact info, then basic config

After setup, the default `root@admin.local` account is replaced with
the trainer's email and password.

## Resource Usage (per tenant)

| Component | RAM |
|-----------|-----|
| .NET app (idle) | ~80–120 MB |
| PostgreSQL | ~30–50 MB |
| **Total** | **~150 MB** |

On a 2C/4GB server: ~15–20 tenants comfortably.

## Backups

Daily cron (recommended):
```cron
0 3 * * * /path/to/deploy/backup.sh >> /var/log/ptscheduler-backup.log 2>&1
```

Backups are kept for 30 days, then auto-cleaned.

## Upgrading

After pulling new code:
```bash
./upgrade.sh                    # rebuild + restart all
./upgrade.sh --tenant jan       # restart only one
./upgrade.sh --build-only       # just rebuild the image
```

EF migrations run automatically on each app startup.
