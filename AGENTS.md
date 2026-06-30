# AGENTS.md

## Cursor Cloud specific instructions

nopCommerce is an ASP.NET Core (.NET 10) eCommerce app. The .NET 10 SDK is preinstalled
on the cloud VM, so build and run **natively** with `dotnet`. The repo ships Docker assets
and a `start-local-nopcommerce` skill, but **Docker is not installed** here — ignore the
Docker path and use the SDK directly.

### Services / how to run

- **Web app (`Nop.Web`)** — the storefront + admin. Solution: `src/NopCommerce.sln`,
  web project: `src/Presentation/Nop.Web`.
  - Build (whole solution): `dotnet build src/NopCommerce.sln -c Debug`
  - Run in dev mode (from `src/Presentation/Nop.Web`):
    `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet run -c Debug --project Nop.Web.csproj`
  - Storefront: `http://localhost:5000/` · Admin: `http://localhost:5000/Admin`
    (seeded admin: `admin@yourStore.com` / `Admin@12345`).

- **PostgreSQL** — the app database. The cluster is preinstalled but a fresh VM may have it
  stopped; start it with `sudo service postgresql start`. Database `nopcommerce`, login role
  `nopcommerce` (password `nopCommerce_db_password`).
  - **Non-obvious, important:** on PostgreSQL 15+ the app role must **own the `public`
    schema**, otherwise FluentMigrator installation fails with `permission denied for schema
    public`. The DB was created with `OWNER nopcommerce` and `ALTER SCHEMA public OWNER TO
    nopcommerce`, plus the `citext` and `pgcrypto` extensions. The installer only auto-creates
    those extensions when it creates the database itself, so a pre-created DB needs them added
    manually.

### Install / first run

The store is installed via the web wizard at `/install`; the chosen DB connection is persisted
to `src/Presentation/Nop.Web/App_Data/appsettings.json` (gitignored). If that file is missing
or the DB is empty, the app redirects to `/install` — re-run the wizard (DataProvider
PostgreSQL, server `localhost`, database `nopcommerce`, the role above, leave "create database"
unchecked since it already exists). Sample data is optional but useful for demos.

- **Caveat:** the installer's "Restart application" / "Restart installation" buttons call
  `RestartAppDomain`, which stops the `dotnet run` process. Just relaunch the run command above;
  it comes back up already-installed because the connection is saved in `appsettings.json`.

### Tests

- `dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj` — unit/integration tests run against an
  in-memory **SQLite** DB, so they need no external database or running PostgreSQL.
- The 3 `CanCheckVatNumber` tests call the external EU VIES / UK HMRC VAT validation web
  services and **fail when outbound network to those services is blocked** — this is an
  environment limitation, not a code regression.
