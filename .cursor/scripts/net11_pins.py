#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TFM = "net11.0"
SDK = "11.0.100-preview.7.26381.103"
PKG = "11.0.0-preview.7.26381.103"
RUNTIME = "11.0.0-preview.7.26381.103"
DOCKER_SDK = "mcr.microsoft.com/dotnet/sdk:11.0-preview-alpine"
DOCKER_ASPNET = "mcr.microsoft.com/dotnet/aspnet:11.0-preview-alpine"

SHARED_FRAMEWORK_PACKAGES = (
    "Microsoft.AspNetCore.Mvc.NewtonsoftJson",
    "Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation",
    "Microsoft.Extensions.Caching.StackExchangeRedis",
    "Microsoft.Extensions.Caching.SqlServer",
    "Microsoft.AspNetCore.Authentication.Facebook",
    "Microsoft.Data.Sqlite",
    "System.Configuration.ConfigurationManager",
)

LEAVE_ALONE = {
    "Autofac.Extensions.DependencyInjection": "10.0.0",
    "Npgsql": "10.0.2",
}

PINNED_PACKAGES = {
    "Microsoft.Data.SqlClient": "7.0.2",
}


def rel(path: Path) -> str:
    return str(path.relative_to(ROOT))


def text_files() -> list[Path]:
    skip_dirs = {".git", "bin", "obj", "node_modules", "wwwroot", "lib_npm"}
    out: list[Path] = []
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        if any(part in skip_dirs for part in path.parts):
            continue
        if path.suffix.lower() not in {
            ".csproj",
            ".props",
            ".proj",
            ".json",
            ".yml",
            ".yaml",
            ".txt",
            ".md",
            "",
        } and path.name != "Dockerfile":
            continue
        out.append(path)
    return out


def inventory() -> dict[str, list[str]]:
    hits: dict[str, list[str]] = {
        "tfm_net10": [],
        "docker_10": [],
        "sdk_10": [],
        "shared_framework_old": [],
    }
    for path in text_files():
        data = path.read_text(encoding="utf-8", errors="replace")
        if "<TargetFramework>net10.0</TargetFramework>" in data or '"tfm": "net10.0"' in data:
            hits["tfm_net10"].append(rel(path))
        if "sdk:10.0" in data or "aspnet:10.0" in data:
            hits["docker_10"].append(rel(path))
        if '"version": "10.0.100"' in data or "dotnet-version: 10.0" in data:
            hits["sdk_10"].append(rel(path))
        for name in SHARED_FRAMEWORK_PACKAGES:
            if f'Include="{name}"' in data and PKG not in data.split(name, 1)[1][:80]:
                hits["shared_framework_old"].append(f"{rel(path)}:{name}")
    return hits


def replace_package_version(data: bytes, name: str, version: str) -> bytes:
    return re.sub(
        rf'(<PackageReference Include="{re.escape(name)}" Version=")[^"]+(")'.encode(),
        rf"\g<1>{version}\2".encode(),
        data,
    )


def write_if_changed(path: Path, new: bytes, old: bytes, changed: list[str]) -> None:
    if new != old:
        path.write_bytes(new)
        changed.append(rel(path))


def apply() -> list[str]:
    changed: list[str] = []
    skip = {".git", "bin", "obj", "node_modules", "wwwroot", "lib_npm"}

    for path in ROOT.rglob("*"):
        if not path.is_file() or any(part in skip for part in path.parts):
            continue
        if path.suffix.lower() not in {".csproj", ".proj", ".txt", ".props"}:
            continue
        data = path.read_bytes()
        new = data.replace(b"<TargetFramework>net10.0</TargetFramework>", f"<TargetFramework>{TFM}</TargetFramework>".encode())
        if path.suffix.lower() == ".csproj":
            for name in SHARED_FRAMEWORK_PACKAGES:
                new = replace_package_version(new, name, PKG)
            for name, version in PINNED_PACKAGES.items():
                new = replace_package_version(new, name, version)
        write_if_changed(path, new, data, changed)

    runtime = ROOT / "src" / "Build" / "ClearPluginAssemblies.runtimeconfig.json"
    data = runtime.read_bytes()
    new = data.replace(b'"tfm": "net10.0"', f'"tfm": "{TFM}"'.encode()).replace(
        b'"version": "10.0.0"', f'"version": "{RUNTIME}"'.encode()
    )
    write_if_changed(runtime, new, data, changed)

    global_json = ROOT / "global.json"
    data = global_json.read_bytes()
    new = data.replace(b'"version": "10.0.100"', f'"version": "{SDK}"'.encode()).replace(
        b'"allowPrerelease": false', b'"allowPrerelease": true'
    )
    write_if_changed(global_json, new, data, changed)

    dockerfile = ROOT / "Dockerfile"
    data = dockerfile.read_bytes()
    new = data.replace(b"mcr.microsoft.com/dotnet/sdk:10.0-alpine", DOCKER_SDK.encode()).replace(
        b"mcr.microsoft.com/dotnet/aspnet:10.0-alpine", DOCKER_ASPNET.encode()
    )
    write_if_changed(dockerfile, new, data, changed)

    workflow = ROOT / ".github" / "workflows" / "dotnet.yml"
    data = workflow.read_bytes()
    new = data.replace(
        b"        dotnet-version: 10.0.x\n",
        f"        dotnet-version: {SDK}\n        include-prerelease: true\n".encode(),
    )
    write_if_changed(workflow, new, data, changed)

    skill = ROOT / ".cursor" / "skills" / "start-local-nopcommerce" / "SKILL.md"
    data = skill.read_bytes()
    new = data.replace(b"This repo targets .NET 10.", b"This repo targets .NET 11 Preview.")
    write_if_changed(skill, new, data, changed)

    readme = ROOT / "README.md"
    data = readme.read_bytes()
    new = data.replace(
        b"nopCommerce runs on .NET 9 with an MS SQL 2012 (or higher) backend database.",
        b"nopCommerce runs on .NET 11 Preview with an MS SQL 2012 (or higher) backend database.",
    )
    write_if_changed(readme, new, data, changed)

    return sorted(set(changed))


def check() -> list[str]:
    errors: list[str] = []
    hits = inventory()
    for key, paths in hits.items():
        if paths:
            errors.append(f"{key}: {', '.join(paths)}")

    props = (ROOT / "src" / "Directory.Build.props").read_text(encoding="utf-8")
    if f"<TargetFramework>{TFM}</TargetFramework>" not in props:
        errors.append("Directory.Build.props missing net11.0")

    sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))
    if sdk["sdk"]["version"] != SDK or not sdk["sdk"]["allowPrerelease"]:
        errors.append(f"global.json sdk pin is {sdk['sdk']}")

    dockerfile = (ROOT / "Dockerfile").read_text(encoding="utf-8")
    if DOCKER_SDK not in dockerfile or DOCKER_ASPNET not in dockerfile:
        errors.append("Dockerfile missing 11.0-preview-alpine tags")

    workflow = (ROOT / ".github" / "workflows" / "dotnet.yml").read_text(encoding="utf-8")
    if SDK not in workflow:
        errors.append("CI missing Preview 7 SDK pin")

    for path in ROOT.rglob("*.csproj"):
        if any(part in {".git", "bin", "obj"} for part in path.parts):
            continue
        data = path.read_text(encoding="utf-8")
        for name, version in LEAVE_ALONE.items():
            if f'Include="{name}"' in data and f'Version="{version}"' not in data:
                errors.append(f"{rel(path)} changed {name}")
        for name, version in PINNED_PACKAGES.items():
            if f'Include="{name}"' in data and f'Version="{version}"' not in data:
                errors.append(f"{rel(path)} {name} is not {version}")

    skill = (ROOT / ".cursor" / "skills" / "start-local-nopcommerce" / "SKILL.md").read_text(
        encoding="utf-8"
    )
    if "targets .NET 11" not in skill:
        errors.append("start-local-nopcommerce skill still names .NET 10")

    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    if "runs on .NET 11" not in readme:
        errors.append("README.md does not say the app runs on .NET 11")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("inventory", "apply", "check"))
    args = parser.parse_args()
    if args.command == "inventory":
        hits = inventory()
        print(json.dumps(hits, indent=2))
        print(
            "counts",
            {k: len(v) for k, v in hits.items()},
            file=sys.stderr,
        )
        return 0
    if args.command == "apply":
        changed = apply()
        print("\n".join(changed))
        return 0
    errors = check()
    if errors:
        print("FAIL")
        print("\n".join(errors))
        return 1
    print("PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
