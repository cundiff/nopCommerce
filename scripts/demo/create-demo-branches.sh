#!/usr/bin/env bash
# Create demo branches for the test-coverage cloud agent demo.
#
# - demo/ground-truth-orders-tests  → source branch with full Orders tests (scoring only)
# - demo/no-tests-orders            → orphan branch, single commit, no Orders test files
#
# Usage:
#   ./scripts/demo/create-demo-branches.sh [--push]
#   SOURCE_BRANCH=develop ./scripts/demo/create-demo-branches.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

SOURCE_BRANCH="${SOURCE_BRANCH:-}"
PUSH=false

for arg in "$@"; do
  case "$arg" in
    --push) PUSH=true ;;
    -h|--help)
      echo "Usage: $0 [--push]"
      echo "  SOURCE_BRANCH  Branch to snapshot (default: current branch)"
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$SOURCE_BRANCH" ]]; then
  SOURCE_BRANCH="$(git branch --show-current || true)"
fi

if [[ -z "$SOURCE_BRANCH" ]] || ! git rev-parse --verify "$SOURCE_BRANCH" >/dev/null 2>&1; then
  echo "Error: SOURCE_BRANCH must be a valid ref (got: '${SOURCE_BRANCH:-<empty>}')" >&2
  exit 1
fi

if ! git diff --quiet || ! git diff --cached --quiet; then
  echo "Error: working tree has uncommitted changes. Commit or stash before running." >&2
  echo "       (This script resets the working tree while creating the orphan branch.)" >&2
  exit 1
fi

GROUND_TRUTH_REF="demo/ground-truth-orders-tests"
NO_TESTS_BRANCH="demo/no-tests-orders"
ORDERS_TESTS_DIR="src/Tests/Nop.Tests/Nop.Services.Tests/Orders"
ORIGINAL_BRANCH="$(git branch --show-current 2>/dev/null || true)"
ORIGINAL_HEAD="$(git rev-parse HEAD)"

echo "==> Source: $SOURCE_BRANCH ($(git rev-parse --short "$SOURCE_BRANCH"))"

# Ground truth: branch pointer at source (tests intact)
git branch -f "$GROUND_TRUTH_REF" "$SOURCE_BRANCH"
echo "==> Ground truth branch: $GROUND_TRUTH_REF"

# Remove local no-tests branch if re-running
git branch -D "$NO_TESTS_BRANCH" 2>/dev/null || true

# Orphan branch: one root commit, no history
git checkout --orphan "$NO_TESTS_BRANCH"
git reset --hard

git checkout "$SOURCE_BRANCH" -- .

# Remove order test files only
if [[ -d "$ORDERS_TESTS_DIR" ]]; then
  find "$ORDERS_TESTS_DIR" -maxdepth 1 -name '*Tests.cs' -type f -delete
fi

git add -A

if git diff --cached --quiet; then
  echo "Error: nothing to commit on orphan branch (order tests may already be absent)" >&2
  git checkout "$ORIGINAL_HEAD" 2>/dev/null || git checkout "$SOURCE_BRANCH"
  exit 1
fi

git commit -m "demo: baseline without order service tests"

COMMIT_COUNT="$(git rev-list --count HEAD)"
TEST_FILE_COUNT="$(find "$ORDERS_TESTS_DIR" -maxdepth 1 -name '*Tests.cs' 2>/dev/null | wc -l | tr -d ' ')"

echo ""
echo "==> Created $NO_TESTS_BRANCH"
echo "    Root commit: $(git rev-parse --short HEAD)"
echo "    Commit count: $COMMIT_COUNT (expect 1)"
echo "    Orders *Tests.cs files: $TEST_FILE_COUNT (expect 0)"

if [[ "$COMMIT_COUNT" != "1" ]]; then
  echo "Warning: expected exactly 1 commit on orphan branch" >&2
fi

if [[ "$TEST_FILE_COUNT" != "0" ]]; then
  echo "Error: order test files still present" >&2
  exit 1
fi

# Verify build
echo ""
echo "==> Verifying Release build..."
dotnet build --configuration Release src >/dev/null
echo "    Build OK"

# Return to original branch
if [[ -n "$ORIGINAL_BRANCH" ]]; then
  git checkout "$ORIGINAL_BRANCH" 2>/dev/null || git checkout "$SOURCE_BRANCH"
else
  git checkout "$SOURCE_BRANCH"
fi

if [[ "$PUSH" == true ]]; then
  echo ""
  echo "==> Pushing branches to origin..."
  git push -u origin "$GROUND_TRUTH_REF"
  git push -u origin "$NO_TESTS_BRANCH" --force
  echo "    Pushed $GROUND_TRUTH_REF and $NO_TESTS_BRANCH"
fi

echo ""
echo "Done."
echo "  Agent 2 start:  git checkout $NO_TESTS_BRANCH"
echo "  Score:          ./scripts/demo/score-regenerated-tests.sh"
