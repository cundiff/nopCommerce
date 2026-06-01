# Headless Storefront API plugin

REST API for [Next.js Commerce](https://github.com/vercel/commerce) and other headless frontends.

## Enable

1. Build the solution (`dotnet build NopCommerce.sln`).
2. Admin → Configuration → Local plugins → **Headless Storefront API** → Install.
3. Restart the application if prompted.

## Endpoints

Base path: `/headless/v1`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Health check |
| GET | `/categories` | List categories (collections) |
| GET | `/categories/{seName}` | Category by URL slug |
| GET | `/categories/{seName}/products` | Products in category |
| GET | `/products` | Search/list products |
| GET | `/products/{seName}` | Product by URL slug |
| POST | `/sessions` | Create guest session → `{ sessionToken }` |
| GET | `/cart` | Get cart (`X-Storefront-Session` header) |
| POST | `/cart/items` | Add line item |
| PATCH | `/cart/items/{id}` | Update quantity |
| DELETE | `/cart/items/{id}` | Remove line |
| POST | `/checkout` | Returns `{ checkoutUrl }` handoff URL |
| GET | `/checkout/handoff?token=` | Sets guest cookie and redirects to nop checkout |

## Local Docker

```bash
docker compose up
```

Complete the install wizard, enable this plugin, and point Next.js at `NOPCOMMERCE_API_URL=http://localhost/headless/v1`.
