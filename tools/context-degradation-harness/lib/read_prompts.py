#!/usr/bin/env python3
"""Read warmup prompts and test question from harness prompt files."""

from __future__ import annotations

import json
import sys
from pathlib import Path


def read_warmup_prompts(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return [p.strip() for p in text.split("---") if p.strip()]


def main() -> int:
    harness_dir = Path(__file__).resolve().parent.parent
    warmup_path = harness_dir / "prompts" / "warmup.txt"
    test_path = harness_dir / "prompts" / "test-question.txt"

    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        print(test_path.read_text(encoding="utf-8").strip())
        return 0

    prompts = read_warmup_prompts(warmup_path)

    if len(sys.argv) > 1 and sys.argv[1] == "--null-delimited":
        for prompt in prompts:
            sys.stdout.write(prompt)
            sys.stdout.write("\0")
        return 0

    json.dump(prompts, sys.stdout)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
