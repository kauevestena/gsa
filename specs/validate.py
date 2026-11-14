#!/usr/bin/env python3
"""Validação documental read-only do pacote SDD, usando apenas Python stdlib."""
from __future__ import annotations

import hashlib
import json
import re
import sys
from collections import Counter
from pathlib import Path
from urllib.parse import unquote

ROOT = Path(__file__).resolve().parent
ID = re.compile(r"\b(?:REQ|AC|T)-F\d{2}-\d{2}\b")
DEFINITION = re.compile(r"^### ((?:REQ|AC|T)-F\d{2}-\d{2})(?:\s+—.*)?$", re.M)
LINK = re.compile(r"\[[^\]\n]+\]\(([^)\n]+)\)")
STAGES = {f"M{i}": i for i in range(10)} | {"E1": 10, "E2": 10}


def reject_nonfinite(value: str) -> None:
    raise ValueError(f"número não finito em JSON: {value}")


def validate() -> tuple[list[str], dict[str, int]]:
    errors: list[str] = []
    texts = {p: p.read_text(encoding="utf-8") for p in sorted(ROOT.rglob("*.md"))}
    definitions: dict[str, Path] = {}
    tasks: dict[str, dict] = {}

    def error(path: Path, message: str) -> None:
        errors.append(f"{path.relative_to(ROOT)}: {message}")

    for path, text in texts.items():
        if len(re.findall(r"^```", text, re.M)) % 2:
            error(path, "bloco de código não fechado")
        if re.search(r"\b(?:TODO|TBD|FIXME)\b|embedded content:", text):
            error(path, "placeholder não resolvido")
        for match in DEFINITION.finditer(text):
            key = match.group(1)
            if key in definitions:
                error(path, f"definição duplicada: {key}")
            definitions[key] = path
        for match in LINK.finditer(text):
            target = match.group(1).strip().strip("<>")
            if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", target):
                continue
            raw_path, _, fragment = target.partition("#")
            resolved = (path.parent / unquote(raw_path)).resolve() if raw_path else path
            if not resolved.exists():
                error(path, f"link local inexistente: {target}")
            elif fragment and resolved.suffix == ".md":
                headings = re.findall(r"^#+\s+(.+)$", resolved.read_text(encoding="utf-8"), re.M)
                anchors = {re.sub(r"[^\w\- ]", "", h.lower()).replace(" ", "-") for h in headings}
                if unquote(fragment) not in anchors:
                    error(path, f"âncora local inexistente: {target}")
        for block in re.findall(r"^```json\s*\n(.*?)^```", text, re.M | re.S):
            try:
                json.loads(block, parse_constant=reject_nonfinite)
            except ValueError as exc:
                error(path, f"JSON embutido inválido: {exc}")

    for path, text in texts.items():
        for key in set(ID.findall(text)):
            if key not in definitions:
                error(path, f"ID não definido: {key}")
        if path.name != "tasks.md":
            continue
        for match in re.finditer(r"^### (T-F\d{2}-\d{2}) — ([^\n]+)\n(.*?)(?=^### |\Z)", text, re.M | re.S):
            key, title, body = match.groups()
            fields = dict(re.findall(r"^\*\*([^*\n]+):\*\* ([^\n]+)", body, re.M))
            required = {"Marco", "Dependências", "Requisitos", "Cenários relacionados", "Entregável e limite", "Critério de aceite", "Evidência"}
            for missing in required - fields.keys():
                error(path, f"{key} sem campo {missing}")
            if not re.search(r"^- \[[ x]\] Aceita$", body, re.M):
                error(path, f"{key} sem estado revisável")
            stage = fields.get("Marco", "")
            if stage not in STAGES:
                error(path, f"{key} tem marco inválido {stage}")
            reqs = re.findall(r"REQ-F\d{2}-\d{2}", fields.get("Requisitos", ""))
            acs = re.findall(r"AC-F\d{2}-\d{2}", fields.get("Cenários relacionados", ""))
            if not reqs or not acs:
                error(path, f"{key} precisa requisitos e aceites")
            tasks[key] = dict(path=path, title=title, stage=stage, reqs=reqs, acs=acs,
                              deps=re.findall(r"T-F\d{2}-\d{2}", fields.get("Dependências", "")))

    covered_reqs = {r for t in tasks.values() for r in t["reqs"]}
    covered_acs = {a for t in tasks.values() for a in t["acs"]}
    for key, path in definitions.items():
        if key.startswith("REQ-") and key not in covered_reqs:
            error(path, f"requisito sem tarefa: {key}")
        if key.startswith("AC-") and key not in covered_acs:
            error(path, f"aceite sem tarefa: {key}")
        if key.startswith("T-") and key not in tasks:
            error(path, f"tarefa não reconhecida pelo formato: {key}")

    for key, task in tasks.items():
        for dep in task["deps"]:
            if dep not in tasks:
                error(task["path"], f"{key}: dependência inexistente {dep}")
            elif STAGES.get(tasks[dep]["stage"], 99) > STAGES.get(task["stage"], -1):
                error(task["path"], f"{key} depende de marco futuro: {dep}")

    visiting: set[str] = set()
    done: set[str] = set()

    def walk(key: str, chain: list[str]) -> None:
        if key in visiting:
            errors.append("ciclo de dependências: " + " → ".join(chain + [key]))
            return
        if key in done or key not in tasks:
            return
        visiting.add(key)
        for dep in tasks[key]["deps"]:
            walk(dep, chain + [key])
        visiting.remove(key)
        done.add(key)

    for key in tasks:
        walk(key, [])

    expected_reqs = {key for key in definitions if key.startswith("REQ-")}
    for filename in ("requisitos.md", "rastreabilidade.md"):
        path = ROOT / filename
        rows = re.findall(r"^\| (REQ-F\d{2}-\d{2}) \|", texts.get(path, ""), re.M)
        counts = Counter(rows)
        if set(rows) != expected_reqs or any(v != 1 for v in counts.values()):
            error(path, "catálogo/matriz não contém exatamente uma linha por requisito")
    trace_path = ROOT / "rastreabilidade.md"
    for line in texts.get(trace_path, "").splitlines():
        match = re.match(r"\| (REQ-F\d{2}-\d{2}) \|", line)
        if match:
            req = match.group(1)
            expected = {key for key, task in tasks.items() if req in task["reqs"]}
            found = set(re.findall(r"T-F\d{2}-\d{2}", line))
            if found != expected:
                error(trace_path, f"tarefas divergentes na linha {req}")
            expected_acs = {ac for key in expected for ac in tasks[key]["acs"]}
            if set(re.findall(r"AC-F\d{2}-\d{2}", line)) != expected_acs:
                error(trace_path, f"aceites divergentes na linha {req}")

    roadmap = ROOT / "roadmap.md"
    rows = re.findall(r"^\| (T-F\d{2}-\d{2}) \|", texts.get(roadmap, ""), re.M)
    if set(rows) != set(tasks) or any(v != 1 for v in Counter(rows).values()):
        error(roadmap, "roadmap não contém exatamente uma linha por tarefa")
    current_stage = None
    for line in texts.get(roadmap, "").splitlines():
        match = re.match(r"### (M\d|E[12]) —", line)
        if match:
            current_stage = match.group(1)
        match = re.match(r"\| (T-F\d{2}-\d{2}) \|", line)
        if match and match.group(1) in tasks and tasks[match.group(1)]["stage"] != current_stage:
            error(roadmap, f"tarefa em marco divergente: {match.group(1)}")

    folders = sorted(p for p in (ROOT / "features").iterdir() if p.is_dir())
    for folder in folders:
        for name in ("spec.md", "plan.md", "tasks.md"):
            if folder / name not in texts:
                error(folder, f"arquivo obrigatório ausente: {name}")
        plan_tasks = re.findall(r"^\| (T-F\d{2}-\d{2}) —", texts.get(folder / "plan.md", ""), re.M)
        own_tasks = {key for key, t in tasks.items() if t["path"].parent == folder}
        if set(plan_tasks) != own_tasks or len(plan_tasks) != len(own_tasks):
            error(folder / "plan.md", "incrementos não correspondem às tarefas")
        for line in texts.get(folder / "plan.md", "").splitlines():
            match = re.match(r"\| (T-F\d{2}-\d{2}) — [^|]+\| ([^|]+)\| ([^|]+)\|", line)
            if match and match.group(1) in tasks:
                key, stage, deps = match.groups()
                task = tasks[key]
                if stage.strip() != task["stage"] or set(re.findall(r"T-F\d{2}-\d{2}", deps)) != set(task["deps"]):
                    error(folder / "plan.md", f"marco/dependências divergentes para {key}")

    for path in ROOT.rglob("*.json"):
        try:
            json.loads(path.read_text(encoding="utf-8"), parse_constant=reject_nonfinite)
        except (ValueError, UnicodeDecodeError) as exc:
            error(path, f"JSON inválido: {exc}")
    baseline_path = ROOT / "baseline.json"
    try:
        baseline = json.loads(baseline_path.read_text(encoding="utf-8"), parse_constant=reject_nonfinite)
        source = (ROOT / baseline["source"]).resolve()
        if hashlib.sha256(source.read_bytes()).hexdigest() != baseline["source_sha256"]:
            error(baseline_path, "origem mudou desde a baseline; reconciliar requisitos antes de atualizar o hash")
    except (OSError, KeyError, ValueError) as exc:
        error(baseline_path, f"baseline inválida: {exc}")

    counts = dict(documentos=len(texts), funcionalidades=len(folders),
                  requisitos=len(expected_reqs), cenarios=len(covered_acs), tarefas=len(tasks),
                  marcos=len({t["stage"] for t in tasks.values()}))
    return errors, counts


if __name__ == "__main__":
    errors, counts = validate()
    if errors:
        for issue in errors:
            print("ERRO:", issue, file=sys.stderr)
        print(f"Falha: {len(errors)} problema(s).", file=sys.stderr)
        sys.exit(1)
    print("OK:", ", ".join(f"{n} {label}" for label, n in counts.items()))
    print("Links locais, IDs, cobertura, rastreabilidade, marcos, DAG, JSON e hash da origem válidos.")
