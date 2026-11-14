#!/usr/bin/env python3
"""Install the pinned desktop toolchain locally; Python 3.10+, no pip packages."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import platform
import shutil
import stat
import sys
import tarfile
import tempfile
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[1]
TEMPLATES = {
    "linux_debug.x86_64",
    "linux_release.x86_64",
    "windows_debug_x86_64.exe",
    "windows_release_x86_64.exe",
    "windows_debug_x86_64_console.exe",
    "windows_release_x86_64_console.exe",
    "icudt_godot.dat",
}


def sha512(path: Path) -> str:
    digest = hashlib.sha512()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def download(artifact: dict, destination: Path) -> Path:
    """Even existing cached archives must pass SHA512 before extraction."""
    archive = destination / artifact["filename"]
    expected = artifact["sha512"]
    if archive.exists():
        if sha512(archive) != expected:
            raise ValueError(f"SHA512 mismatch in cached archive: {archive}; remove it and retry")
        print(f"Verified cached archive: {archive.name}", flush=True)
        return archive

    partial = archive.with_name(archive.name + ".part")
    print(f"Downloading {artifact['url']}", flush=True)
    try:
        request = urllib.request.Request(artifact["url"], headers={"User-Agent": "gsa-toolchain/1"})
        with urllib.request.urlopen(request, timeout=120) as response, partial.open("wb") as stream:
            shutil.copyfileobj(response, stream, length=1024 * 1024)
        if sha512(partial) != expected:
            raise ValueError(f"SHA512 mismatch in downloaded archive: {archive.name}")
        partial.replace(archive)
    finally:
        partial.unlink(missing_ok=True)
    print(f"Verified download: {archive.name}", flush=True)
    return archive


def safe_relative(name: str) -> Path:
    """Reject absolute paths, Windows drives, traversal, and NUL bytes on either OS."""
    normalized = name.replace("\\", "/")
    parsed = PurePosixPath(normalized)
    if not parsed.parts or parsed.is_absolute() or "\x00" in name or any(
        part == ".." or ":" in part for part in parsed.parts
    ):
        raise ValueError(f"Unsafe archive member: {name!r}")
    return Path(*parsed.parts)


def extract_zip(archive: Path, destination: Path, templates: bool = False) -> None:
    found = set()
    with zipfile.ZipFile(archive) as source:
        for item in source.infolist():
            relative = safe_relative(item.filename)
            mode = item.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise ValueError(f"Archive symlinks are not supported: {item.filename}")
            if templates:
                # The official package uses templates/<filename>; do not extract other platforms.
                if len(relative.parts) != 2 or relative.parts[0] != "templates":
                    continue
                if relative.name not in TEMPLATES and relative.name != "version.txt":
                    continue
                relative = Path(relative.name)
                found.add(relative.name)
            target = destination / relative
            if item.is_dir():
                target.mkdir(parents=True, exist_ok=True)
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                with source.open(item) as stream, target.open("wb") as output:
                    shutil.copyfileobj(stream, output)
                if mode & 0o777:
                    target.chmod(mode & 0o777)
                if templates and relative.name.startswith("linux_"):
                    target.chmod(0o755)
    if templates and not TEMPLATES.issubset(found):
        raise ValueError(f"Missing desktop templates: {sorted(TEMPLATES - found)}")


def extract_tar(archive: Path, destination: Path) -> None:
    # Copy regular files individually: no extractall, symlinks, device nodes, or hard links.
    with tarfile.open(archive, "r:gz") as source:
        for item in source:
            if item.name in (".", "./") and item.isdir():
                continue
            relative = safe_relative(item.name)
            target = destination / relative
            if item.isdir():
                target.mkdir(parents=True, exist_ok=True)
            elif item.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                stream = source.extractfile(item)
                if stream is None:
                    raise ValueError(f"Cannot read archive member: {item.name}")
                with stream, target.open("wb") as output:
                    shutil.copyfileobj(stream, output)
                target.chmod(item.mode & 0o777)
            else:
                raise ValueError(f"Unsupported archive member: {item.name}")


def install(artifact: dict, cache: Path, name: str, templates: bool = False) -> Path:
    archive = download(artifact, cache)
    destination = cache / name
    marker = destination / ".gsa-install.json"
    identity = {"archive": artifact["filename"], "sha512": artifact["sha512"]}
    if marker.is_file() and json.loads(marker.read_text(encoding="utf-8")) == identity:
        print(f"Already installed: {destination}", flush=True)
        return destination
    if destination.exists():
        raise ValueError(f"Unrecognized/incomplete installation: {destination}; move it aside and retry")
    with tempfile.TemporaryDirectory(prefix=f".{name}-", dir=cache) as temporary:
        staging = Path(temporary) / name
        staging.mkdir()
        if archive.name.endswith(".tar.gz"):
            extract_tar(archive, staging)
        else:
            extract_zip(archive, staging, templates)
        (staging / marker.name).write_text(json.dumps(identity, indent=2) + "\n", encoding="utf-8")
        staging.replace(destination)
    print(f"Installed: {destination}", flush=True)
    return destination


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, default=ROOT / "work/toolchain")
    parser.add_argument("--skip-templates", action="store_true", help="Install SDK/editor only; exports need templates")
    args = parser.parse_args()
    system = platform.system()
    if system not in ("Linux", "Windows") or platform.machine().lower() not in ("x86_64", "amd64"):
        parser.error("Only Linux x86-64 and Windows x86-64 are supported")
    host = "linux-x64" if system == "Linux" else "windows-x64"
    manifest = json.loads((ROOT / "toolchain/manifest.json").read_text(encoding="utf-8"))
    artifacts = manifest["artifacts"]
    cache = args.destination.resolve()
    cache.mkdir(parents=True, exist_ok=True)
    dotnet = install(artifacts[f"dotnet-{host}"], cache, "dotnet")
    godot = install(artifacts[f"godot-{host}"], cache, "godot")
    if not args.skip_templates:
        install(artifacts["godot-templates"], cache, "templates", templates=True)
    print(f"\nDOTNET_ROOT={dotnet}")
    print(f"Add to PATH: {dotnet}")
    print(f"Godot editor extracted under: {godot}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, tarfile.TarError, zipfile.BadZipFile) as error:
        print(f"Toolchain installation failed: {error}", file=sys.stderr)
        sys.exit(1)
