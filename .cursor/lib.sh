#!/usr/bin/env bash
# Shared configuration and helpers for the nopCommerce Cloud Agent environment.
# Sourced by install.sh, start.sh, seed-db.sh and run-web.sh.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SLN_DIR="${REPO_ROOT}/src"
WEB_DIR="${SLN_DIR}/Presentation/Nop.Web"
APP_DATA_DIR="${WEB_DIR}/App_Data"
APPSETTINGS_FILE="${APP_DATA_DIR}/appsettings.json"

# Local-only development database credentials. These are not real secrets: the
# database lives inside the throwaway agent VM and is never exposed. Override via
# environment variables if needed. Mirrors the repo's docker-compose.yml.
NOP_DB_NAME="${NOP_DB_NAME:-nopcommerce}"
NOP_DB_USER="${NOP_DB_USER:-nopcommerce}"
NOP_DB_PASSWORD="${NOP_DB_PASSWORD:-nopCommerce_db_password}"
NOP_DB_HOST="${NOP_DB_HOST:-127.0.0.1}"

# Seed admin account created by the nopCommerce installer.
NOP_ADMIN_EMAIL="${NOP_ADMIN_EMAIL:-admin@yourStore.com}"
NOP_ADMIN_PASSWORD="${NOP_ADMIN_PASSWORD:-Admin@123}"

NOP_PORT="${NOP_PORT:-8080}"

# Run a command as root, using sudo only when we are not already root.
as_root() {
    if [ "$(id -u)" -eq 0 ]; then
        "$@"
    else
        sudo "$@"
    fi
}

# Run psql as the postgres superuser.
psql_super() {
    as_root -u postgres psql "$@"
}

# Detect the installed PostgreSQL major version / cluster (e.g. "16" or "15").
pg_version() {
    ls /etc/postgresql 2>/dev/null | sort -n | tail -1
}

# Start the default PostgreSQL cluster if it is not already accepting connections.
start_postgres() {
    local ver
    ver="$(pg_version)"
    if [ -z "${ver}" ]; then
        echo "ERROR: no PostgreSQL cluster found under /etc/postgresql" >&2
        return 1
    fi
    as_root pg_ctlcluster "${ver}" main start 2>/dev/null || true
    for _ in $(seq 1 30); do
        if as_root -u postgres pg_isready -q; then
            return 0
        fi
        sleep 1
    done
    echo "ERROR: PostgreSQL did not become ready" >&2
    return 1
}

# True when the nopCommerce schema has already been created and seeded.
db_is_seeded() {
    local count
    count="$(PGPASSWORD="${NOP_DB_PASSWORD}" psql -h "${NOP_DB_HOST}" -U "${NOP_DB_USER}" \
        -d "${NOP_DB_NAME}" -tAc \
        "SELECT to_regclass('public.\"Product\"') IS NOT NULL;" 2>/dev/null || echo "f")"
    [ "${count}" = "t" ]
}

# Ensure App_Data/appsettings.json points at the provisioned DB so the app boots
# in the installed state. Non-destructive: the installer writes a richer file
# during setup, so only regenerate when it is missing (e.g. a fresh checkout).
write_appsettings() {
    if [ -s "${APPSETTINGS_FILE}" ]; then
        return 0
    fi
    mkdir -p "${APP_DATA_DIR}"
    cat > "${APPSETTINGS_FILE}" <<JSON
{
  "ConnectionStrings": {
    "ConnectionString": "Host=${NOP_DB_HOST};Database=${NOP_DB_NAME};Username=${NOP_DB_USER};Password=${NOP_DB_PASSWORD}",
    "DataProvider": "postgresql"
  }
}
JSON
}
