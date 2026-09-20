#!/usr/bin/env python3
"""Verify the committed upstream resource subset offline, without executing it.

Hashes verify fixity against the recorded intake, not legal clearance or gameplay.
This tool never rewrites the index to make a changed file pass.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import stat
import sys

DEFAULT_ROOT = Path(__file__).resolve().parents[2] / "ThirdParty/Xonotic"
INDEX_NAME = "resource-index.json"
MAX_INDEX_BYTES = 4 * 1024 * 1024
MAX_RESOURCE_BYTES = 100 * 1024 * 1024
MAX_FILES = 10000
# Project-authored records, not upstream assets. Everything else must be indexed.
METADATA = {
    INDEX_NAME,
    "README.md",
    "maps-provenance.json",
    "data/resource-notes.md",
    "data/resources-provenance.json",
    "art-source/textures/MANIFEST.md",
}


def safe_relative(path):
    if not isinstance(path, str) or not path or "\\" in path or ":" in path or "\0" in path:
        return False
    return all(part not in ("", ".", "..") for part in path.split("/"))


def plain_file(root, name):
    """Reject symlinks in every component, including broken final links."""
    current = root
    for part in name.split("/"):
        current /= part
        mode = current.lstat().st_mode
        if stat.S_ISLNK(mode):
            raise ValueError(f"symlink: {name}")
    if not stat.S_ISREG(current.lstat().st_mode):
        raise ValueError(f"not a regular file: {name}")
    return current


def verify(root):
    root = Path(root)
    errors = []
    if root.is_symlink() or not root.is_dir():
        return {"passed": False, "files_checked": 0, "bytes_checked": 0,
                "errors": ["Resource root must be a real directory, not a symlink."]}
    root = root.resolve()
    try:
        index_file = plain_file(root, INDEX_NAME)
        if index_file.stat().st_size > MAX_INDEX_BYTES:
            raise ValueError("Index exceeds size limit")
        index = json.loads(index_file.read_text(encoding="utf-8"))
        entries = index.get("files")
        if index.get("schema_version") != 1 or not isinstance(entries, list):
            raise ValueError("Unsupported index schema")
        if not 1 <= len(entries) <= MAX_FILES:
            raise ValueError("Invalid resource count")
    except (OSError, ValueError, AttributeError) as exc:
        return {"passed": False, "files_checked": 0, "bytes_checked": 0,
                "errors": [f"Cannot read resource index: {exc}"]}

    names, canonical = set(), set()
    checked, total_bytes = 0, 0
    for entry in entries:
        if not isinstance(entry, dict):
            errors.append("Index entry is not an object")
            continue
        name = entry.get("path")
        if not safe_relative(name) or name in METADATA:
            errors.append(f"Unsafe or reserved resource path: {name!r}")
            continue
        alias = name.casefold()
        if name in names or alias in canonical:
            errors.append(f"Duplicate/case-alias resource: {name}")
            continue
        names.add(name)
        canonical.add(alias)
        size, expected = entry.get("bytes"), entry.get("sha256")
        if type(size) is not int or not 0 <= size <= MAX_RESOURCE_BYTES:
            errors.append(f"Invalid byte count: {name}")
            continue
        if not isinstance(expected, str) or not re.fullmatch(r"[0-9a-f]{64}", expected):
            errors.append(f"Invalid SHA256: {name}")
            continue
        source = entry.get("source_url")
        if not isinstance(source, str) or not source.startswith("https://"):
            errors.append(f"Missing HTTPS provenance: {name}")
            continue
        try:
            path = plain_file(root, name)
            if path.stat().st_size != size:
                raise ValueError("byte count changed")
            digest = hashlib.sha256()
            actual_size = 0
            with path.open("rb") as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    actual_size += len(block)
                    if actual_size > size:
                        raise ValueError("file grew while reading")
                    digest.update(block)
            if actual_size != size or digest.hexdigest() != expected:
                raise ValueError("SHA256/size mismatch")
            checked += 1
            total_bytes += size
        except (OSError, ValueError) as exc:
            errors.append(f"{name}: {exc}")

    # Detect forgotten files and links, rather than validating only a chosen subset.
    for path in root.rglob("*"):
        name = path.relative_to(root).as_posix()
        if path.is_symlink():
            errors.append(f"Unexpected symlink in resource tree: {name}")
        elif path.is_file() and name not in names and name not in METADATA:
            errors.append(f"Unindexed resource file: {name}")
    return {
        "passed": not errors,
        "files_checked": checked,
        "bytes_checked": total_bytes,
        "errors": errors,
        "scope": "Offline byte fixity and manifest coverage only; not licence, Unity or gameplay validation.",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=DEFAULT_ROOT)
    args = parser.parse_args()
    result = verify(args.root)
    print(json.dumps(result, indent=2))
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
