#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HARNESS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# shellcheck source=../lib/config.sh
source "${HARNESS_DIR}/lib/config.sh"
load_harness_config "$HARNESS_DIR"

CURSOR_BIN="${CURSOR_BIN:-agent}"
CLAUDE_BIN="${CLAUDE_BIN:-claude}"
MODEL="${MODEL:-claude-4.6-sonnet-medium}"

if [[ -z "${RUN_DIR:-}" ]]; then
  TIMESTAMP="$(date -u +"%Y%m%dT%H%M%SZ")"
  RUN_DIR="${HARNESS_DIR}/${OUTPUT_DIR_NAME}/${TIMESTAMP}"
else
  RUN_DIR="$(cd "$RUN_DIR" && pwd)"
  TIMESTAMP="$(basename "$RUN_DIR")"
fi
mkdir -p "$RUN_DIR"

echo "Validating prerequisites..."

if ! command -v "$CURSOR_BIN" >/dev/null 2>&1; then
  echo "ERROR: Cursor CLI not found: $CURSOR_BIN" >&2
  exit 1
fi

if ! command -v "$CLAUDE_BIN" >/dev/null 2>&1; then
  echo "ERROR: Claude CLI not found: $CLAUDE_BIN" >&2
  echo "Set CLAUDE_BIN to the real Anthropic CLI binary if a shell wrapper (e.g. comproxy) is unavailable." >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "ERROR: python3 is required" >&2
  exit 1
fi

if [[ ! -d "$NOP_ROOT/.git" ]]; then
  echo "WARNING: nopCommerce git root not detected at $NOP_ROOT" >&2
fi

get_version() {
  local bin_name="$1"
  shift
  if ! command -v "$bin_name" >/dev/null 2>&1; then
    echo "unavailable"
    return
  fi
  "$bin_name" "$@" 2>/dev/null | head -n 1 | tr -d '\r' || echo "unknown"
}

GIT_SHA="$(git -C "$NOP_ROOT" rev-parse HEAD 2>/dev/null || echo "unknown")"
CURSOR_VERSION="$(get_version "$CURSOR_BIN" --version)"
CLAUDE_VERSION="$(get_version "$CLAUDE_BIN" -v)"
if [[ "$CLAUDE_VERSION" == "unknown" || "$CLAUDE_VERSION" == "unavailable" ]]; then
  CLAUDE_VERSION="$(get_version "$CLAUDE_BIN" --version)"
fi

python3 - "$RUN_DIR/manifest.json" <<PY
import json
from pathlib import Path

manifest = {
    "created_at": "${TIMESTAMP:-manual}",
    "model": "${MODEL}",
    "workspace": "${NOP_ROOT}",
    "git_sha": "${GIT_SHA}",
    "harness_dir": "${HARNESS_DIR}",
    "versions": {
        "cursor": "${CURSOR_VERSION}",
        "claude": "${CLAUDE_VERSION}",
        "python": __import__("platform").python_version(),
    },
    "config": {
        "cursor_bin": "${CURSOR_BIN}",
        "claude_bin": "${CLAUDE_BIN}",
        "warmup_sleep_seconds": int("${WARMUP_SLEEP_SECONDS}"),
    },
}
Path("${RUN_DIR}/manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
PY

echo "Manifest written: ${RUN_DIR}/manifest.json"

export RUN_DIR
echo "Running Cursor harness..."
"${SCRIPT_DIR}/run_cursor.sh"

echo "Running Claude harness..."
"${SCRIPT_DIR}/run_claude.sh"

echo "Scoring answers..."
python3 "${HARNESS_DIR}/lib/score.py" --run-dir "$RUN_DIR" --json > "${RUN_DIR}/scores.json"

echo "Generating report..."
python3 "${HARNESS_DIR}/lib/report.py" "$RUN_DIR"

echo "Harness complete: ${RUN_DIR}"
echo "Report: ${RUN_DIR}/report.md"
