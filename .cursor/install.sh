#!/usr/bin/env bash
# One-time repository bootstrap for the Cloud Agent environment.
# Builds the solution and provisions + seeds the PostgreSQL database.
# Runs after the source tree is available; safe to re-run.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

echo "Building NopCommerce.sln (Release)..."
dotnet build "${SLN_DIR}/NopCommerce.sln" -c Release

bash "${REPO_ROOT}/.cursor/seed-db.sh"

# Ensure the running app will connect to the freshly seeded database.
write_appsettings

echo "Environment install complete."
