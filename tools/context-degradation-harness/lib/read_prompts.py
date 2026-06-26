#!/usr/bin/env python3
"""Read warmup prompts and benchmark questions from harness prompt files."""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from typing import Any


def read_warmup_prompts(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    return [p.strip() for p in text.split("---") if p.strip()]


def load_question_manifest(path: Path) -> list[dict[str, Any]]:
    """Load benchmark questions and resolve their ground-truth paths."""
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, list):
        raise ValueError(f"question manifest must be a JSON array: {path}")

    questions: list[dict[str, Any]] = []
    for index, item in enumerate(raw, start=1):
        if not isinstance(item, dict):
            raise ValueError(f"question #{index} must be an object")

        question_id = str(item.get("id") or "").strip()
        prompt = str(item.get("prompt") or "").strip()
        ground_truth = str(item.get("ground_truth") or "").strip()
        if not question_id or not prompt or not ground_truth:
            raise ValueError(f"question #{index} must include id, prompt, and ground_truth")

        ground_truth_path = (path.parent / ground_truth).resolve()
        questions.append(
            {
                **item,
                "id": question_id,
                "prompt": prompt,
                "ground_truth_path": str(ground_truth_path),
            }
        )

    return questions


def main() -> int:
    harness_dir = Path(__file__).resolve().parent.parent
    warmup_path = harness_dir / "prompts" / "warmup.txt"
    test_path = harness_dir / "prompts" / "test-question.txt"
    questions_config = os.environ.get("QUESTION_MANIFEST") or "prompts/questions.json"
    questions_path = Path(questions_config)
    if not questions_path.is_absolute():
        questions_path = harness_dir / questions_path

    if len(sys.argv) > 1 and sys.argv[1] == "--test":
        if questions_path.exists():
            print(load_question_manifest(questions_path)[0]["prompt"])
        else:
            print(test_path.read_text(encoding="utf-8").strip())
        return 0

    if len(sys.argv) > 1 and sys.argv[1] == "--questions-json":
        json.dump(load_question_manifest(questions_path), sys.stdout)
        return 0

    if len(sys.argv) > 1 and sys.argv[1] == "--questions-tsv":
        for question in load_question_manifest(questions_path):
            sys.stdout.write(f"{question['id']}\t{question['prompt']}\n")
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
