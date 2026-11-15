#!/usr/bin/env python3
"""Build, test and export the game; record only executed checks."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exports", choices=("none", "host", "all"), default="all")
    parser.add_argument("--offline-smoke", action="store_true",
                        help="Linux: run exported binary in an isolated network namespace (requires unshare)")
    args = parser.parse_args()
    if platform.system() not in ("Linux", "Windows"):
        parser.error("Only Linux and Windows x86-64 are supported.")

    host = "windows" if sys.platform == "win32" else "linux"
    if args.offline_smoke and (host != "linux" or args.exports == "none"):
        parser.error("--offline-smoke requires Linux and a host export.")
    tools = ROOT / "work/toolchain"
    dotnet = tools / "dotnet" / ("dotnet.exe" if host == "windows" else "dotnet")
    editor_name = ("Godot_v*-stable_mono_win64_console.exe" if host == "windows"
                   else "Godot_v*-stable_mono_linux.x86_64")
    editors = list((tools / "godot").rglob(editor_name))
    if not dotnet.is_file() or len(editors) != 1:
        parser.error("Run python scripts/install_toolchain.py first (one pinned editor required).")
    godot = editors[0]

    logs = ROOT / "artifacts/logs" / host
    if logs.is_symlink():
        parser.error("Refusing a symlink log directory.")
    if logs.exists():
        shutil.rmtree(logs)
    logs.mkdir(parents=True, exist_ok=True)
    (ROOT / "work/test-artifacts").mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env.update({
        "DOTNET_ROOT": str(dotnet.parent),
        "DOTNET_CLI_HOME": str(ROOT / "work/dotnet-home"),
        "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
        "DOTNET_NOLOGO": "1",
        "RestoreLockedMode": "true",
        "NUGET_PACKAGES": str(ROOT / "work/nuget/packages"),
        "VSTEST_ARTIFACTS_DIRECTORY": str(ROOT / "work/test-artifacts"),
        "GODOT_SILENCE_ROOT_WARNING": "1",
        "XDG_DATA_HOME": str(ROOT / "work/xdg/data"),
        "XDG_CACHE_HOME": str(ROOT / "work/xdg/cache"),
        "XDG_CONFIG_HOME": str(ROOT / "work/xdg/config"),
        "PATH": str(dotnet.parent) + os.pathsep + env.get("PATH", ""),
    })
    report = {
        "tasks": ["T-F01-02", "T-F06-01", "T-F01-03", "T-F11-01", "T-F01-04",
                  "T-F02-01", "T-F02-02", "T-F04-01", "T-F05-01",
                  "T-F03-01", "T-F04-02", "T-F03-02",
                  "T-F09-01", "T-F15-01", "T-F17-01", "T-F19-01"],
        "base_commit": subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "host": {"system": platform.system(), "machine": platform.machine(),
                 "version": platform.version()},
        "checks": [],
        "exports": {},
        "native_smoke": {"linux": "not_run", "windows": "not_run"},
        "graphical_smoke": {"linux": "not_run", "windows": "not_run"},
        "network_isolation": "not_tested",
        "review": "pending",
    }
    report_path = logs / "report.json"

    def run(name: str, command: list[str | Path], *, marker: str | None = None, expected_exit: int = 0,
            runtime_env: dict | None = None) -> str:
        cmd = [str(p) for p in command]
        print(f"[{name}] {' '.join(cmd)}", flush=True)
        path = logs / f"{name}.log"
        check = {"name": name, "command": cmd, "log": str(path.relative_to(ROOT)),
                 "status": "failed"}
        report["checks"].append(check)
        try:
            result = subprocess.run(cmd, cwd=ROOT, env=runtime_env or env,
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                    text=True, errors="replace", timeout=600)
        except subprocess.TimeoutExpired as exc:
            output = exc.stdout or b""
            path.write_text(output.decode(errors="replace") if isinstance(output, bytes)
                            else output, encoding="utf-8")
            raise RuntimeError(f"{name}: timed out; see {path.relative_to(ROOT)}") from exc
        path.write_text(result.stdout, encoding="utf-8")
        check["exit_code"] = result.returncode
        if (result.returncode != expected_exit or re.search(r"^(?:SCRIPT )?ERROR:", result.stdout, re.M)
                or (marker and marker not in result.stdout)):
            raise RuntimeError(f"{name}: failed; see {path.relative_to(ROOT)}\n{result.stdout[-6000:]}")
        check["status"] = "passed"
        return result.stdout.strip()

    try:
        expected_sdk = json.loads((ROOT / "global.json").read_text())["sdk"]["version"]
        actual_sdk = run("dotnet-version", [dotnet, "--version"])
        if actual_sdk != expected_sdk:
            raise RuntimeError(f"SDK mismatch: expected {expected_sdk}, got {actual_sdk}")
        expected_godot = ET.parse(ROOT / "game/Gsa.csproj").getroot().attrib["Sdk"].split("/")[1]
        actual_godot = run("godot-version", [godot, "--version"])
        if not actual_godot.startswith(expected_godot + ".stable.mono."):
            raise RuntimeError(f"Godot mismatch: expected {expected_godot} .NET, got {actual_godot}")
        report["versions"] = {"godot": actual_godot, "dotnet_sdk": actual_sdk}
        lock_paths = lambda: sorted(p for area in ("src", "tests", "game")
                                    for p in (ROOT / area).rglob("packages.*.lock.json"))
        locks = {str(p.relative_to(ROOT)): sha256(p) for p in lock_paths()}
        if not locks:
            raise RuntimeError("No package lock files found")
        report["package_locks_sha256"] = locks
        run("specs", [sys.executable, "specs/validate.py"])
        run("installer-tests", [sys.executable, "-m", "unittest", "discover", "-s", "tests/toolchain", "-v"])
        run("architecture", [sys.executable, "scripts/verify_architecture.py"])
        run("domain-restore", [dotnet, "restore", "Gsa.Domain.slnf", "--locked-mode"])
        run("domain-tests", [dotnet, "test", "Gsa.Domain.slnf", "--no-restore",
                             "--logger", "trx", "--results-directory", logs / "test-results"])
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
        results = [ET.parse(path).find("t:ResultSummary/t:Counters", ns)
                   for path in sorted((logs / "test-results").glob("*.trx"))]
        if len(results) != 4 or any(r is None or int(r.attrib["executed"]) == 0 or int(r.attrib["failed"]) for r in results):
            raise RuntimeError("Expected four successful, nonempty test suites")
        report["domain_tests"] = {key: sum(int(r.attrib[key]) for r in results)
                                  for key in ("total", "executed", "passed", "failed", "notExecuted")}
        content_result = run("validate-content", [dotnet, ROOT / "src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll",
                                                  "validate-content", "content"])
        if json.loads(content_result).get("valid") is not True:
            raise RuntimeError("Content validator did not report valid content")
        with tempfile.TemporaryDirectory(prefix="invalid-content-", dir=ROOT / "work") as temporary:
            fixture = Path(temporary)
            shutil.copy2(ROOT / "content/catalog.json", fixture / "catalog.json")
            for folder in ("contracts", "profiles", "regions", "missions"):
                shutil.copytree(ROOT / "content" / folder, fixture / folder)
            contract = fixture / "contracts/facades.json"
            value = json.loads(contract.read_text(encoding="utf-8"))
            value["required_feature_ids"][0] = "missing_feature_for_smoke_test"
            contract.write_text(json.dumps(value), encoding="utf-8")
            output = run("invalid-content", [dotnet, ROOT / "src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll",
                                             "validate-content", fixture], expected_exit=2)
            invalid = json.loads(output)
            if invalid.get("valid") is not False or not any(d.get("field") == "$.required_feature_ids[0]"
                                                            for d in invalid.get("diagnostics", [])):
                raise RuntimeError("Invalid content must identify the missing feature field")
        sky_result = run("precompute-sky", [dotnet, ROOT / "src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll",
                                           "precompute-sky", "game/world/tutorial-block.json",
                                           "game/world/tutorial-sky.json"])
        if json.loads(sky_result).get("valid") is not True:
            raise RuntimeError("Sky precomputation did not report valid output")
        art_result = run("validate-art-kit", [dotnet, ROOT / "src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll",
                                              "validate-art-kit", "game/world/pilot-kit"])
        if json.loads(art_result).get("valid") is not True:
            raise RuntimeError("Pilot art kit validator did not report valid output")
        run("restore", [dotnet, "restore", "Gsa.sln", "--locked-mode"])
        run("build", [dotnet, "build", "Gsa.sln", "--no-restore"])
        run("import", [godot, "--headless", "--path", "game", "--editor", "--import"])
        bench_output = run("run-m1-benchmark", [dotnet, ROOT / "src/Gsa.Tools/bin/Debug/net10.0/gsa-tools.dll",
                                                "run-m1-benchmark", "game/world/tutorial-block.json",
                                                "game/world/pilot-kit", logs / "m1-benchmark-smoke.json",
                                                "--warmup=1", "--duration=5", "--runs=3"])
        bench_summary = json.loads(bench_output)
        if bench_summary.get("valid") is not True or bench_summary.get("runs_executed") != 6:
            raise RuntimeError("M1 benchmark CLI smoke check did not produce 6 valid runs")
        smoke = run("editor-smoke", [godot, "--headless", "--path", "game", "--", "--smoke-test"],
                    marker="GSA_BOOTSTRAP_SMOKE_OK")
        for marker in ("GSA_BRIDGE_SMOKE_OK", "GSA_LOCALIZATION_SMOKE_OK"):
            if marker not in smoke:
                raise RuntimeError(f"Missing integration marker: {marker}")
        world_smoke = run("world-smoke", [godot, "--headless", "--path", "game",
                                          "res://world/TutorialBlock.tscn", "--", "--smoke-test"],
                          marker="GSA_WORLD_SMOKE_OK")
        for marker in ("GSA_SKY_SMOKE_OK", "GSA_MOBILITY_SMOKE_OK", "GSA_CAMERA_SMOKE_OK",
                       "GSA_GIMBALINA_SMOKE_OK", "GSA_TRAFFIC_SMOKE_OK", "GSA_LIDAR_SMOKE_OK",
                       "GSA_PILOT_KIT_SMOKE_OK", "GSA_BENCHMARK_SMOKE_OK"):
            if marker not in world_smoke:
                raise RuntimeError(f"Missing world verification marker: {marker}")

        targets = ("linux", "windows") if args.exports == "all" else (
            (host,) if args.exports == "host" else ())
        for target in targets:
            out = ROOT / "artifacts/exports" / target
            # This dedicated generated directory must not retain files from older exports.
            if out.is_symlink():
                raise RuntimeError(f"Refusing symlink export directory: {out}")
            if out.exists():
                shutil.rmtree(out)
            out.mkdir(parents=True, exist_ok=True)
            binary = out / ("gsa.exe" if target == "windows" else "gsa.x86_64")
            preset = "Windows" if target == "windows" else "Linux"
            run(f"export-{target}", [godot, "--headless", "--path", "game",
                                     "--export-release", preset, binary])
            if not binary.is_file() or not binary.with_suffix(".pck").is_file():
                raise RuntimeError(f"{target}: executable or PCK missing")
            report["exports"][target] = {"status": "passed", "files": [
                {"path": str(p.relative_to(ROOT)), "sha256": sha256(p)}
                for p in sorted(out.rglob("*")) if p.is_file()
            ]}
            if target == host:
                # Verify the distributed runtime without depending on the installed SDK.
                clean = env.copy()
                for key in list(clean):
                    if key.startswith("DOTNET_ROOT"):
                        del clean[key]
                clean["PATH"] = os.environ.get("PATH", "")
                wrapper = out / "gsa.console.exe"
                executable = wrapper if target == "windows" and wrapper.exists() else binary
                prefix = ["unshare", "--user", "--map-root-user", "--net"] if args.offline_smoke else []
                output = run(f"export-smoke-{target}", prefix + [executable, "--headless", "--", "--smoke-test"],
                             marker="GSA_BOOTSTRAP_SMOKE_OK", runtime_env=clean)
                if any(marker not in output for marker in ("GSA_BRIDGE_SMOKE_OK", "GSA_LOCALIZATION_SMOKE_OK")):
                    raise RuntimeError(f"{target}: exported bridge/localization smoke did not complete")
                report["native_smoke"][target] = "passed_headless"
                world_output = run(f"export-world-smoke-{target}",
                                   prefix + [executable, "--headless", "--", "--bench-smoke"],
                                   marker="GSA_WORLD_SMOKE_OK", runtime_env=clean)
                for marker in ("GSA_SKY_SMOKE_OK", "GSA_MOBILITY_SMOKE_OK", "GSA_CAMERA_SMOKE_OK",
                               "GSA_GIMBALINA_SMOKE_OK", "GSA_TRAFFIC_SMOKE_OK", "GSA_LIDAR_SMOKE_OK",
                               "GSA_PILOT_KIT_SMOKE_OK", "GSA_BENCHMARK_SMOKE_OK"):
                    if marker not in world_output:
                        raise RuntimeError(f"{target}: missing exported world marker {marker}")
                if args.offline_smoke:
                    report["network_isolation"] = "linux_network_namespace"
        current_locks = {str(p.relative_to(ROOT)): sha256(p) for p in lock_paths()}
        if locks != current_locks:
            raise RuntimeError("Package locks changed during verification")
        report["package_locks_unchanged"] = True
        return 0
    except (OSError, RuntimeError) as exc:
        report["error"] = str(exc)
        print(str(exc), file=sys.stderr)
        return 1
    finally:
        report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                               encoding="utf-8")
        print(f"Report: {report_path.relative_to(ROOT)}", flush=True)


if __name__ == "__main__":
    sys.exit(main())
