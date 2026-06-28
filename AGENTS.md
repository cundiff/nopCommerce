# AGENTS.md

## Cursor Cloud specific instructions

This repo is **nopCommerce** — an ASP.NET Core (.NET 10) eCommerce store. `global.json` pins SDK `10.0.100` (rollForward `latestFeature`). The `.cursor/skills/*` describe a Docker-based bring-up; in this cloud environment the app is run directly with the **.NET SDK in Development mode** against a local **PostgreSQL** database, which is faster and gives a real dev server.

### Services
- **Web store (`Nop.Web`)** — Kestrel on port `8080`.
  - Run (dev), from `src/Presentation/Nop.Web`:
    `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet run -c Debug`
  - `/` redirects to `/install` until the store is installed, then serves the storefront. Admin area is at `/admin` (seeded admin: `admin@example.com`).
  - Build whole solution: `dotnet build NopCommerce.sln -c Debug` (run from `src`).
  - Tests: `dotnet test Tests/Nop.Tests/Nop.Tests.csproj` (run from `src`).
- **PostgreSQL 16** — local, port `5432`, user `postgres` / password `nopCommerce_db_password`, database `nopcommerce`. Start it before running the app: `sudo service postgresql start` (it is not auto-started on boot).

### Non-obvious notes / gotchas
- **The store is already installed** in this environment: `src/Presentation/Nop.Web/App_Data/appsettings.json` (gitignored) holds the PostgreSQL connection string and the DB is seeded with sample data. Just start PostgreSQL and run the app — no reinstall needed.
- **`citext` extension is required** for PostgreSQL installs. Before running the web installer, the target database must have it: `CREATE EXTENSION IF NOT EXISTS citext;`. Otherwise install fails with `42704: type "citext" does not exist`.
- **Post-install restart quirk:** finishing the web installer makes nopCommerce restart the app, which simply **stops the `dotnet run` process** (there is no process manager to bring it back). After install completes, re-run the `dotnet run` command to serve the store.
- **To reinstall from scratch:** stop the app, recreate the DB with citext
  (`sudo -u postgres psql -c "DROP DATABASE IF EXISTS nopcommerce;" -c "CREATE DATABASE nopcommerce;"` then `sudo -u postgres psql -d nopcommerce -c "CREATE EXTENSION IF NOT EXISTS citext;"`),
  and clear the `ConnectionString` value in `App_Data/appsettings.json`.
- **Tests:** the 3 `CanCheckVatNumber` cases call the external EU VIES SOAP service and fail under restricted egress; treat them as environment limitations, not regressions. (One other pre-existing unit failure may also appear.)
