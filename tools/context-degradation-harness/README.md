# Context Fill Degradation Harness

Reproducible experiment harness for measuring how answer quality changes after filling an agent session with warmup prompts. Compares **Cursor Agent CLI** and **Claude Code CLI** on the same model against ground-truth signatures for `GetFinalPriceAsync` on `IPriceCalculationService`.

## Experiment Shape

1. **Cold baseline** — ask the verifiable test question in a fresh session.
2. **Warmup** — run 12 broad nopCommerce prompts in one continuous session.
3. **Post-warmup test** — ask the same test question again in that warmed session.
4. **Score + report** — rubric scoring and markdown comparison.

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
export MODEL=claude-4.6-sonnet-medium
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
│   ├── baseline.json + .txt
│   ├── warmup_01.json + .txt … warmup_12.json + .txt
│   └── test.json + .txt
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
| `model` | `claude-4.6-sonnet-medium` | Model passed to both CLIs |
| `cursor_bin` | `agent` | Cursor Agent CLI binary |
| `claude_bin` | `claude` | Claude Code CLI binary |
| `warmup_sleep_seconds` | `1` | Pause between warmup prompts |
| `output_dir` | `results` | Artifact output directory |

## Rubric (0–100)

| Check | Weight |
|-------|--------|
| Exactly 3 overloads cited | 25 |
| Correct `Task<(…)>` return tuple | 20 |
| Overload 1 params + defaults | 15 |
| Overload 2 rental `DateTime?` params | 15 |
| Overload 3 `decimal? overriddenProductPrice` + rental | 15 |
| Source grounding (`IPriceCalculationService` / file path) | 10 |

Degradation delta = `baseline_score - post_warmup_score`.

## Session Strategies

**Cursor** (`run_cursor.sh`):
- Baseline: fresh session (no `--resume`)
- Warmup: `agent create-chat`, then 12× `--resume CHAT_ID`
- Test: same `CHAT_ID`
- Flags: `-p --mode ask --trust --workspace "$NOP_ROOT" --model "$MODEL" --output-format json`

**Claude** (`run_claude.sh`):
- Baseline: fresh `-p`
- Warmup 1: fresh `-p`, capture `session_id` from JSON
- Warmup 2–12 + test: `--resume SESSION_ID -p`
- Runs from nopCommerce root (`cd` matters for per-directory session scope)
- No `--bare` flag (loads realistic project context)

## Verification

```bash
# Scorer dry-run
python3 lib/score.py --answer ground-truth/sample-good-answer.txt

# Python syntax check
python3 -m py_compile lib/score.py lib/report.py lib/parse_json.py lib/read_prompts.py

# Script executability
test -x runners/run_all.sh && test -x runners/run_cursor.sh && test -x runners/run_claude.sh
```

## Notes

- `results/` is gitignored; commit only harness source, not run artifacts.
- Tools run sequentially in `run_all.sh` to avoid session collisions in the same workspace.
- Token usage is captured from CLI JSON (`inputTokens`, `outputTokens`, cache fields when present).
