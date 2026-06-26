#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HARNESS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# shellcheck source=../lib/config.sh
source "${HARNESS_DIR}/lib/config.sh"
# shellcheck source=../lib/load_warmup_prompts.sh
source "${HARNESS_DIR}/lib/load_warmup_prompts.sh"
load_harness_config "$HARNESS_DIR"

CLAUDE_BIN="${CLAUDE_BIN:-claude}"
MODEL="${CLAUDE_MODEL:-${MODEL:-sonnet}}"
WARMUP_SLEEP_SECONDS="${WARMUP_SLEEP_SECONDS:-1}"
WARMUP_PROMPT_COUNT="${WARMUP_PROMPT_COUNT:-12}"
PARSE_JSON="${HARNESS_DIR}/lib/parse_json.py"
VALIDATE_ARTIFACT="${HARNESS_DIR}/lib/validate_artifact.py"

if [[ -z "${RUN_DIR:-}" ]]; then
  TIMESTAMP="$(date -u +"%Y%m%dT%H%M%SZ")"
  RUN_DIR="${HARNESS_DIR}/${OUTPUT_DIR_NAME}/${TIMESTAMP}/claude"
else
  RUN_DIR="${RUN_DIR}/claude"
fi
mkdir -p "$RUN_DIR"

READ_PROMPTS="${HARNESS_DIR}/lib/read_prompts.py"
QUESTION_TSV="${RUN_DIR}/questions.tsv"
python3 "$READ_PROMPTS" --questions-tsv > "$QUESTION_TSV"

cd "$NOP_ROOT"

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

extract_session_id() {
  local parsed_file="$1"
  python3 - "$parsed_file" <<'PY'
import json
import sys
from pathlib import Path
data = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
print(data.get("session_id", ""))
PY
}

run_claude_prompt() {
  local label="$1"
  local prompt="$2"
  local session_id="${3:-}"
  local raw_file="${RUN_DIR}/${label}.raw"
  local parsed_file="${RUN_DIR}/${label}.json"

  local -a args=(
    -p
    --model "$MODEL"
    --output-format json
  )
  if [[ -n "$session_id" ]]; then
    args+=(--resume "$session_id")
  fi

  echo "[$label] Running Claude Code..."
  set +e
  "$CLAUDE_BIN" "${args[@]}" "$prompt" > "$raw_file" 2>&1
  local cli_status=$?
  set -e
  if [[ "$cli_status" -ne 0 ]]; then
    echo "ERROR: Claude CLI exited non-zero for ${label}; preserving raw output." >&2
  fi
  save_artifact "$label" "$raw_file" "$parsed_file"
  python3 "$VALIDATE_ARTIFACT" "$parsed_file" "$label"
  if [[ "$cli_status" -ne 0 ]]; then
    return "$cli_status"
  fi
}

echo "Claude runner"
echo "  NOP_ROOT=$NOP_ROOT"
echo "  MODEL=$MODEL"
echo "  RUN_DIR=$RUN_DIR"

# Baseline: fresh sessions, all benchmark questions
while IFS=$'\t' read -r question_id prompt; do
  run_claude_prompt "baseline_${question_id}" "$prompt"
done < "$QUESTION_TSV"

load_warmup_prompts "$READ_PROMPTS" "$WARMUP_PROMPT_COUNT"

# Warmup 1: fresh session, capture session_id
run_claude_prompt "warmup_01" "${WARMUP_PROMPTS[0]}"
SESSION_ID="$(extract_session_id "${RUN_DIR}/warmup_01.json")"
if [[ -z "$SESSION_ID" ]]; then
  echo "ERROR: could not extract session_id from warmup_01.json" >&2
  exit 1
fi
echo "Captured session: $SESSION_ID"

# Warmup 2-12 + test: resume same session
idx=2
while [[ "$idx" -le "$WARMUP_PROMPT_COUNT" ]]; do
  run_claude_prompt "$(printf 'warmup_%02d' "$idx")" "${WARMUP_PROMPTS[$((idx - 1))]}" "$SESSION_ID"
  if [[ "$idx" -lt "$WARMUP_PROMPT_COUNT" ]]; then
    sleep "$WARMUP_SLEEP_SECONDS"
  fi
  idx=$((idx + 1))
done

# Post-warmup tests in same session
while IFS=$'\t' read -r question_id prompt; do
  run_claude_prompt "test_${question_id}" "$prompt" "$SESSION_ID"
done < "$QUESTION_TSV"

echo "Claude run complete: $RUN_DIR"
