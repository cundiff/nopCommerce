#!/usr/bin/env bash
# Long-running nopCommerce web server. Launched as a Cloud Agent terminal so its
# logs stay visible and it can be restarted independently.

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib.sh"

cd "${WEB_DIR}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="http://0.0.0.0:${NOP_PORT}"
exec dotnet run --project "${WEB_DIR}/Nop.Web.csproj" -c Release --no-build
