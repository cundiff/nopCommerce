#!/usr/bin/env bash
# Per-boot initialization: start PostgreSQL and make sure the store is ready.
# Runs on every environment start; must be idempotent.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

start_postgres

# App_Data/appsettings.json is generated (gitignored) and may be absent on a
# fresh checkout. Recreate it and self-heal the database if it was not seeded.
if ! db_is_seeded; then
    bash "${REPO_ROOT}/.cursor/seed-db.sh"
fi
write_appsettings

echo "PostgreSQL is up and nopCommerce is configured on port ${NOP_PORT}."
