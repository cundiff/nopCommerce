#!/usr/bin/env python3
"""Score answers against context-degradation benchmark ground truth."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any


HARNESS_DIR = Path(__file__).resolve().parent.parent
GROUND_TRUTH_PATH = HARNESS_DIR / "ground-truth" / "get-final-price-async.json"
QUESTIONS_PATH = HARNESS_DIR / "prompts" / "questions.json"

CHECKS = [
    ("overload_count", 25),
    ("return_type", 20),
    ("overload_1_signature", 15),
    ("overload_2_signature", 15),
    ("overload_3_signature", 15),
    ("source_grounding", 10),
]


@dataclass
class CheckResult:
    name: str
    weight: int
    passed: bool
    detail: str


@dataclass
class ScoreResult:
    answer_path: str
    total_score: int
    max_score: int = 100
    checks: list[CheckResult] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        return {
            "answer_path": self.answer_path,
            "total_score": self.total_score,
            "max_score": self.max_score,
            "checks": [
                {
                    "name": c.name,
                    "weight": c.weight,
                    "passed": c.passed,
                    "detail": c.detail,
                    "points": c.weight if c.passed else 0,
                }
                for c in self.checks
            ],
        }


def resolve_question_manifest(path: Path | None = None, run_dir: Path | None = None) -> Path:
    if path is not None:
        questions_path = path
    elif run_dir is not None:
        manifest = load_json_file(run_dir / "manifest.json")
        configured = manifest.get("config", {}).get("question_manifest")
        questions_path = Path(configured) if configured else QUESTIONS_PATH
    else:
        configured = os.environ.get("QUESTION_MANIFEST")
        questions_path = Path(configured) if configured else QUESTIONS_PATH

    if not questions_path.is_absolute():
        questions_path = HARNESS_DIR / questions_path
    return questions_path


def load_json_file(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    with path.open(encoding="utf-8") as f:
        data = json.load(f)
    return data if isinstance(data, dict) else {}


def load_questions(path: Path | None = None, run_dir: Path | None = None) -> list[dict[str, Any]]:
    questions_path = resolve_question_manifest(path, run_dir)
    if not questions_path.exists():
        return [
            {
                "id": "get_final_price_overloads",
                "prompt": (HARNESS_DIR / "prompts" / "test-question.txt").read_text(encoding="utf-8").strip(),
                "ground_truth_path": str(GROUND_TRUTH_PATH),
            }
        ]

    sys.path.insert(0, str(HARNESS_DIR / "lib"))
    from read_prompts import load_question_manifest

    return load_question_manifest(questions_path)


def load_ground_truth(path: Path | None = None) -> dict[str, Any]:
    gt_path = path or GROUND_TRUTH_PATH
    with gt_path.open(encoding="utf-8") as f:
        return json.load(f)


def normalize_text(text: str) -> str:
    text = re.sub(r"```[\w]*", "", text)
    text = text.replace("```", "")
    text = text.replace("\r\n", "\n")
    return text


def normalize_for_terms(text: str) -> str:
    return re.sub(r"\s+", " ", normalize_text(text).lower()).strip()


def term_found(normalized_text: str, term: str | list[str]) -> bool:
    if isinstance(term, list):
        return any(term_found(normalized_text, option) for option in term)
    return normalize_for_terms(str(term)) in normalized_text


def describe_term(term: str | list[str]) -> str:
    if isinstance(term, list):
        return " / ".join(str(option) for option in term)
    return str(term)


def count_overload_mentions(text: str) -> int:
    patterns = [
        r"GetFinalPriceAsync\s*\(",
        r"overload\s*(?:#?\s*)?(\d+)",
        r"(?:first|second|third)\s+overload",
    ]
    signatures = len(re.findall(patterns[0], text, flags=re.IGNORECASE))
    numbered = {
        int(m)
        for m in re.findall(r"overload\s*#?\s*(\d+)", text, flags=re.IGNORECASE)
        if int(m) <= 10
    }
    ordinals = len(
        re.findall(
            r"\b(?:first|second|third)\s+overload\b",
            text,
            flags=re.IGNORECASE,
        )
    )
    return max(signatures, len(numbered), ordinals)


def extract_signatures(text: str) -> list[str]:
    normalized = normalize_text(text)
    matches = re.findall(
        r"GetFinalPriceAsync\s*\([^)]*\)",
        normalized,
        flags=re.IGNORECASE | re.DOTALL,
    )
    cleaned = []
    for match in matches:
        single_line = " ".join(match.split())
        cleaned.append(single_line)
    return cleaned


def parse_params(signature: str) -> dict[str, dict[str, str | None]]:
    inner = re.search(r"\((.*)\)", signature, flags=re.DOTALL)
    if not inner:
        return {}

    params: dict[str, dict[str, str | None]] = {}
    for chunk in split_params(inner.group(1)):
        chunk = chunk.strip()
        if not chunk:
            continue
        default = None
        if "=" in chunk:
            left, right = chunk.split("=", 1)
            default = right.strip()
            chunk = left.strip()
        parts = chunk.split()
        if len(parts) < 2:
            continue
        name = parts[-1]
        typ = " ".join(parts[:-1])
        params[normalize_name(name)] = {"type": normalize_type(typ), "default": default}
    return params


def split_params(param_blob: str) -> list[str]:
    parts: list[str] = []
    current: list[str] = []
    depth = 0
    for ch in param_blob:
        if ch == "<":
            depth += 1
        elif ch == ">":
            depth = max(depth - 1, 0)
        if ch == "," and depth == 0:
            parts.append("".join(current))
            current = []
            continue
        current.append(ch)
    if current:
        parts.append("".join(current))
    return parts


def normalize_name(name: str) -> str:
    return re.sub(r"[^a-z0-9]", "", name.lower())


def normalize_type(typ: str) -> str:
    typ = typ.strip()
    typ = re.sub(r"\s+", "", typ)
    typ = typ.replace("System.", "")
    return typ.lower()


def params_match(expected: list[dict[str, Any]], found: dict[str, dict[str, str | None]]) -> tuple[bool, str]:
    missing = []
    wrong = []

    for spec in expected:
        name = normalize_name(spec["name"])
        if name not in found:
            missing.append(spec["name"])
            continue
        actual = found[name]
        expected_type = normalize_type(spec["type"])
        actual_type = actual["type"] or ""
        if expected_type not in actual_type and actual_type not in expected_type:
            wrong.append(f"{spec['name']}: expected type {spec['type']}, saw {actual['type']}")
        if "default" in spec:
            expected_default = str(spec["default"]).lower()
            actual_default = (actual.get("default") or "").lower().replace(" ", "")
            if actual_default and actual_default != expected_default:
                wrong.append(
                    f"{spec['name']}: expected default {spec['default']}, saw {actual.get('default')}"
                )
            if not actual_default and expected_default in {"0", "true", "1"}:
                wrong.append(f"{spec['name']}: missing default value {spec['default']}")

    if missing or wrong:
        details = []
        if missing:
            details.append("missing: " + ", ".join(missing))
        if wrong:
            details.append("; ".join(wrong))
        return False, "; ".join(details)
    return True, "all expected parameters present"


def find_best_signature(signatures: list[str], required_names: set[str]) -> str | None:
    best = None
    best_score = -1
    for sig in signatures:
        params = parse_params(sig)
        score = len(required_names.intersection(params.keys()))
        if score > best_score:
            best = sig
            best_score = score
    if best_score <= 0:
        return None
    return best


def check_return_type(text: str, ground_truth: dict[str, Any]) -> CheckResult:
    weight = 20
    expected_parts = [
        "pricewithoutdiscounts",
        "finalprice",
        "applieddiscountamount",
        "applieddiscounts",
    ]
    normalized = normalize_text(text).lower()
    normalized = re.sub(r"\s+", "", normalized)

    has_task = "task<" in normalized or "task (" in normalized
    hits = sum(1 for part in expected_parts if part in normalized)
    has_list_discount = "list<discount>" in normalized or "list discount" in normalized.replace("_", " ")

    passed = has_task and hits >= 4 and ("list<discount>" in normalized or "listdiscount" in normalized or has_list_discount)
    detail = "return tuple with 4 named elements inside Task<>"
    if not passed:
        detail = f"task={has_task}, tuple_elements={hits}/4, list_discount={has_list_discount}"
    return CheckResult("return_type", weight, passed, detail)


def check_overload_signature(
    text: str,
    signatures: list[str],
    overload_spec: dict[str, Any],
    check_name: str,
    weight: int,
) -> CheckResult:
    required = {normalize_name(p["name"]) for p in overload_spec["parameters"]}
    best = find_best_signature(signatures, required)
    if not best:
        # Fallback: search in full text for distinctive parameter names.
        normalized = normalize_text(text).lower()
        distinctive = [p["name"] for p in overload_spec["parameters"] if p["name"] not in {"product", "customer", "store"}]
        hits = sum(1 for name in distinctive if name.lower() in normalized)
        if hits >= max(1, len(distinctive) - 1):
            return CheckResult(check_name, weight, True, "distinctive parameters found in prose")
        return CheckResult(check_name, weight, False, "no matching GetFinalPriceAsync signature found")

    params = parse_params(best)
    passed, detail = params_match(overload_spec["parameters"], params)
    return CheckResult(check_name, weight, passed, detail)


def check_source_grounding(text: str, ground_truth: dict[str, Any]) -> CheckResult:
    weight = 10
    normalized = normalize_text(text).lower()
    iface = ground_truth["interface"].lower()
    file_path = ground_truth["file"].lower()
    file_name = Path(ground_truth["file"]).name.lower()

    has_iface = iface in normalized
    has_file = file_path in normalized or file_name in normalized
    passed = has_iface or has_file
    detail = f"interface={has_iface}, file={has_file}"
    return CheckResult("source_grounding", weight, passed, detail)


def score_structured_terms(text: str, answer_path: str, ground_truth: dict[str, Any]) -> ScoreResult:
    normalized = normalize_for_terms(text)
    checks: list[CheckResult] = []

    for check in ground_truth.get("checks", []):
        name = check["name"]
        weight = int(check["weight"])
        required_terms = check.get("required_terms", [])
        missing = [term for term in required_terms if not term_found(normalized, term)]

        ordered_terms = check.get("ordered_terms", [])
        order_ok = True
        if ordered_terms:
            cursor = -1
            for term in ordered_terms:
                options = term if isinstance(term, list) else [term]
                positions = [
                    normalized.find(normalize_for_terms(str(option)), cursor + 1)
                    for option in options
                ]
                positions = [position for position in positions if position >= 0]
                if not positions:
                    order_ok = False
                    break
                cursor = min(positions)

        forbidden_terms = check.get("forbidden_terms", [])
        present_forbidden = [term for term in forbidden_terms if term_found(normalized, term)]

        passed = not missing and order_ok and not present_forbidden
        details = []
        if missing:
            details.append("missing: " + ", ".join(describe_term(term) for term in missing))
        if not order_ok:
            details.append("ordered terms not found in expected order")
        if present_forbidden:
            details.append("forbidden: " + ", ".join(describe_term(term) for term in present_forbidden))
        checks.append(CheckResult(name, weight, passed, "; ".join(details) if details else "all required terms present"))

    total = sum(c.weight for c in checks if c.passed)
    max_score = sum(int(check["weight"]) for check in ground_truth.get("checks", [])) or 100
    return ScoreResult(answer_path=answer_path, total_score=total, max_score=max_score, checks=checks)


def score_answer_text(text: str, answer_path: str = "<inline>", ground_truth: dict[str, Any] | None = None) -> ScoreResult:
    gt = ground_truth or load_ground_truth()
    if gt.get("rubric") == "structured_terms":
        return score_structured_terms(text, answer_path, gt)

    normalized = normalize_text(text)
    signatures = extract_signatures(normalized)

    checks: list[CheckResult] = []

    overload_count = count_overload_mentions(normalized)
    exact_three = overload_count == 3 or (
        len(signatures) == 3 and "4" not in re.findall(r"\b4\s+overloads?\b", normalized, flags=re.IGNORECASE)
    )
    if len(signatures) == 3:
        exact_three = True
    if re.search(r"\b(?:four|4)\s+overloads?\b", normalized, flags=re.IGNORECASE):
        exact_three = False
    checks.append(
        CheckResult(
            "overload_count",
            25,
            exact_three,
            f"detected_overloads={max(overload_count, len(signatures))}, signatures={len(signatures)}",
        )
    )

    checks.append(check_return_type(normalized, gt))
    checks.append(
        check_overload_signature(normalized, signatures, gt["overloads"][0], "overload_1_signature", 15)
    )
    checks.append(
        check_overload_signature(normalized, signatures, gt["overloads"][1], "overload_2_signature", 15)
    )
    checks.append(
        check_overload_signature(normalized, signatures, gt["overloads"][2], "overload_3_signature", 15)
    )
    checks.append(check_source_grounding(normalized, gt))

    total = sum(c.weight for c in checks if c.passed)
    return ScoreResult(answer_path=answer_path, total_score=total, checks=checks)


def score_answer_file(path: Path, ground_truth: dict[str, Any] | None = None) -> ScoreResult:
    text = path.read_text(encoding="utf-8")
    return score_answer_text(text, str(path), ground_truth)


def load_answer_from_artifact(path: Path) -> str:
    if path.suffix == ".json":
        with path.open(encoding="utf-8") as f:
            data = json.load(f)
        if isinstance(data, dict):
            return data.get("result") or data.get("answer") or ""
    return path.read_text(encoding="utf-8")


def score_run_directory(run_dir: Path, questions: list[dict[str, Any]] | None = None) -> dict[str, Any]:
    questions = questions or load_questions(run_dir=run_dir)
    results: dict[str, Any] = {
        "run_dir": str(run_dir),
        "questions": [
            {"id": question["id"], "prompt": question.get("prompt", "")}
            for question in questions
        ],
        "tools": {},
    }

    for tool in ("cursor", "claude"):
        tool_dir = run_dir / tool
        if not tool_dir.is_dir():
            continue
        tool_scores: dict[str, Any] = {"questions": {}}
        baseline_total = 0
        test_total = 0
        max_total = 0

        for question in questions:
            question_id = question["id"]
            ground_truth = load_ground_truth(Path(question["ground_truth_path"]))
            question_scores: dict[str, Any] = {}

            for phase in ["baseline", "test"]:
                label = f"{phase}_{question_id}"
                legacy_label = phase if len(questions) == 1 else label
                txt_path = tool_dir / f"{label}.txt"
                json_path = tool_dir / f"{label}.json"
                if not txt_path.exists() and not json_path.exists():
                    txt_path = tool_dir / f"{legacy_label}.txt"
                    json_path = tool_dir / f"{legacy_label}.json"
                answer_path = txt_path if txt_path.exists() else json_path
                if not answer_path.exists():
                    continue
                if answer_path.suffix == ".json":
                    text = load_answer_from_artifact(answer_path)
                    scored = score_answer_text(text, str(answer_path), ground_truth)
                else:
                    scored = score_answer_file(answer_path, ground_truth)
                question_scores[phase] = scored.to_dict()

            baseline = question_scores.get("baseline", {}).get("total_score")
            test = question_scores.get("test", {}).get("total_score")
            max_score = question_scores.get("baseline", {}).get("max_score") or question_scores.get("test", {}).get("max_score")
            if baseline is not None and test is not None:
                question_scores["degradation_delta"] = baseline - test
                baseline_total += baseline
                test_total += test
                max_total += int(max_score or 0)

            if question_scores:
                tool_scores["questions"][question_id] = question_scores

        if tool_scores["questions"]:
            tool_scores["aggregate"] = {
                "baseline_score": baseline_total,
                "test_score": test_total,
                "max_score": max_total,
                "degradation_delta": baseline_total - test_total,
            }
            results["tools"][tool] = tool_scores

    scores_path = run_dir / "scores.json"
    with scores_path.open("w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)

    for tool, tool_scores in results.get("tools", {}).items():
        tool_scores_path = run_dir / tool / "scores.json"
        with tool_scores_path.open("w", encoding="utf-8") as f:
            json.dump(tool_scores, f, indent=2)

    return results


def main() -> int:
    parser = argparse.ArgumentParser(description="Score GetFinalPriceAsync answers")
    parser.add_argument("--answer", type=Path, help="Score a single answer text/json file")
    parser.add_argument("--run-dir", type=Path, help="Score baseline/test answers in a run directory")
    parser.add_argument("--ground-truth", type=Path, default=GROUND_TRUTH_PATH)
    parser.add_argument("--json", action="store_true", help="Emit JSON")
    args = parser.parse_args()

    ground_truth = load_ground_truth(args.ground_truth)

    if args.answer:
        if args.answer.suffix == ".json":
            text = load_answer_from_artifact(args.answer)
            result = score_answer_text(text, str(args.answer), ground_truth)
        else:
            result = score_answer_file(args.answer, ground_truth)
        payload = result.to_dict()
        if args.json:
            print(json.dumps(payload, indent=2))
        else:
            print(f"Score: {payload['total_score']}/{payload['max_score']} ({args.answer})")
            for check in payload["checks"]:
                status = "PASS" if check["passed"] else "FAIL"
                print(f"  [{status}] {check['name']} ({check['weight']}): {check['detail']}")
        return 0

    if args.run_dir:
        payload = score_run_directory(args.run_dir)
        if args.json:
            print(json.dumps(payload, indent=2))
        else:
            print(json.dumps(payload, indent=2))
        return 0

    parser.print_help()
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
