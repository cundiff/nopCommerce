# AGENTS.md

## Cursor Cloud specific instructions

This repo is **nopCommerce** — an ASP.NET Core (.NET 10) e-commerce app. The only runnable
web project is `src/Presentation/Nop.Web`. Standard build/test/run commands live in the CI
workflow `.github/workflows/dotnet.yml`; reference that instead of inventing new ones.

### Services
- **Nop.Web** — the storefront + admin web app (`src/Presentation/Nop.Web`, .NET 10).
- **PostgreSQL 16** — the dev database (installed natively). nopCommerce also supports SQL
  Server and MySQL, but this environment is set up with PostgreSQL. There is a `Dockerfile` /
  `docker-compose.yml` (SQL Server) in the repo, but Docker is **not** used here — the dev
  setup runs .NET and PostgreSQL natively.

### Database (already installed & seeded by setup)
- PostgreSQL must be running before starting the app; it does **not** auto-start on VM boot:
  `sudo pg_ctlcluster 16 main start`
- Role `nop` (password `nopCommerce_db_password`, has `CREATEDB`+`SUPERUSER`), database
  `nopcommerce`, both seeded with sample data.
- App DB config lives in `src/Presentation/Nop.Web/App_Data/appsettings.json` (gitignored).
  Connection string: `Host=127.0.0.1;Database=nopcommerce;Username=nop;Password=nopCommerce_db_password`.
- Admin login for the seeded store: `admin@nopcommerce.local` / `Admin@12345`.

### Running the app
- From `src/Presentation/Nop.Web`: `ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet run -c Release`
  (default Kestrel port is 5000 since there is no `launchSettings.json`).
- Until a database is installed, the app **redirects `/` to `/install`** (the first-run wizard).
- After completing the installer, the running process caches "installed" state — **restart the
  app** so it picks up the new `appsettings.json`.

### Non-obvious gotchas
- **PostgreSQL install gotcha:** if you ever re-run the `/install` wizard against PostgreSQL,
  CHECK "Create database if it doesn't exist" and do **not** pre-create the database manually.
  nopCommerce only enables the `citext`/`pgcrypto` extensions and calls Npgsql `ReloadTypes()`
  inside its own `CreateDatabase()` path. Installing into a manually-created DB makes seeding
  fail with `Reading as 'System.Object' is not supported for fields having DataTypeName '-'`.
- **3 tests require external network:** `CanCheckVatNumber` in
  `src/Tests/Nop.Tests/Nop.Services.Tests/Tax/TaxServiceTests.cs` call the live EU VIES VAT
  service at `ec.europa.eu`, which is blocked by egress here. These 3 fail with network errors;
  the other ~1083 tests pass. This is an environment limitation, not a code issue.
- Tests use SQLite in-memory (no external DB needed); only the running app needs PostgreSQL.
