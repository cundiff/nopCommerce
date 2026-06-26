#!/usr/bin/env bash
# Shared config loader for harness runners.
# Source this file from runner scripts after setting SCRIPT_DIR.

load_harness_config() {
  local harness_dir="$1"
  local config_file="${harness_dir}/config.yaml"

  if [[ ! -f "$config_file" ]]; then
    echo "ERROR: config not found: $config_file" >&2
    exit 1
  fi

  # shellcheck disable=SC1090
  eval "$(python3 - "$config_file" <<'PY'
import os
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    yaml = None

config_path = Path(sys.argv[1])
text = config_path.read_text()

def parse_simple_yaml(raw: str) -> dict:
    data = {}
    for line in raw.splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        if ":" not in stripped:
            continue
        key, value = stripped.split(":", 1)
        key = key.strip()
        value = value.split("#", 1)[0].strip().strip('"').strip("'")
        if value.isdigit():
            data[key] = int(value)
        else:
            data[key] = value
    return data

if yaml is not None:
    data = yaml.safe_load(text) or {}
else:
    data = parse_simple_yaml(text)

harness_dir = config_path.parent.resolve()
workspace = os.environ.get("WORKSPACE") or data.get("workspace", "..")
nop_root = (harness_dir / workspace).resolve()
shared_model = os.environ.get("MODEL") or data.get("model", "")
cursor_model = (
    os.environ.get("CURSOR_MODEL")
    or data.get("cursor_model")
    or shared_model
    or "claude-4.6-sonnet-medium"
)
claude_model = (
    os.environ.get("CLAUDE_MODEL")
    or data.get("claude_model")
    or shared_model
    or "sonnet"
)

pairs = {
    "HARNESS_DIR": str(harness_dir),
    "NOP_ROOT": str(nop_root),
    "MODEL": shared_model,
    "CURSOR_MODEL": cursor_model,
    "CLAUDE_MODEL": claude_model,
    "CURSOR_BIN": os.environ.get("CURSOR_BIN") or data.get("cursor_bin", "agent"),
    "CLAUDE_BIN": os.environ.get("CLAUDE_BIN") or data.get("claude_bin", "claude"),
    "WARMUP_SLEEP_SECONDS": str(
        os.environ.get("WARMUP_SLEEP_SECONDS") or data.get("warmup_sleep_seconds", 1)
    ),
    "OUTPUT_DIR_NAME": os.environ.get("OUTPUT_DIR_NAME") or data.get("output_dir", "results"),
}

for key, value in pairs.items():
    escaped = str(value).replace("'", "'\\''")
    print(f"{key}='{escaped}'")
PY
)"
}
