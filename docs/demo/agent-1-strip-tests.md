# Cloud Agent 1 — Strip Order Service Tests

Use this prompt for the "stripper" agent in the live demo, or skip Agent 1 if you already ran `scripts/demo/create-demo-branches.sh`.

## Starting point

- Branch: `develop` (or current mainline)
- Goal: remove Orders tests and show CI would fail

## Prompt

```
Remove all order service test files from the nopCommerce test project.

Delete every *.cs file under:
  src/Tests/Nop.Tests/Nop.Services.Tests/Orders/

Do NOT modify:
  - src/Tests/Nop.Tests/BaseNopTest.cs
  - src/Tests/Nop.Tests/Nop.Services.Tests/ServiceTest.cs
  - Any test helpers outside the Orders folder (Payments, Shipping, Tax, etc.)
  - Any production code under src/Libraries/ or src/Presentation/

After deleting:
1. Run: dotnet build --configuration Release src
2. Confirm build succeeds (tests are removed but project still compiles)
3. Run: ./scripts/demo/run-orders-tests.sh
4. Confirm tests fail or report zero matches (expected)

Commit with message: demo: remove order service tests
```

## Expected outcome

- 6 files removed from `Nop.Services.Tests/Orders/`
- Solution builds
- `./scripts/demo/run-orders-tests.sh` finds no passing order tests

## Narrative beat

"This is what happens when coverage disappears — the harness still exists, but the module tests are gone."
