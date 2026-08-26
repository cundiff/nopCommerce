#!/usr/bin/env bash
# Idempotently provision and seed the nopCommerce PostgreSQL database.
#   1. ensure the role, database and required extensions exist
#   2. if the schema is missing, drive the nopCommerce web installer once to
#      create the schema and load the sample catalog + admin account
#
# Safe to run repeatedly: it is a no-op once the store is installed.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

start_postgres

# Role + database (owned by the app role).
if ! psql_super -tAc "SELECT 1 FROM pg_roles WHERE rolname='${NOP_DB_USER}'" | grep -q 1; then
    psql_super -c "CREATE ROLE \"${NOP_DB_USER}\" WITH LOGIN PASSWORD '${NOP_DB_PASSWORD}' CREATEDB;"
fi
if ! psql_super -tAc "SELECT 1 FROM pg_database WHERE datname='${NOP_DB_NAME}'" | grep -q 1; then
    psql_super -c "CREATE DATABASE \"${NOP_DB_NAME}\" OWNER \"${NOP_DB_USER}\";"
fi

# nopCommerce's PostgreSQL provider requires the citext and pgcrypto types.
# These are created automatically only when nopCommerce creates the database
# itself, so we add them here for the pre-created database. They must exist
# before the app first connects, otherwise Npgsql caches the type catalog
# without citext and installation fails with DataTypeName '-.-'.
psql_super -d "${NOP_DB_NAME}" -c \
    "CREATE EXTENSION IF NOT EXISTS citext; CREATE EXTENSION IF NOT EXISTS pgcrypto;"

if db_is_seeded; then
    echo "nopCommerce database already installed; skipping seed."
    exit 0
fi

echo "Installing nopCommerce (schema + sample data) via the web installer..."

# Start with a clean install state: the app must boot in installer mode.
rm -f "${APPSETTINGS_FILE}"

ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://127.0.0.1:${NOP_PORT}" \
    dotnet run --project "${WEB_DIR}/Nop.Web.csproj" -c Release --no-build \
    > /tmp/nop-seed-web.log 2>&1 &
APP_PID=$!
trap 'kill "${APP_PID}" 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
    if curl -sf -o /dev/null "http://127.0.0.1:${NOP_PORT}/install"; then
        break
    fi
    sleep 2
done

COOKIES="$(mktemp)"
PAGE="$(mktemp)"
curl -sS --max-time 30 -c "${COOKIES}" "http://127.0.0.1:${NOP_PORT}/install" -o "${PAGE}"
TOKEN="$(grep -o '__RequestVerificationToken[^>]*value="[^"]*"' "${PAGE}" \
    | head -1 | sed 's/.*value="//;s/"$//')"

# DataProvider=3 -> PostgreSQL (see Nop.Data.DataProviderType).
curl -sS --max-time 600 -b "${COOKIES}" -c "${COOKIES}" -o /dev/null \
    -w "installer HTTP %{http_code}\n" \
    -X POST "http://127.0.0.1:${NOP_PORT}/install" \
    --data-urlencode "__RequestVerificationToken=${TOKEN}" \
    --data-urlencode "AdminEmail=${NOP_ADMIN_EMAIL}" \
    --data-urlencode "AdminPassword=${NOP_ADMIN_PASSWORD}" \
    --data-urlencode "ConfirmPassword=${NOP_ADMIN_PASSWORD}" \
    --data-urlencode "DataProvider=3" \
    --data-urlencode "ConnectionStringRaw=false" \
    --data-urlencode "ServerName=${NOP_DB_HOST}" \
    --data-urlencode "DatabaseName=${NOP_DB_NAME}" \
    --data-urlencode "IntegratedSecurity=false" \
    --data-urlencode "Username=${NOP_DB_USER}" \
    --data-urlencode "Password=${NOP_DB_PASSWORD}" \
    --data-urlencode "CreateDatabaseIfNotExists=false" \
    --data-urlencode "UseCustomCollation=false" \
    --data-urlencode "Collation=" \
    --data-urlencode "InstallSampleData=true" \
    --data-urlencode "DisableSampleDataOption=false" \
    --data-urlencode "SubscribeNewsletters=false" \
    --data-urlencode "Country="

kill "${APP_PID}" 2>/dev/null || true
wait "${APP_PID}" 2>/dev/null || true
trap - EXIT
rm -f "${COOKIES}" "${PAGE}"

if db_is_seeded; then
    echo "nopCommerce installed successfully."
else
    echo "ERROR: nopCommerce installation did not complete. See /tmp/nop-seed-web.log" >&2
    exit 1
fi
