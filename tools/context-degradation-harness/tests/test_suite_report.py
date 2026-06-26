#!/usr/bin/env python3
"""Tests for suite report rendering."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

HARNESS_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HARNESS_DIR / "lib"))

from report import generate_report


class SuiteReportTests(unittest.TestCase):
    def test_generate_report_renders_suite_summary(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = Path(tmp)
            cursor_dir = run_dir / "cursor"
            cursor_dir.mkdir()
            (run_dir / "manifest.json").write_text(
                json.dumps({"models": {"cursor": "cursor-model"}, "git_sha": "abc"}),
                encoding="utf-8",
            )
            (cursor_dir / "test_order_status_completion.txt").write_text(
                "Post-warmup answer",
                encoding="utf-8",
            )
            (cursor_dir / "test_order_status_completion.json").write_text(
                json.dumps({"usage": {"inputTokens": 3, "cacheReadTokens": 100}}),
                encoding="utf-8",
            )
            for idx in range(1, 14):
                (cursor_dir / f"warmup_{idx:02d}.json").write_text(
                    json.dumps({"usage": {"inputTokens": idx, "cacheReadTokens": idx * 10}}),
                    encoding="utf-8",
                )
            (run_dir / "scores.json").write_text(
                json.dumps(
                    {
                        "questions": [
                            {
                                "id": "order_status_completion",
                                "prompt": "Explain order status completion.",
                            }
                        ],
                        "tools": {
                            "cursor": {
                                "aggregate": {
                                    "baseline_score": 100,
                                    "test_score": 80,
                                    "max_score": 100,
                                    "degradation_delta": 20,
                                },
                                "questions": {
                                    "order_status_completion": {
                                        "degradation_delta": 20,
                                        "baseline": {
                                            "total_score": 100,
                                            "checks": [{"name": "paid", "passed": True}],
                                        },
                                        "test": {
                                            "total_score": 80,
                                            "checks": [{"name": "paid", "passed": False}],
                                        },
                                    }
                                },
                            }
                        },
                    }
                ),
                encoding="utf-8",
            )

            report = generate_report(run_dir)

            self.assertIn("| cursor | 100 | 80 | 100 | 20 |", report)
            self.assertIn("| order_status_completion | 100 | 80 | 20 | 3 | 100 |", report)
            self.assertIn("Warmup 13", report)
            self.assertIn("Single-run CLI-stack comparison", report)


if __name__ == "__main__":
    unittest.main()
