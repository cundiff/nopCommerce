#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HARNESS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# shellcheck source=../lib/config.sh
source "${HARNESS_DIR}/lib/config.sh"
# shellcheck source=../lib/load_warmup_prompts.sh
source "${HARNESS_DIR}/lib/load_warmup_prompts.sh"
load_harness_config "$HARNESS_DIR"

CURSOR_BIN="${CURSOR_BIN:-agent}"
MODEL="${CURSOR_MODEL:-${MODEL:-claude-4.6-sonnet-medium}}"
WARMUP_SLEEP_SECONDS="${WARMUP_SLEEP_SECONDS:-1}"
PARSE_JSON="${HARNESS_DIR}/lib/parse_json.py"
VALIDATE_ARTIFACT="${HARNESS_DIR}/lib/validate_artifact.py"

if [[ -z "${RUN_DIR:-}" ]]; then
  TIMESTAMP="$(date -u +"%Y%m%dT%H%M%SZ")"
  RUN_DIR="${HARNESS_DIR}/${OUTPUT_DIR_NAME}/${TIMESTAMP}/cursor"
else
  RUN_DIR="${RUN_DIR}/cursor"
fi
mkdir -p "$RUN_DIR"

READ_PROMPTS="${HARNESS_DIR}/lib/read_prompts.py"
TEST_QUESTION="$(python3 "$READ_PROMPTS" --test)"

save_artifact() {
  local label="$1"
  local raw_file="$2"
  local parsed_file="$3"

  python3 "$PARSE_JSON" "$raw_file" > "$parsed_file"
  python3 - "$parsed_file" "$RUN_DIR/${label}.txt" <<'PY'
import json
import sys
from pathlib import Path
data = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
result = data.get("result", "")
Path(sys.argv[2]).write_text(result, encoding="utf-8")
PY
}

run_agent_prompt() {
  local label="$1"
  local prompt="$2"
  local chat_id="${3:-}"
  local raw_file="${RUN_DIR}/${label}.raw"
  local parsed_file="${RUN_DIR}/${label}.json"

  local -a args=(
    -p
    --mode ask
    --trust
    --workspace "$NOP_ROOT"
    --model "$MODEL"
    --output-format json
  )
  if [[ -n "$chat_id" ]]; then
    args+=(--resume "$chat_id")
  fi

  echo "[$label] Running Cursor agent..."
  set +e
  "$CURSOR_BIN" "${args[@]}" "$prompt" > "$raw_file" 2>&1
  local cli_status=$?
  set -e
  if [[ "$cli_status" -ne 0 ]]; then
    echo "ERROR: Cursor agent exited non-zero for ${label}; preserving raw output." >&2
  fi
  save_artifact "$label" "$raw_file" "$parsed_file"
  python3 "$VALIDATE_ARTIFACT" "$parsed_file" "$label"
  if [[ "$cli_status" -ne 0 ]]; then
    return "$cli_status"
  fi
}

echo "Cursor runner"
echo "  NOP_ROOT=$NOP_ROOT"
echo "  MODEL=$MODEL"
echo "  RUN_DIR=$RUN_DIR"

cd "$NOP_ROOT"

# Baseline: fresh session, test question only
run_agent_prompt "baseline" "$TEST_QUESTION"

# Warmup: create chat, run 12 prompts with resume
CHAT_ID="$("$CURSOR_BIN" create-chat)"
echo "Created chat: $CHAT_ID"

load_warmup_prompts "$READ_PROMPTS" 12

idx=1
for prompt in "${WARMUP_PROMPTS[@]}"; do
  run_agent_prompt "$(printf 'warmup_%02d' "$idx")" "$prompt" "$CHAT_ID"
  if [[ "$idx" -lt 12 ]]; then
    sleep "$WARMUP_SLEEP_SECONDS"
  fi
  idx=$((idx + 1))
done

# Post-warmup test in same chat
run_agent_prompt "test" "$TEST_QUESTION" "$CHAT_ID"

echo "Cursor run complete: $RUN_DIR"
