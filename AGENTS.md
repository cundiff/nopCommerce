# AGENTS.md

Guidance for AI agents working in this repository.

## Cursor Cloud specific instructions

### Stack

- **.NET 10** ASP.NET Core monolith (`src/NopCommerce.sln`, entry point `src/Presentation/Nop.Web`).
- **Node.js** + **Gulp** for frontend assets under `src/Presentation/Nop.Web` (see `package.json`, `gulpfile.js`).
- **Docker** optional for containerized runs (`Dockerfile`, `docker-compose.yml` with MSSQL).

### VM packages

Cloud VMs need:

- `dotnet-sdk-10.0` (Ubuntu apt; satisfies `global.json` via `rollForward`)
- `docker-ce` with `fuse-overlayfs` storage driver and `iptables-legacy` (nested Docker)
- Node.js (preinstalled on Cursor Cloud VMs)

After installing Docker, ensure the agent user can access `/var/run/docker.sock` (e.g. `sudo chmod 666 /var/run/docker.sock` or add user to `docker` group).

### Update script vs manual setup

The VM **update script** only refreshes NuGet/npm dependencies. It does **not** start services.

Run `npx gulp default` in `src/Presentation/Nop.Web` when `package.json` / lockfile changes and `wwwroot/lib_npm` assets need refreshing (not required on every session if those files are unchanged).

### Build and test

From repo root (matches `.github/workflows/dotnet.yml`):

```bash
dotnet restore src
dotnet build --configuration Release src
dotnet test --no-build --configuration Release src
```

There is no separate ESLint/StyleCop CI step; `dotnet build` is the primary static check.

**Tests:** `Nop.Tests` uses in-memory SQLite automatically. Three `TaxServiceTests.CanCheckVatNumber` cases call the live EU VIES web service and may fail with `VatNumberStatus.Unknown` when outbound HTTPS to that API is blocked or flaky. That is an environment/network limitation, not a broken build.

### Run the web app (development)

**Fast path (SDK on host):**

```bash
cd src/Presentation/Nop.Web
export ASPNETCORE_URLS=http://localhost:8080
dotnet run --configuration Release
```

Before first run after clone, run `npm ci` and `npx gulp default` in that directory if static assets are missing.

**Docker path** (documented in `.cursor/skills/start-local-nopcommerce/SKILL.md`):

```bash
docker build -t nopcommerce-local-amd64 .
docker run --name nopcommerce-local -p 8080:80 nopcommerce-local-amd64
```

Fresh installs redirect `/` → `/install` (302) until a database is configured. The installer UI alone does not require a DB container.

**Full stack with SQL Server:** `docker compose up` from repo root (web on port 80, not 8080).

Use a **tmux** session for long-running `dotnet run` or `docker run` processes.

### Stop local Docker container

See `.cursor/skills/stop-local-nopcommerce/SKILL.md` (`docker stop` / `docker rm nopcommerce-local`).

### Apple Silicon

Build/run images as `linux/amd64` because of `IBM.Data.Db2.dll`; avoid default `docker-compose.yml` MSSQL on ARM unless debugging the DB stack.
