# AGENTS.md

## Cursor Cloud specific instructions

This repo is **nopCommerce**, a .NET 10 ASP.NET Core MVC eCommerce app (storefront + admin in a
single web project) backed by a relational database. The runnable web app is
`src/Presentation/Nop.Web` (`Nop.Web.csproj`); the only test project is
`src/Tests/Nop.Tests/Nop.Tests.csproj` (NUnit). See `README.md` and the
`.cursor/skills/start-local-nopcommerce` skill for general context (that skill uses Docker; in this
cloud VM we run directly with the .NET SDK + a local PostgreSQL instead).

### What is already provisioned (persists in the VM snapshot)
- **.NET 10 SDK** (apt `dotnet-sdk-10.0`) and **PostgreSQL 16** (apt) are installed.
- NuGet packages are refreshed automatically by the startup update script
  (`dotnet restore src/NopCommerce.sln`).
- The store is **already installed**: a PostgreSQL database `nopcommerce` (role `nop` / password
  `noppass`) with sample data, and the connection string is saved in
  `src/Presentation/Nop.Web/App_Data/appsettings.json`. Admin login: `admin@nopcommerce.local` /
  `Admin@12345`.

### Starting services (NOT done by the update script — do these yourself)
1. **Start PostgreSQL** (it does not auto-start on boot):
   `sudo pg_ctlcluster 16 main start`
2. **Frontend libs** — `src/Presentation/Nop.Web/wwwroot/lib_npm` (the JS/CSS the views reference) is
   **committed**, so the storefront renders out of the box; no npm/gulp step is needed just to run.
   For frontend work, install node deps with `npm install --prefix src/Presentation/Nop.Web` and
   regenerate the vendored libs by running `npx gulp` from `src/Presentation/Nop.Web` (clean →
   copyDependencies → prepareCldr). Note `npm install` rewrites `package-lock.json`.
3. **Run the web app** from `src/Presentation/Nop.Web`. There is **no `launchSettings.json`**, so set
   the URL explicitly:
   `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5000 dotnet run`
   Then open `http://localhost:5000`.

### Build / test / lint
- Build: `dotnet build src/NopCommerce.sln -c Release` (the only warnings are pre-existing
  `CS0618`/`CS0108` ones).
- Test: `dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj`. The 4 `CanCheckVatNumber` cases call the
  external EU VAT SOAP web service and **fail when egress is blocked** (offline) — this is expected,
  not a regression. The other ~1090 tests pass. There is no separate lint step; the CI
  (`.github/workflows/dotnet.yml`) only runs restore/build/test.

### Gotchas
- **PostgreSQL needs the `citext` and `pgcrypto` extensions.** nopCommerce only creates them
  automatically when *it* creates the database (installer "Create database if it doesn't exist").
  The provisioned `nopcommerce` DB already has them; if you ever recreate the DB manually, run
  `CREATE EXTENSION IF NOT EXISTS citext; CREATE EXTENSION IF NOT EXISTS pgcrypto;` in it or
  migrations fail with `type "citext" does not exist`.
- **The in-app installer restart kills `dotnet run`.** Finishing the install wizard (and its
  "Restart installation" button) calls `RestartApplication`, which stops the dev process. If the app
  exits right after installing, just start it again — the install is already persisted.
- **To re-trigger the installer**, empty the `ConnectionString` in
  `src/Presentation/Nop.Web/App_Data/appsettings.json` (and drop/recreate the DB for a clean run);
  the app redirects `/` → `/install` whenever the connection string is empty.
