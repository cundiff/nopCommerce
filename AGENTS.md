# AGENTS.md

Guidance for AI agents working in this repository.

## Cursor Cloud specific instructions

### Product overview

Single **nopCommerce v5** ASP.NET Core e-commerce app (`Nop.Web`). Storefront, admin (`/admin`), and installer (`/install`) all run in one process. See `.cursor/skills/start-local-nopcommerce/SKILL.md` for Docker-based startup; on Cloud VMs, **`dotnet run` is the reliable path** when pulling MCR base images fails.

### Toolchain (VM snapshot)

- **.NET SDK 10** — pinned in `global.json` (`10.0.100`); Ubuntu package `dotnet-sdk-10.0` satisfies this.
- **Docker** — installed for optional container workflows. The daemon uses **`fuse-overlayfs`** and **`iptables-legacy`** (see `/etc/docker/daemon.json`). Start manually if needed: `sudo dockerd > /tmp/dockerd.log 2>&1 &` then wait for `sudo docker info`.
- **Node.js / npm** — optional; only needed to rebuild frontend assets via Gulp in `src/Presentation/Nop.Web`. Prebuilt assets under `wwwroot/lib_npm` are already in the repo.

### Running the app (recommended on Cloud VM)

```bash
cd /workspace/src/Presentation/Nop.Web
ASPNETCORE_URLS=http://0.0.0.0:8080 dotnet run --configuration Release
```

Before first run after a clean build:

```bash
dotnet restore /workspace/src
dotnet build --configuration Release /workspace/src/NopCommerce.sln
```

- **Installer:** http://localhost:8080/install (fresh install redirects `/` → `/install`)
- **Admin (after install):** http://localhost:8080/admin

Use a tmux session for long-running dev servers (e.g. session name `nopcommerce-dev`).

### Docker alternative

When MCR registry access works:

```bash
docker build --platform linux/amd64 -t nopcommerce-local-amd64 /workspace
docker run --platform linux/amd64 --name nopcommerce-local -p 8080:80 nopcommerce-local-amd64
```

Full stack with SQL Server: `docker compose -f /workspace/docker-compose.yml up --build` (requires pulling `mcr.microsoft.com/mssql/server`).

### Build and test

Match CI (`.github/workflows/dotnet.yml`):

```bash
dotnet restore /workspace/src
dotnet build --no-restore --configuration Release /workspace/src
dotnet test --no-build --configuration Release /workspace/src
```

Tests use **SQLite in-memory**; no external DB required. Three VAT validation tests (`CanCheckVatNumber`) call the live EU VIES service and may fail offline — this is expected and unrelated to local setup.

There is **no repo lint script**; build warnings are pre-existing. No pre-commit hooks are enabled (only `.sample` files in `.git/hooks/`).

### Frontend assets (optional)

```bash
cd /workspace/src/Presentation/Nop.Web
npm install
npx gulp
```

### Gotchas

- `App_Data/appsettings.json` and `launchSettings.json` are gitignored; created at install/runtime.
- Docker builds in this environment may fail with `EOF` when pulling `mcr.microsoft.com/dotnet/*-alpine` — use `dotnet run` instead.
- `dotnet restore` after dependency changes is **not** picked up by a running `dotnet run` process; restart the server after restore/rebuild.
