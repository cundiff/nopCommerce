# nopCommerce Test Coverage Cloud Agent Demo

Repeatable demo showing cloud agents recovering test coverage from production code alone — without access to deleted test files or git history that reveals them.

## Demo target

**Slice:** [`src/Tests/Nop.Tests/Nop.Services.Tests/Orders/`](../src/Tests/Nop.Tests/Nop.Services.Tests/Orders/)

| File | Source under test |
|------|-------------------|
| `OrderCustomValuesTests.cs` | Order custom values |
| `GiftCardServiceTests.cs` | [`GiftCardService`](../../src/Libraries/Nop.Services/Orders/GiftCardService.cs) |
| `CheckoutAttributeParserAndFormatterTests.cs` | Checkout attribute parsing |
| `OrderServiceTests.cs` | [`OrderService`](../../src/Libraries/Nop.Services/Orders/OrderService.cs) |
| `OrderTotalCalculationServiceTests.cs` | Order total / tax / shipping math |
| `OrderProcessingServiceTests.cs` | [`OrderProcessingService`](../../src/Libraries/Nop.Services/Orders/OrderProcessingService.cs) |

Roughly 67 test methods across 6 files. Tests inherit from [`ServiceTest`](../../src/Tests/Nop.Tests/Nop.Services.Tests/ServiceTest.cs), which wires SQLite plus fake payment, shipping, and tax plugins.

**Do not delete or modify:** `BaseNopTest.cs`, `ServiceTest.cs`, or test helpers outside the Orders folder.

## Quick start

From the repo root (commit or stash local changes first — the branch script resets the working tree):

```bash
# 1. Create demo branches (ground truth + orphan no-tests baseline)
./scripts/demo/create-demo-branches.sh

# 2. Optional: push branches for cloud agents
./scripts/demo/create-demo-branches.sh --push

# 3. Run order tests (validation loop oracle)
./scripts/demo/run-orders-tests.sh

# 4. After Agent 2 finishes, score recovery vs ground truth
./scripts/demo/score-regenerated-tests.sh
```

## Branch layout

| Branch / ref | Purpose | Who sees it |
|--------------|---------|-------------|
| `demo/ground-truth-orders-tests` | Full Orders tests intact | Demo operator only (scoring) |
| `demo/no-tests-orders` | Single orphan commit, no Orders test files | Cloud Agent 2 starting point |
| Agent 2 output branch / PR | Regenerated tests | Live demo audience |

The orphan branch has **one root commit** with no parent history referencing deleted tests. Agent 2 cannot `git log` or diff against prior test files on that branch.

## Live demo script

| Act | Action | Notes |
|-----|--------|-------|
| 0 | Run `create-demo-branches.sh` | Pre-demo; confirm orphan branch has 1 commit |
| 1 | Launch Agent 1 (optional) | See [agent-1-strip-tests.md](agent-1-strip-tests.md); or show pre-stripped branch |
| 2 | Launch Agent 2 from `demo/no-tests-orders` | See [agent-2-regenerate-tests.md](agent-2-regenerate-tests.md) |
| 3 | Watch scoped tests pass, then full suite | `./scripts/demo/run-orders-tests.sh` then `dotnet test src` |
| 4 | (Optional) Browser smoke test | See [browser-smoke-checklist.md](browser-smoke-checklist.md) |
| 5 | Score vs ground truth | `./scripts/demo/score-regenerated-tests.sh` |

## Agent prompts

- **Agent 1 (stripper):** [agent-1-strip-tests.md](agent-1-strip-tests.md)
- **Agent 2 (generator + loop):** [agent-2-regenerate-tests.md](agent-2-regenerate-tests.md)

## Validation commands

Scoped loop (Agent 2 runs after each file):

```bash
./scripts/demo/run-orders-tests.sh
```

Final gate (matches [CI](../../.github/workflows/dotnet.yml)):

```bash
dotnet restore src
dotnet build --no-restore --configuration Release src
dotnet test --no-build --configuration Release --verbosity normal src
```

## Scoring rubric

Run `./scripts/demo/score-regenerated-tests.sh` to compare the current branch against `demo/ground-truth-orders-tests`:

| Metric | Target |
|--------|--------|
| Test method count (`[Test]` + `[TestCase]`) | ≥ 50% of ground truth |
| Files present | 6 / 6 |
| `dotnet test --filter Orders` | All pass |
| Full `dotnet test src` | All pass (no regressions) |

Manual review: do regenerated tests cover core behaviors (cancel/refund/capture rules, order GUID generation, total calculation edge cases)?

## Fast alternate slice

If demo time is tight, use [`Nop.Web.Tests/Public/Validators/`](../src/Tests/Nop.Tests/Nop.Web.Tests/Public/Validators/) instead — pure unit tests, faster loop, weaker narrative. Adjust scripts to filter `FullyQualifiedName~Validators`.

## Risks

| Risk | Mitigation |
|------|------------|
| Agent copies patterns from other test folders | Expected; demo measures reconstruction quality |
| `BaseNopTest` init is slow | Warn audience; use scoped filter |
| Agent breaks non-Orders tests | Final gate runs full suite |
| Demo overruns | Stop after 3 of 6 Order files or switch to Validators slice |
| Uncommitted work lost during branch setup | Commit first; script refuses dirty working tree |

## Teardown

Demo branches are isolated from `develop`. Delete when done:

```bash
git branch -D demo/no-tests-orders demo/ground-truth-orders-tests
git push origin --delete demo/no-tests-orders demo/ground-truth-orders-tests  # if pushed
```
