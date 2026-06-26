#!/usr/bin/env python3
"""Generate markdown report for a context degradation harness run."""

from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path
from typing import Any


HARNESS_DIR = Path(__file__).resolve().parent.parent
EXCERPT_LIMIT = 1200


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    with path.open(encoding="utf-8") as f:
        data = json.load(f)
    return data if isinstance(data, dict) else {}


def read_answer_text(tool_dir: Path, label: str) -> str:
    txt_path = tool_dir / f"{label}.txt"
    if txt_path.exists():
        return txt_path.read_text(encoding="utf-8")
    json_path = tool_dir / f"{label}.json"
    if json_path.exists():
        data = load_json(json_path)
        return data.get("result") or data.get("answer") or ""
    return ""


def read_usage(tool_dir: Path, label: str) -> dict[str, Any]:
    json_path = tool_dir / f"{label}.json"
    data = load_json(json_path)
    usage = data.get("usage")
    return usage if isinstance(usage, dict) else {}


def format_usage(usage: dict[str, Any]) -> str:
    if not usage:
        return "n/a"
    parts = []
    for key in ("inputTokens", "outputTokens", "cacheReadTokens", "cacheWriteTokens"):
        if key in usage:
            parts.append(f"{key}={usage[key]}")
    if not parts:
        return ", ".join(f"{k}={v}" for k, v in usage.items())
    return ", ".join(parts)


def truncate(text: str, limit: int = EXCERPT_LIMIT) -> str:
    text = text.strip()
    if len(text) <= limit:
        return text
    return text[:limit] + "\n\n... (truncated; see full .txt artifact)"


def warmup_token_series(tool_dir: Path) -> list[tuple[int, dict[str, Any]]]:
    series: list[tuple[int, dict[str, Any]]] = []
    for i in range(1, 13):
        label = f"warmup_{i:02d}"
        usage = read_usage(tool_dir, label)
        if usage:
            series.append((i, usage))
    return series


def rubric_table(tool_scores: dict[str, Any]) -> str:
    baseline_checks = {
        c["name"]: c for c in tool_scores.get("baseline", {}).get("checks", [])
    }
    test_checks = {c["name"]: c for c in tool_scores.get("test", {}).get("checks", [])}
    names = sorted(set(baseline_checks) | set(test_checks))

    lines = [
        "| Check | Baseline | Post-warmup |",
        "|-------|----------|-------------|",
    ]
    for name in names:
        b = baseline_checks.get(name, {})
        t = test_checks.get(name, {})
        b_status = "PASS" if b.get("passed") else "FAIL"
        t_status = "PASS" if t.get("passed") else "FAIL"
        lines.append(f"| {name} | {b_status} | {t_status} |")
    return "\n".join(lines)


def generate_report(run_dir: Path) -> str:
    manifest = load_json(run_dir / "manifest.json")
    scores = load_json(run_dir / "scores.json")

    lines: list[str] = [
        "# Context Fill Degradation Report",
        "",
        f"Generated: {datetime.now().isoformat(timespec='seconds')}",
        f"Run directory: `{run_dir}`",
        "",
    ]

    if manifest:
        models = manifest.get("models")
        if isinstance(models, dict):
            model_line = ", ".join(f"{tool}=`{model}`" for tool, model in models.items())
        else:
            model_line = f"`{manifest.get('model', 'unknown')}`"
        lines.extend(
            [
                "## Run Metadata",
                "",
                f"- Models: {model_line}",
                f"- Git SHA: `{manifest.get('git_sha', 'unknown')}`",
                f"- Workspace: `{manifest.get('workspace', 'unknown')}`",
            ]
        )
        versions = manifest.get("versions", {})
        if versions:
            lines.append("- Tool versions:")
            for tool, version in versions.items():
                lines.append(f"  - {tool}: `{version}`")
        lines.append("")

    lines.extend(
        [
            "## Summary",
            "",
            "| Tool | Baseline Score | Post-warmup Score | Delta | Test Input Tokens |",
            "|------|----------------|-------------------|-------|-------------------|",
        ]
    )

    tool_entries: list[tuple[str, dict[str, Any]]] = []
    for tool in ("cursor", "claude"):
        tool_scores = scores.get("tools", {}).get(tool, {})
        if not tool_scores:
            continue
        tool_entries.append((tool, tool_scores))
        baseline = tool_scores.get("baseline", {}).get("total_score", "n/a")
        test = tool_scores.get("test", {}).get("total_score", "n/a")
        delta = tool_scores.get("degradation_delta", "n/a")
        usage = read_usage(run_dir / tool, "test")
        input_tokens = usage.get("inputTokens", usage.get("input_tokens", "n/a"))
        lines.append(f"| {tool} | {baseline} | {test} | {delta} | {input_tokens} |")

    lines.append("")

    for tool, tool_scores in tool_entries:
        tool_dir = run_dir / tool
        lines.extend([f"## {tool.title()} Details", ""])

        series = warmup_token_series(tool_dir)
        if series:
            lines.extend(["### Context Growth (input tokens)", ""])
            for step, usage in series:
                input_tokens = usage.get("inputTokens", usage.get("input_tokens", "n/a"))
                lines.append(f"- Warmup {step:02d}: {input_tokens}")
            lines.append("")

        lines.extend(["### Rubric Breakdown", "", rubric_table(tool_scores), ""])

        baseline_text = read_answer_text(tool_dir, "baseline")
        test_text = read_answer_text(tool_dir, "test")
        lines.extend(
            [
                "### Side-by-Side Excerpts",
                "",
                "#### Baseline",
                "",
                "```",
                truncate(baseline_text),
                "```",
                "",
                f"Full answer: `{tool_dir / 'baseline.txt'}`",
                "",
                "#### Post-warmup",
                "",
                "```",
                truncate(test_text),
                "```",
                "",
                f"Full answer: `{tool_dir / 'test.txt'}`",
                "",
            ]
        )

    if len(tool_entries) == 2:
        cursor_delta = tool_entries[0][1].get("degradation_delta")
        claude_delta = tool_entries[1][1].get("degradation_delta")
        lines.extend(
            [
                "## Cross-Tool Comparison",
                "",
            ]
        )
        if cursor_delta is not None and claude_delta is not None:
            if cursor_delta > claude_delta:
                lines.append(
                    f"Cursor degraded more on this question (delta {cursor_delta} vs {claude_delta})."
                )
            elif claude_delta > cursor_delta:
                lines.append(
                    f"Claude degraded more on this question (delta {claude_delta} vs {cursor_delta})."
                )
            else:
                lines.append(
                    f"Both tools showed the same degradation delta ({cursor_delta})."
                )
        else:
            lines.append("Insufficient scored results for cross-tool comparison.")
        lines.append("")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate harness markdown report")
    parser.add_argument("run_dir", type=Path, help="Timestamped results directory")
    parser.add_argument(
        "--output",
        type=Path,
        help="Report output path (default: <run_dir>/report.md)",
    )
    args = parser.parse_args()

    run_dir = args.run_dir.resolve()
    if not run_dir.is_dir():
        print(f"ERROR: run directory not found: {run_dir}", file=__import__("sys").stderr)
        return 1

    report = generate_report(run_dir)
    output = args.output or (run_dir / "report.md")
    output.write_text(report, encoding="utf-8")
    print(f"Wrote report: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
