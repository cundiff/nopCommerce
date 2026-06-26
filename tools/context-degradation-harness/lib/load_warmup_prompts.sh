#!/usr/bin/env bash
# Load warmup prompts into WARMUP_PROMPTS[] (bash 3.2+; avoids mapfile which needs bash 4).

load_warmup_prompts() {
  local read_prompts="$1"
  local expected_count="${2:-12}"
  local prompt

  WARMUP_PROMPTS=()
  while IFS= read -r -d '' prompt || [[ -n "$prompt" ]]; do
    WARMUP_PROMPTS+=("$prompt")
  done < <(python3 "$read_prompts" --null-delimited)

  if [[ "${#WARMUP_PROMPTS[@]}" -ne "$expected_count" ]]; then
    echo "ERROR: expected ${expected_count} warmup prompts, found ${#WARMUP_PROMPTS[@]}" >&2
    return 1
  fi
}
