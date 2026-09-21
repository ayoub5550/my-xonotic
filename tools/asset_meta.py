#!/usr/bin/env python3
"""Seed missing source-asset GUIDs, or check committed metadata without Unity.

Never overwrite an existing .meta. Generated/external assets remain Unity-owned
and ignored. This does not validate Unity importers or the contents of an asset.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import uuid

ROOT = Path(__file__).resolve().parents[1]
NAMESPACE = uuid.UUID("9f401867-7bcd-4dce-af10-b4d5676e3baa")


def source_assets():
    for path in sorted((ROOT / "Assets").rglob("*")):
        rel = path.relative_to(ROOT)
        if path.suffix == ".meta" or "Generated" in rel.parts or "StreamingAssets" in rel.parts:
            continue
        yield path


def metadata(path):
    rel = path.relative_to(ROOT).as_posix()
    guid = uuid.uuid5(NAMESPACE, rel).hex
    header = f"fileFormatVersion: 2\nguid: {guid}\n"
    common = "  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    if path.is_dir():
        return header + "folderAsset: yes\nDefaultImporter:\n" + common
    if path.suffix == ".cs":
        return header + ("MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
                         "  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n"
                         "  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    if path.suffix == ".asmdef":
        return header + "AssemblyDefinitionImporter:\n" + common
    if path.suffix == ".shader":
        return header + ("ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n"
                         "  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n"
                         "  assetBundleVariant:\n")
    return header + "DefaultImporter:\n" + common


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write-missing", action="store_true")
    args = parser.parse_args()
    seen = {}
    count = 0
    errors = []
    for asset in source_assets():
        meta = asset.with_name(asset.name + ".meta")
        if not meta.exists() and args.write_missing:
            meta.write_text(metadata(asset))
        if not meta.is_file():
            errors.append(f"Missing metadata: {asset.relative_to(ROOT)}")
            continue
        match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M)
        if not match:
            errors.append(f"Invalid GUID: {meta.relative_to(ROOT)}")
            continue
        guid = match.group(1)
        if guid in seen:
            errors.append(f"Duplicate GUID: {meta.relative_to(ROOT)}, {seen[guid]}")
        seen[guid] = str(meta.relative_to(ROOT))
        count += 1
    for error in errors:
        print(error)
    print(f"Source metadata: {count} checked, {len(errors)} errors. Not an Editor import test.")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
