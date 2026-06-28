#!/usr/bin/env bash
# Run order service tests only (validation loop oracle for cloud agents).
#
# Usage:
#   ./scripts/demo/run-orders-tests.sh [extra dotnet test args...]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj \
  --filter "FullyQualifiedName~Nop.Services.Tests.Orders" \
  --configuration Release \
  --verbosity normal \
  "$@"
