# Scope: Migrate nopCommerce to latest .NET (.NET 11)

**Status:** Scoped only — no TFM / SDK / package bumps in this PR  
**Branch:** `cursor/dotnet-11-migration-scope-07b1`  
**Baseline:** `develop` @ .NET 10 (`net10.0`, SDK `10.0.100`)  
**Target:** .NET 11 (`net11.0`) — latest available as of 2026-08-04 is **11.0.0-preview.6** (GA expected **2026-11-10**, STS)

## Executive summary

`develop` already completed the #7859 move to **.NET 10 LTS**. “Latest .NET” now means **.NET 11**, which is still in preview. This document scopes the follow-on migration: what to change, what not to touch, risks known from the .NET 10 upgrade, and a phased execution plan.

**Recommendation:** keep production on .NET 10 until .NET 11 GA (or RC), and run the TFM bump on a long-lived preview branch. Ship a small **Phase 0** cleanup on `develop` now (docs + stranded `9.0.9` Microsoft packages) without waiting for 11.

## Current state (inventory)

| Area | Current value | Notes |
| --- | --- | --- |
| TFM (libraries / web / tests) | `net10.0` via `src/Directory.Build.props` | Plugins still set TFM per `.csproj` |
| SDK pin | `global.json` → `10.0.100`, `rollForward: latestFeature`, `allowPrerelease: false` | Must allow prerelease for preview SDKs |
| CI | `.github/workflows/dotnet.yml` → `dotnet-version: 10.0.x`, `windows-latest` | |
| Docker | `mcr.microsoft.com/dotnet/sdk:10.0-alpine` / `aspnet:10.0-alpine` | Alpine + `libgdiplus` / ICU extras |
| Plugins | 30 plugin projects, all `net10.0` | `plugin.json` `SupportedVersions` is nopCommerce version (`5.00`), not TFM |
| README | Still says “runs on .NET 9” | Stale after #7859 |
| Stranded packages | `Nop.Core` still references four `Microsoft.*` packages at **9.0.9** | Incomplete .NET 10 package alignment |

### Projects that must change TFM / tooling

Mechanical surface area mirrors #7859 / .NET 9 upgrades:

1. **Shared / tooling (6):** `global.json`, `src/Directory.Build.props`, `Dockerfile`, `.github/workflows/dotnet.yml`, `src/Build/ClearPluginAssemblies.*`, `ClearPluginAssemblies` rebuild
2. **Core projects (6):** `Nop.Core`, `Nop.Data`, `Nop.Services`, `Nop.Web.Framework`, `Nop.Web`, `Nop.Tests` (TFM inherited from `Directory.Build.props` where already centralized)
3. **Plugins (30 `.csproj` + matching `Notes.txt` where present):** each still hard-codes `<TargetFramework>net10.0</TargetFramework>`
4. **Docs / agent skills:** `README.md`, `.cursor/skills/start-local-nopcommerce` (currently documents .NET 10)

Approx. **~70+ files** for a mechanical TFM pass, plus follow-up compile/runtime fixes (historically non-trivial — see #7859).

### Microsoft / ASP.NET packages that must move with the TFM

| Package | Today | Target band |
| --- | --- | --- |
| `Microsoft.AspNetCore.Mvc.NewtonsoftJson` | 9.0.9 | 11.x (or 10.x in Phase 0) |
| `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` | 9.0.9 | 11.x (or 10.x in Phase 0) |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | 9.0.9 | 11.x (or 10.x in Phase 0) |
| `Microsoft.Extensions.Caching.SqlServer` | 9.0.9 | 11.x (or 10.x in Phase 0) |
| `Microsoft.AspNetCore.Authentication.Facebook` | 10.0.1 | 11.x |
| `Microsoft.Data.Sqlite` | 10.0.5 | latest compatible 11.x |
| `System.Configuration.ConfigurationManager` | 10.0.1 | latest compatible 11.x |

## Goals

- Run the full solution (`NopCommerce.sln`) on `net11.0` with SDK 11.
- Keep Docker install path green (`/` → install redirect, `/install` 200).
- Keep CI green: restore → build Release → test on Windows.
- Preserve SQL Server / PostgreSQL / MySQL data-provider support.
- Preserve plugin build + `ClearPluginAssemblies` cleanup behavior.
- Document breaking-change triage and known holdouts (e.g. SqlClient).

## Non-goals (out of scope for this migration)

- Feature work unrelated to the TFM bump (storefront/API features, admin UX, tax, payments).
- Marketplace / third-party plugins not in this repo.
- Centralized package management (`Directory.Packages.props`) — optional follow-up, not required to ship 11.
- Database schema upgrades / `upgradescripts` — only if a dependency forces a migration (unexpected).
- Moving off Alpine runtime images or removing `libgdiplus` / SkiaSharp native deps.
- Adopting Blazor-only .NET 11 template changes (nopCommerce is MVC + Razor, not Blazor Web App).
- Raising minimum SQL Server beyond what #7859 already constrained (`Microsoft.Data.SqlClient` pinned to **6.1.1** for SQL Server 2016).

## In-scope workstreams

### Phase 0 — Finish .NET 10 alignment (can land on `develop` independently)

Low risk; unblocks honest “we’re on 10” messaging and reduces dual-version package skew before 11.

- [ ] Bump stranded `Microsoft.AspNetCore.*` / `Microsoft.Extensions.*` in `Nop.Core` from `9.0.9` → current `10.0.x`
- [ ] Fix `README.md` (.NET 9 → .NET 10)
- [ ] Confirm CI / Docker still green

### Phase 1 — Tooling & TFM (preview branch)

- [ ] `global.json`: SDK 11 preview, set `allowPrerelease: true` while on preview
- [ ] `Directory.Build.props`: `net11.0`
- [ ] All 30 plugin `.csproj` TFMs → `net11.0`
- [ ] `ClearPluginAssemblies` project + `runtimeconfig` / rebuilt tool DLL
- [ ] `Dockerfile`: `sdk:11.0-*-alpine` / `aspnet:11.0-*-alpine` (tag once preview images exist for the chosen channel)
- [ ] CI: `dotnet-version: 11.0.x` (include prerelease if setup-dotnet requires it)
- [ ] Update Cursor local Docker skill text to .NET 11 when implementation starts

### Phase 2 — Package upgrades

- [ ] Align all `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*`, `Microsoft.Data.*`, `System.*` packages to 11.x-compatible versions
- [ ] Re-validate third-party stack on 11:
  - Data: `linq2db`, `FluentMigrator*`, `Npgsql`, `MySqlConnector`, **`Microsoft.Data.SqlClient` (hold 6.1.1 unless SQL Server 2016 support is explicitly dropped)**
  - Imaging / PDF: `SkiaSharp*`, `Svg.Skia`, `PdfRpt.Core`, `HarfBuzzSharp*`
  - Other: `AutoMapper`, `Autofac.Extensions.DependencyInjection`, `MailKit`, `ClosedXML`, `WebMarkupMin.*`, `LigerShark.WebOptimizer.Core`, plugin-specific SDKs (Avalara, Brevo, Azure Blob, Facebook auth, etc.)
- [ ] Resolve restore warnings / binding issues; keep `NU1901–NU1904` policy unless intentionally tightened

### Phase 3 — Compile & behavioral fixes

Expect code churn similar to #7859 (async enumerable helpers, localization import, data providers, WebHelper, install data, shipping/cart services). Treat these as first-class work, not “drive-by”:

- [ ] Fix analyzer / obsolete API breakages from [.NET 11 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11)
- [ ] Triage ASP.NET Core 10→11 notes ([migration guide](https://learn.microsoft.com/en-us/aspnet/core/migration/100-to-110)): routing `%2F` absolute-form behavior, OpenAPI 3.2 defaults (only if Web API surfaces are affected), antiforgery auto-injection (likely N/A for current pipeline)
- [ ] Re-run full `dotnet test` (`Nop.Tests`) and fix regressions
- [ ] Smoke install + critical storefront/admin paths in Docker (`linux/amd64`)

### Phase 4 — Hardening & merge readiness

- [ ] Flip `allowPrerelease` back to `false` once GA SDK is pinned
- [ ] Pin exact GA SDK in `global.json`
- [ ] Update README / contributor docs to .NET 11
- [ ] Capture SqlClient / SQL Server matrix decision in release notes
- [ ] Merge after GA (or explicit product decision to ship on RC)

## Risks & constraints

| Risk | Why it matters | Mitigation |
| --- | --- | --- |
| .NET 11 still preview | API / container tags / SDK can still move before Nov 2026 GA | Keep work on a preview branch; do not merge to `develop` until RC/GA decision |
| `Microsoft.Data.SqlClient` vs SQL Server 2016 | #7859 explicitly reverted to 6.1.1 because newer clients broke SQL 2016 | Default: keep pin; document if product drops SQL 2016 |
| Alpine + native deps | Dockerfile pulls `libgdiplus`, ICU, `gcompat` from Alpine edge | Re-test image build early; watch SkiaSharp native asset compatibility |
| Apple Silicon local Docker | Published app includes `IBM.Data.Db2.dll`; skill already forces `linux/amd64` | Keep amd64 platform requirement in local skills |
| Large plugin surface | 30 plugins × TFM + package graph | Mechanical script/PR for TFM; validate plugin load after publish |
| Incomplete .NET 10 package bump | Mixing 9.0.9 ASP.NET packages on `net10.0` | Phase 0 first |
| STS vs LTS | .NET 11 is STS (support through ~2028); .NET 10 is LTS through 2028-11-14 | Product call: adopt 11 for “latest” vs stay on 10 for longevity |

## Suggested PR slicing (when implementing)

1. **PR A (Phase 0):** README + `Nop.Core` 9.0.9 → 10.0.x package alignment  
2. **PR B (Phase 1):** TFM + SDK + Docker + CI only (preview)  
3. **PR C (Phase 2–3):** Package bumps + compile/test fixes  
4. **PR D (Phase 4):** GA pin + docs

Do not combine feature work into these PRs.

## Acceptance criteria (implementation PRs)

- [ ] `dotnet build src/NopCommerce.sln -c Release` succeeds on SDK 11
- [ ] `dotnet test` succeeds with no new flaky baselines
- [ ] Docker image builds for `linux/amd64` and serves `/install`
- [ ] CI workflow updated and green on the PR
- [ ] No unintentional SqlClient bump past the SQL Server 2016-supported pin without an explicit decision
- [ ] README and local Docker skill match the shipped TFM

## Decision log (open)

1. **Ship preview to `develop` before GA?** Default: **no** — preview branch only.  
2. **Drop SQL Server 2016 support to unblock newer SqlClient?** Default: **no** — keep 6.1.1.  
3. **Adopt .NET 11 STS or remain on .NET 10 LTS until next LTS?** Product decision required; this scope assumes “latest = 11” if approved.

## References

- Prior upgrade: #7859 (commits starting at `114576262b`, SqlClient pin `12637195f8`)
- ASP.NET Core 10 → 11 migration: https://learn.microsoft.com/en-us/aspnet/core/migration/100-to-110
- .NET 11 breaking changes: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11
- .NET 11 Preview 6 announcement / downloads (2026-07-14)
- Support policy: .NET 10 LTS through 2028-11-14; .NET 11 STS GA 2026-11-10
