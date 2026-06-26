# Context Fill Degradation Harness

Reproducible experiment harness for measuring how answer quality changes after filling an agent session with warmup prompts. Compares **Cursor Agent CLI** and **Claude Code CLI** against a small suite of nopCommerce code-understanding questions.

## Experiment Shape

1. **Cold baseline** — ask every benchmark question in fresh sessions.
2. **Warmup** — run 12 broad nopCommerce prompts in one continuous session.
3. **Post-warmup test** — ask every benchmark question again in that warmed session.
4. **Score + report** — per-question rubric scoring, aggregate deltas, and markdown comparison.

## Prerequisites

- nopCommerce repo checkout (this harness lives under `tools/context-degradation-harness/`)
- **Cursor Agent CLI** (`agent`) authenticated and available on `PATH`
- **Claude Code CLI** (`claude`) authenticated and available on `PATH`
- **Python 3.9+**

### Claude CLI / `CLAUDE_BIN`

Some environments wrap `claude` behind a shell function (e.g. internal `comproxy`). If the wrapper is unavailable outside that environment, point the harness at the real Anthropic CLI binary:

```bash
export CLAUDE_BIN=/path/to/real/claude
./runners/run_claude.sh
```

Override any config value via environment variables after sourcing is not needed — edit `config.yaml` or export before running:

```bash
export CURSOR_MODEL=claude-4.6-sonnet-medium
export CLAUDE_MODEL=sonnet
export CLAUDE_BIN=claude
```

## Quick Start

From the harness directory:

```bash
cd tools/context-degradation-harness
./runners/run_all.sh
```

This creates `results/<timestamp>/` with:

```
results/<timestamp>/
├── manifest.json
├── scores.json
├── report.md
├── cursor/
│   ├── baseline_<question_id>.json + .txt
│   ├── warmup_01.json + .txt … warmup_12.json + .txt
│   └── test_<question_id>.json + .txt
└── claude/
    └── (same layout)
```

## Individual Runners

Run a single tool (creates its own timestamped run dir unless `RUN_DIR` is set):

```bash
./runners/run_cursor.sh
./runners/run_claude.sh
```

Share one run directory across both tools:

```bash
export RUN_DIR="results/manual-run"
mkdir -p "$RUN_DIR"
./runners/run_cursor.sh
./runners/run_claude.sh
python3 lib/score.py --run-dir "$RUN_DIR"
python3 lib/report.py "$RUN_DIR"
```

## Scoring Only

Score a hand-crafted answer:

```bash
python3 lib/score.py --answer ground-truth/sample-good-answer.txt
```

Score an existing run:

```bash
python3 lib/score.py --run-dir results/<timestamp> --json
```

## Configuration

`config.yaml` defaults:

| Key | Default | Description |
|-----|---------|-------------|
| `workspace` | `../..` | nopCommerce root relative to harness dir |
| `model` | unset | Optional shared model passed to both CLIs when tool-specific values are not set |
| `cursor_model` | `claude-4.6-sonnet-medium` | Model passed to Cursor Agent CLI |
| `claude_model` | `sonnet` | Model passed to Claude Code CLI |
| `cursor_bin` | `agent` | Cursor Agent CLI binary |
| `claude_bin` | `claude` | Claude Code CLI binary |
| `warmup_sleep_seconds` | `1` | Pause between warmup prompts |
| `warmup_prompt_count` | `12` | Expected number of warmup prompts |
| `question_manifest` | `prompts/questions.json` | Benchmark question manifest |
| `output_dir` | `results` | Artifact output directory |

## Question Suite

`prompts/questions.json` defines the benchmark suite:

```json
[
  {
    "id": "order_status_completion",
    "prompt": "Describe exactly when order status changes...",
    "ground_truth": "../ground-truth/order-status-completion.json"
  }
]
```

The default suite includes:

- `get_final_price_overloads` — calibration lookup for exact overload signatures.
- `order_status_completion` — status transition truth table and settings branches.
- `discount_validation_chain` — ordered validation guards and requirement tree behavior.
- `cart_total_pipeline` — total calculation sequence and null-total edge case.
- `payment_processing_paths` — standard, recurring, and zero-total payment branches.

## Rubrics

Ground-truth files support two rubric shapes:

- Legacy overload rubric for `get_final_price_overloads`.
- `structured_terms` rubric for harder behavioral questions. Each weighted check lists required terms, optional ordered terms, and optional forbidden terms.

Per-question degradation delta is `baseline_score - post_warmup_score`. Aggregate degradation is the sum of per-question baseline scores minus the sum of post-warmup scores.

## Session Strategies

**Cursor** (`run_cursor.sh`):
- Baseline: fresh session per benchmark question (no `--resume`)
- Warmup: `agent create-chat`, then 12× `--resume CHAT_ID`
- Test: every benchmark question in the same warmed `CHAT_ID`
- Flags: `-p --mode ask --trust --workspace "$NOP_ROOT" --model "$CURSOR_MODEL" --output-format json`

**Claude** (`run_claude.sh`):
- Baseline: fresh `-p` per benchmark question
- Warmup 1: fresh `-p`, capture `session_id` from JSON
- Warmup 2–12 + tests: `--resume SESSION_ID -p`
- Uses `--model "$CLAUDE_MODEL"` because Claude Code model aliases differ from Cursor model IDs
- Runs from nopCommerce root (`cd` matters for per-directory session scope)
- No `--bare` flag (loads realistic project context)

## Verification

```bash
# Scorer dry-run
python3 lib/score.py --answer ground-truth/sample-good-answer.txt

# Python syntax check
python3 -m py_compile lib/score.py lib/report.py lib/parse_json.py lib/read_prompts.py lib/validate_artifact.py

# Regression tests
python3 -m unittest discover tests

# Script executability
test -x runners/run_all.sh && test -x runners/run_cursor.sh && test -x runners/run_claude.sh
```

## Notes

- `results/` is gitignored; commit only harness source, not run artifacts.
- Tools run sequentially in `run_all.sh` to avoid session collisions in the same workspace.
- Token usage is captured from CLI JSON (`inputTokens`, `outputTokens`, cache fields when present).
- Parsed CLI artifacts with `is_error: true`, parse errors, or empty results fail the run instead of being scored.
- Treat results as a single-run CLI-stack comparison. Repeat runs before making strong degradation or cross-tool claims.
