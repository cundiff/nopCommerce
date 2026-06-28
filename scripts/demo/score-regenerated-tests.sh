#!/usr/bin/env bash
# Compare regenerated order tests on the current branch vs ground truth.
#
# Usage:
#   ./scripts/demo/score-regenerated-tests.sh
#   GROUND_TRUTH_REF=demo/ground-truth-orders-tests ./scripts/demo/score-regenerated-tests.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

GROUND_TRUTH_REF="${GROUND_TRUTH_REF:-demo/ground-truth-orders-tests}"
ORDERS_DIR="src/Tests/Nop.Tests/Nop.Services.Tests/Orders"
CURRENT_REF="HEAD"

if ! git rev-parse --verify "$GROUND_TRUTH_REF" >/dev/null 2>&1; then
  echo "Error: ground truth ref '$GROUND_TRUTH_REF' not found." >&2
  echo "Run ./scripts/demo/create-demo-branches.sh first." >&2
  exit 1
fi

count_test_attributes() {
  local ref="$1"
  local total=0

  while IFS= read -r file; do
    [[ -z "$file" ]] && continue
    local count
    count="$(git show "$ref:$file" 2>/dev/null | grep -cE '\[(Test|TestCase)\]' || true)"
    total=$((total + count))
  done < <(git ls-tree -r --name-only "$ref" -- "$ORDERS_DIR" 2>/dev/null | grep 'Tests\.cs$' || true)

  echo "$total"
}

count_test_files() {
  local ref="$1"
  git ls-tree -r --name-only "$ref" -- "$ORDERS_DIR" 2>/dev/null | grep -c 'Tests\.cs$' || echo 0
}

gt_files="$(count_test_files "$GROUND_TRUTH_REF")"
cur_files="$(count_test_files "$CURRENT_REF")"
gt_tests="$(count_test_attributes "$GROUND_TRUTH_REF")"
cur_tests="$(count_test_attributes "$CURRENT_REF")"

echo "=== Order test coverage score ==="
echo ""
echo "Ground truth: $GROUND_TRUTH_REF ($(git rev-parse --short "$GROUND_TRUTH_REF"))"
echo "Current:      $CURRENT_REF ($(git rev-parse --short "$CURRENT_REF"))"
echo ""
echo "Test files:   $cur_files / $gt_files"
echo "Test methods: $cur_tests / $gt_tests ([Test] + [TestCase])"

if [[ "$gt_tests" -gt 0 ]]; then
  pct=$((cur_tests * 100 / gt_tests))
  echo "Recovery:     ${pct}%"
else
  echo "Recovery:     n/a (no ground truth tests found)"
fi

echo ""
echo "=== Diffstat ($ORDERS_DIR) ==="
git diff --stat "$GROUND_TRUTH_REF".."$CURRENT_REF" -- "$ORDERS_DIR" || true

echo ""
echo "=== File list (current) ==="
git ls-tree -r --name-only "$CURRENT_REF" -- "$ORDERS_DIR" 2>/dev/null | grep 'Tests\.cs$' || echo "(none)"
