# Browser Smoke Test Checklist (Optional)

Lightweight UI validation after unit tests pass. No Playwright infra required — use Cursor browser automation or manual walkthrough.

## Prerequisites

Start nopCommerce locally (see [`.cursor/skills/start-local-nopcommerce/SKILL.md`](../../.cursor/skills/start-local-nopcommerce/SKILL.md)):

```bash
docker build --platform linux/amd64 -t nopcommerce-local-amd64 .
docker run --platform linux/amd64 --name nopcommerce-local -p 8080:80 nopcommerce-local-amd64
```

## Checklist

| Step | URL / action | Expected |
|------|--------------|----------|
| 1 | `http://localhost:8080/install` | Installer loads (first run) or redirect to store |
| 2 | Complete install (if needed) | Store homepage loads |
| 3 | Browse catalog → open a product | Product detail page renders |
| 4 | Add to cart | Cart badge updates |
| 5 | Checkout flow | Shipping/payment steps load without 500 |
| 6 | Place order (test payment) | Order confirmation page |

## Cloud agent prompt (optional final step)

```
After all unit tests pass, start nopCommerce in Docker on port 8080.
Navigate to the store in the browser.
Verify: homepage loads, a product can be added to cart, and checkout reaches the payment step without errors.
Take a snapshot of the checkout page as proof.
```

## Time cap

Keep this under 5 minutes in a live demo. Skip install if the container is pre-seeded.
