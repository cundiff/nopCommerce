#!/usr/bin/env python3
"""Parse JSON output from Cursor agent or Claude Code CLI."""

from __future__ import annotations

import json
import re
import sys
from typing import Any


def extract_json_objects(text: str) -> list[dict[str, Any]]:
    """Extract JSON objects from stdout, preferring the last complete object."""
    objects: list[dict[str, Any]] = []

    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            obj = json.loads(line)
            if isinstance(obj, dict):
                objects.append(obj)
        except json.JSONDecodeError:
            continue

    if objects:
        return objects

    # Fallback: try parsing the full text or trailing JSON blob.
    try:
        obj = json.loads(text)
        if isinstance(obj, dict):
            return [obj]
    except json.JSONDecodeError:
        pass

    matches = re.findall(r"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}", text, flags=re.DOTALL)
    for candidate in reversed(matches):
        try:
            obj = json.loads(candidate)
            if isinstance(obj, dict):
                return [obj]
        except json.JSONDecodeError:
            continue

    return []


def pick_result_text(obj: dict[str, Any]) -> str:
    for key in ("result", "content", "text", "message"):
        value = obj.get(key)
        if isinstance(value, str) and value.strip():
            return value
    return ""


def pick_session_id(obj: dict[str, Any]) -> str:
    for key in ("session_id", "sessionId", "chat_id", "chatId"):
        value = obj.get(key)
        if isinstance(value, str) and value.strip():
            return value
    return ""


def pick_usage(obj: dict[str, Any]) -> dict[str, Any]:
    usage = obj.get("usage")
    if isinstance(usage, dict):
        return usage
    return {}


def parse_cli_output(text: str) -> dict[str, Any]:
    objects = extract_json_objects(text)
    if not objects:
        return {
            "result": text.strip(),
            "session_id": "",
            "usage": {},
            "raw_json": None,
            "parse_error": "no JSON object found in CLI output",
        }

    obj = objects[-1]
    return {
        "result": pick_result_text(obj),
        "session_id": pick_session_id(obj),
        "usage": pick_usage(obj),
        "raw_json": obj,
        "parse_error": None,
    }


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: parse_json.py <raw-output-file>", file=sys.stderr)
        return 1

    raw = open(sys.argv[1], encoding="utf-8").read()
    parsed = parse_cli_output(raw)
    json.dump(parsed, sys.stdout, indent=2)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
