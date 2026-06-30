# AGENTS.md

## Cursor Cloud specific instructions

This repo is **nopCommerce**, an ASP.NET Core (.NET 10) eCommerce app. The single runnable
product is the `Nop.Web` site, which serves both the public storefront and the `/admin` area.

The .NET 10 SDK is preinstalled and the startup update script runs `dotnet restore src/NopCommerce.sln`.
Do **not** use Docker here (it is not installed); run the app directly with the SDK.

### Database (PostgreSQL) — must be started each session
A local PostgreSQL 16 cluster holds an already-installed store (schema + sample data). The data
persists in the VM snapshot, but the cluster does **not** auto-start. Start it before running the app:

```bash
sudo pg_ctlcluster 16 main start   # or: sudo service postgresql start
```

- Database `nopcommerce`, role `nop`, password `nopCommerce_db_password` (local dev only; role is a superuser).
- DB connection settings live in gitignored `src/Presentation/Nop.Web/App_Data/appsettings.json`
  under `ConnectionStrings` (`DataProvider: postgresql`). If that file's `ConnectionString` is empty,
  the app treats itself as not-installed and serves the `/install` wizard instead of the store.
- Store admin login: `admin@yourStore.com` / `admin123`.

### Run the app (development)
From `src/Presentation/Nop.Web`:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet run --project Nop.Web.csproj
```

Then open `http://localhost:5000/` (storefront) or `http://localhost:5000/admin` (admin).

- Gotcha: completing the installer, or clicking "Restart application" in admin, calls
  `RestartAppDomain()` which **terminates `dotnet run`** (it does not auto-relaunch). Just start it again.

### Re-installing into a fresh database (only if you wipe the DB)
The installer auto-creates the required `citext` and `pgcrypto` extensions **only when it creates the
database itself** (the "Create database if it doesn't exist" option). If you pre-create an empty
`nopcommerce` database manually, you must add the extensions first or migrations fail with
`type "citext" does not exist`:

```bash
sudo -u postgres psql -d nopcommerce -c "CREATE EXTENSION IF NOT EXISTS citext; CREATE EXTENSION IF NOT EXISTS pgcrypto;"
```

### Build, lint, and test
- Build / lint: `dotnet build src/NopCommerce.sln` — Roslyn analyzers run during the build; there is
  no separate linter. (A few obsolete-API/`CS0108` warnings are pre-existing and harmless.)
- Tests: `dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj` — these use in-memory SQLite, so no
  external database is needed. The 3 `CanCheckVatNumber` tests call the EU VIES VAT web service and
  fail without outbound internet; that is an environment limitation, not a code problem.
