#!/usr/bin/env bash
# Idempotent Cursor Cloud install for nopCommerce (.NET 10 + PostgreSQL + frontend assets).
set -euo pipefail

export DEBIAN_FRONTEND=noninteractive

echo "==> Ensuring Microsoft package repo and .NET SDK 10"
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb \
    || wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
  sudo dpkg -i /tmp/packages-microsoft-prod.deb
  sudo apt-get update -y
  sudo apt-get install -y dotnet-sdk-10.0
fi
dotnet --info | head -n 40

echo "==> Ensuring PostgreSQL"
if ! command -v psql >/dev/null 2>&1; then
  sudo apt-get update -y
  sudo apt-get install -y postgresql postgresql-contrib
fi
sudo service postgresql start || sudo pg_ctlcluster 16 main start || sudo pg_ctlcluster 14 main start || true

DB_USER="${NOP_DB_USER:-nopcommerce}"
DB_PASS="${NOP_DB_PASSWORD:-nopCommerce_db_password}"
DB_NAME="${NOP_DB_NAME:-nopcommerce}"

echo "==> Ensuring PostgreSQL role/database (${DB_NAME})"
sudo -u postgres psql -v ON_ERROR_STOP=1 <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${DB_USER}') THEN
    CREATE ROLE ${DB_USER} LOGIN PASSWORD '${DB_PASS}';
  END IF;
END
\$\$;
SELECT 'CREATE DATABASE ${DB_NAME} OWNER ${DB_USER}'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${DB_NAME}')\gexec
ALTER DATABASE ${DB_NAME} OWNER TO ${DB_USER};
\c ${DB_NAME}
ALTER SCHEMA public OWNER TO ${DB_USER};
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
SQL

echo "==> Restoring NuGet packages"
dotnet restore src/NopCommerce.sln

echo "==> Installing npm deps and building frontend assets"
if command -v npm >/dev/null 2>&1; then
  (
    cd src/Presentation/Nop.Web
    if [[ -f package-lock.json ]]; then
      npm ci --no-audit --no-fund || npm install --no-audit --no-fund
    else
      npm install --no-audit --no-fund
    fi
    npx gulp default
  )
else
  echo "npm not found; skipping frontend asset build"
fi

echo "==> Install complete"
