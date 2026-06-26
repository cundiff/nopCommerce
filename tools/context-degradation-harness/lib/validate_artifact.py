#!/usr/bin/env python3
"""Validate parsed CLI artifacts before they are scored."""

from __future__ import annotations

import json
import sys
from pathlib import Path


def validate_artifact(path: Path, label: str) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))

    parse_error = data.get("parse_error")
    if parse_error:
        print(f"ERROR: {label} output could not be parsed: {parse_error}", file=sys.stderr)
        return 1

    raw_json = data.get("raw_json")
    is_error = bool(data.get("is_error"))
    api_error_status = data.get("api_error_status")
    if isinstance(raw_json, dict):
        is_error = is_error or bool(raw_json.get("is_error"))
        api_error_status = api_error_status or raw_json.get("api_error_status")

    if is_error:
        status = f" status {api_error_status}" if api_error_status else ""
        result = str(data.get("result") or "").strip()
        detail = f": {result}" if result else ""
        print(f"ERROR: {label} returned CLI error{status}{detail}", file=sys.stderr)
        return 1

    if not str(data.get("result") or "").strip():
        print(f"ERROR: {label} returned an empty result", file=sys.stderr)
        return 1

    return 0


def main() -> int:
    if len(sys.argv) != 3:
        print("Usage: validate_artifact.py <parsed-json-file> <label>", file=sys.stderr)
        return 2

    return validate_artifact(Path(sys.argv[1]), sys.argv[2])


if __name__ == "__main__":
    raise SystemExit(main())
