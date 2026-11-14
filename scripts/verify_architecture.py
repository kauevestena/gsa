#!/usr/bin/env python3
"""Check project dependency directions without loading the engine or the SDK."""
from __future__ import annotations

import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ALLOWED = {
    "Gsa.Core": set(),
    "Gsa.Missions": {"Gsa.Core"},
    "Gsa.Tools": {"Gsa.Core", "Gsa.Missions"},
    "Gsa.Engine": {"Gsa.Core", "Gsa.Missions"},
    "Gsa": {"Gsa.Engine"},
}


def audit() -> dict:
    projects = sorted([*(ROOT / "src").rglob("*.csproj"),
                       *(ROOT / "tests").rglob("*.csproj"), ROOT / "game/Gsa.csproj"])
    graph, diagnostics = {}, []
    for path in projects:
        key = path.relative_to(ROOT).as_posix()
        document = ET.parse(path).getroot()
        references = [((path.parent / item.attrib["Include"]).resolve())
                      for item in document.findall(".//ProjectReference")]
        graph[key] = [p.relative_to(ROOT).as_posix() for p in references]
        allowed = (set(ALLOWED) - {"Gsa", "Gsa.Engine"}) if path.stem.endswith(".Tests") else ALLOWED.get(path.stem)
        if allowed is None:
            diagnostics.append(f"Unknown project role: {key}")
            continue
        for reference in references:
            if not reference.is_file() or reference.stem not in allowed:
                diagnostics.append(f"Forbidden or missing reference: {key} -> {reference}")
        if path.stem not in {"Gsa", "Gsa.Engine"}:
            if "Godot" in document.attrib.get("Sdk", ""):
                diagnostics.append(f"Engine SDK outside adapter: {key}")
            for package in document.findall(".//PackageReference"):
                if "godot" in package.attrib.get("Include", "").lower():
                    diagnostics.append(f"Engine package outside adapter: {key}")
    for required in ALLOWED:
        if not any(p.stem == required for p in projects):
            diagnostics.append(f"Required module missing: {required}")
    return {"valid": not diagnostics, "projects": graph, "diagnostics": diagnostics}


if __name__ == "__main__":
    result = audit()
    print(json.dumps(result, ensure_ascii=False, indent=2))
    sys.exit(0 if result["valid"] else 2)
