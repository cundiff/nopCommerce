# AGENTS.md

Guidance for AI agents working in this repository.

## Cursor Cloud specific instructions

### Stack

- **.NET 10** ASP.NET Core monolith (`src/NopCommerce.sln`, entry point `src/Presentation/Nop.Web`).
- **Node.js** + **Gulp** for frontend assets under `src/Presentation/Nop.Web`.
- **PostgreSQL** for local cloud-agent development (MSSQL/MySQL also supported by the installer).

### Environment scripts

- **Install** (`.cursor/install.sh`, wired via `.cursor/environment.json`): installs .NET SDK 10 if missing, starts PostgreSQL, creates the `nopcommerce` role/DB with `citext`/`pgcrypto`, runs `dotnet restore`, then `npm ci` + `npx gulp default`.
- **Start**: brings PostgreSQL up (`sudo service postgresql start`). Does not start the web app — agents start that on demand.

### Run the web app

```bash
cd src/Presentation/Nop.Web
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5000 \
  dotnet run -c Debug --project Nop.Web.csproj
```

- Storefront: `http://localhost:5000/`
- Admin: `http://localhost:5000/Admin`
- Fresh clones redirect `/` → `/install` until a DB is configured. Connection settings land in `App_Data/appsettings.json` (gitignored).

### PostgreSQL defaults (from install script)

| Setting | Value |
|---------|-------|
| Host | `localhost` |
| Database | `nopcommerce` |
| User | `nopcommerce` |
| Password | `nopCommerce_db_password` |

On PostgreSQL 15+, the app role must **own the `public` schema** or FluentMigrator fails with `permission denied for schema public`. The install script sets this, plus `citext` and `pgcrypto`. Leave "create database" unchecked in the wizard when the DB already exists.

After the installer restarts the app domain, `dotnet run` exits — relaunch the run command; the saved connection string keeps the store installed.

### Build and test

```bash
dotnet restore src/NopCommerce.sln
dotnet build src/NopCommerce.sln -c Debug
dotnet test src/Tests/Nop.Tests/Nop.Tests.csproj
```

Tests use in-memory SQLite. The three `CanCheckVatNumber` cases call the live EU VIES / UK HMRC services and may fail under restricted egress — treat that as an environment limit, not a regression.

### Docker

`Dockerfile` / `docker-compose.yml` and `.cursor/skills/start-local-nopcommerce` cover containerized runs. Prefer the SDK + PostgreSQL path above when Docker is unavailable on the VM.
