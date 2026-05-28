# AGENTS.md

## Cursor Cloud specific instructions

### Project overview

nopCommerce is a full-featured open-source eCommerce platform built on ASP.NET Core (.NET 10). It includes a customer-facing storefront and an admin back-office in a single monolithic web application with a plugin architecture (30+ plugins).

### Tech stack

- **Runtime:** .NET 10 (SDK pinned to `10.0.100` via `global.json` with `latestFeature` roll-forward)
- **Language:** C#
- **Frontend (admin):** jQuery, AdminLTE 3, Bootstrap 4, DataTables, Chart.js, Summernote
- **Frontend (store):** jQuery, Swiper, Magnific Popup
- **Frontend build:** Gulp (from `src/Presentation/Nop.Web/`)
- **Database:** PostgreSQL, MS SQL Server, or MySQL (configured at first run via install wizard)
- **Testing:** NUnit + Moq + SQLite (in-memory) — see `src/Tests/Nop.Tests/`
- **Solution file:** `src/NopCommerce.sln`

### Development commands

| Action | Command | Working directory |
|--------|---------|-------------------|
| Restore NuGet packages | `dotnet restore NopCommerce.sln` | `src/` |
| Build | `dotnet build NopCommerce.sln` | `src/` |
| Run tests | `dotnet test Tests/Nop.Tests/Nop.Tests.csproj` | `src/` |
| Run app (dev) | `dotnet run --project Presentation/Nop.Web/Nop.Web.csproj` | `src/` |
| Install frontend deps | `npm install` | `src/Presentation/Nop.Web/` |
| Build frontend assets | `npx gulp` | `src/Presentation/Nop.Web/` |

### Key caveats

1. **Network access requirement:** NuGet package restore requires HTTPS access to `api.nuget.org`. This domain must be in the Cloud Agent network allowlist. Without it, `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet run` will all fail.

2. **Frontend assets must be built before running the app.** Run `npm install && npx gulp` in `src/Presentation/Nop.Web/` after cloning or pulling changes that modify `package.json` or gulp tasks.

3. **Database:** The app supports PostgreSQL, MS SQL Server, and MySQL. For local development, PostgreSQL is simplest:
   ```
   sudo pg_ctlcluster 16 main start
   ```
   **Important:** The PostgreSQL `citext` extension must be enabled on the target database before running the install wizard:
   ```
   PGPASSWORD='nopCommerce_db_password' psql -h localhost -U postgres -d nopcommerce -c "CREATE EXTENSION IF NOT EXISTS citext;"
   ```

4. **First-run install wizard:** When no database is configured (check `App_Data/appsettings.json` → `ConnectionStrings.ConnectionString`), the app serves an installation page. After completing the wizard, the app must be restarted to pick up the new configuration. Data settings are stored in `App_Data/appsettings.json` (not the old `dataSettings.json`).

5. **Tests use SQLite in-memory** — no external database server is needed to run tests.

6. **The .NET SDK version** is pinned in `global.json`. The `dotnet-sdk-10.0` apt package from `packages.microsoft.com` satisfies this requirement.

7. **Docker:** A `Dockerfile` and `docker-compose.yml` are provided for containerized deployment but are not required for local development.

8. **Default admin credentials** (after install with sample data): `admin@yourStore.com` / `Admin123!`

9. **App URL:** By default runs on `http://localhost:5000`. Use `--urls "http://localhost:5000"` with `dotnet run` to set explicitly.
