#!/usr/bin/env python3
"""Compile C# against a local Unity installation without launching the Editor.

This is an API/type check, NOT an Editor import, player build or runtime test.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--editor-data", required=True, type=Path)
    p.add_argument("--ui-dll", type=Path,
                   help="Existing local UnityEngine.UI.dll built from Unity's UGUI package.")
    a = p.parse_args()
    data = a.editor_data.resolve()
    # Direct csc invocation bypasses Unity's asmdef graph; validate named edges
    # separately so a successful host compile cannot conceal a misspelled assembly.
    builtins = data / "Resources/PackageManager/BuiltInPackages"
    assembly_names = {p.stem for p in (data / "Managed/UnityEngine").glob("*.dll")}
    definition_paths = list((ROOT / "Assets").rglob("*.asmdef")) + list(builtins.rglob("*.asmdef"))
    for definition in definition_paths:
        assembly_names.add(json.loads(definition.read_text(encoding="utf-8-sig"))["name"])
    for definition in (ROOT / "Assets").rglob("*.asmdef"):
        for reference in json.loads(definition.read_text()).get("references", []):
            if reference not in assembly_names:
                p.error(f"Unresolved named asmdef reference {reference!r} in {definition.relative_to(ROOT)}")
    manifest = json.loads((ROOT / "Packages/manifest.json").read_text())
    for package, version in manifest["dependencies"].items():
        metadata = builtins / package / "package.json"
        if not metadata.exists():
            p.error(f"Baseline package {package} is not present in this local Editor.")
        if json.loads(metadata.read_text()).get("version") != version:
            p.error(f"Local builtin version mismatch for {package}.")
    print("Named asmdef references and pinned builtin package versions resolved.", flush=True)
    mono = data / "MonoBleedingEdge/bin/mono"
    compiler = data / "MonoBleedingEdge/lib/mono/4.5/csc.exe"
    if not mono.is_file() or not compiler.is_file():
        p.error("Expected Unity 2022 Mono and C# compiler in --editor-data.")
    api = data / "MonoBleedingEdge/lib/mono/4.8-api"
    refs = list(api.glob("*.dll")) + list((api / "Facades").glob("*.dll"))
    # Unity ships monolithic legacy assemblies AND modular assemblies/facades.
    # Never mix Managed/UnityEngine.dll with Managed/UnityEngine/*.dll.
    managed = data / "Managed/UnityEngine"
    refs += [r for r in managed.glob("*.dll") if not r.name.startswith("UnityEditor")]
    ui = a.ui_dll or ROOT / "Library/ScriptAssemblies/UnityEngine.UI.dll"
    if not ui.is_file():
        p.error("UGUI reference missing. Import project in Unity once, or pass --ui-dll.")
    refs += [ui.resolve()]
    output = ROOT / "Artifacts/host-compile"
    output.mkdir(parents=True, exist_ok=True)
    base = [str(mono), str(compiler), "-nologo", "-target:library", "-langversion:9",
            "-noconfig", "-nostdlib+", "-warn:4"]
    paths = ROOT / "Assets/MyXonotic"
    for name, folder, extra_refs, defines in [
        ("MyXonotic.Runtime", paths / "Runtime", [], []),
        ("MyXonotic.Editor", paths / "Editor",
         list(managed.glob("UnityEditor*.dll")) + [output / "MyXonotic.Runtime.dll"], ["UNITY_EDITOR"]),
    ]:
        sources = sorted(folder.rglob("*.cs"))
        args = base + [f"-out:{output / (name + '.dll')}"]
        args += [f"-r:{r}" for r in refs + extra_refs]
        args += [f"-define:{d}" for d in defines] + [str(s) for s in sources]
        print(f"Host API compile: {name} ({len(sources)} source files)", flush=True)
        result = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
        (output / (name + ".log")).write_text(result.stdout + result.stderr)
        print(result.stdout + result.stderr, end="", flush=True)
        if result.returncode:
            return result.returncode
    print("Host API compile passed. Unity Editor/import/runtime/build gates remain separate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
