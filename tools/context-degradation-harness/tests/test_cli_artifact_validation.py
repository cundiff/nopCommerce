#!/usr/bin/env python3
"""Regression coverage for CLI artifact parsing and validation."""

from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

HARNESS_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HARNESS_DIR / "lib"))

from parse_json import parse_cli_output
from validate_artifact import validate_artifact


class CliArtifactValidationTests(unittest.TestCase):
    def test_parse_cli_output_preserves_api_error_state(self) -> None:
        parsed = parse_cli_output(
            json.dumps(
                {
                    "type": "result",
                    "is_error": True,
                    "api_error_status": 404,
                    "result": "model unavailable",
                    "session_id": "abc",
                }
            )
        )

        self.assertTrue(parsed["is_error"])
        self.assertEqual(parsed["api_error_status"], 404)

    def test_validate_artifact_rejects_cli_error_payload(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            artifact = Path(tmp) / "artifact.json"
            artifact.write_text(
                json.dumps(
                    {
                        "result": "model unavailable",
                        "session_id": "abc",
                        "raw_json": {
                            "is_error": True,
                            "api_error_status": 404,
                        },
                        "parse_error": None,
                    }
                ),
                encoding="utf-8",
            )

            self.assertNotEqual(validate_artifact(artifact, "baseline"), 0)


if __name__ == "__main__":
    unittest.main()
