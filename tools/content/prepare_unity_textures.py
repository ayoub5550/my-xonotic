#!/usr/bin/env python3
"""Decode requested official DDS art to PNG with per-file source provenance.

Usage: python tools/content/prepare_unity_textures.py <extracted-data>
       --texture models/weapons/laser --texture models/weapons/rl
No network access. Original bytes/licences are unchanged. This is preparation,
not evidence that all assets are supported or used by Unity.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import tempfile


def safe_texture_name(value):
    value = value.replace("\\", "/")
    p = PurePosixPath(value)
    if (p.is_absolute() or ".." in p.parts or ":" in value
            or not value or value.endswith("/") or "\x00" in value):
        raise ValueError("Texture must be a relative content path without traversal")
    if p.suffix.lower() == ".dds":
        p = p.with_suffix("")
    if not p.parts or p == PurePosixPath("."):
        raise ValueError("Empty texture name")
    return str(p)


def contained(root, relative):
    path = root / relative
    resolved = path.resolve()
    if not resolved.is_relative_to(root.resolve()):
        raise ValueError("Texture path escapes its content root")
    return path


def convert(root, output, names):
    from PIL import Image
    root, output = Path(root).resolve(), Path(output).resolve()
    names = list(dict.fromkeys(safe_texture_name(n) for n in names))
    sources = []
    for name in names:
        source = contained(root, "dds/" + name + ".dds")
        if not source.is_file():
            raise FileNotFoundError("DDS source missing: " + str(source))
        if source.stat().st_size > 64 * 1024 * 1024:
            raise ValueError("DDS source exceeds 64 MiB")
        destination = contained(output, name + ".png")
        sources.append((name, source, destination))
    output.mkdir(parents=True, exist_ok=True)
    manifest = output / "conversion-manifest.json"
    previous = json.loads(manifest.read_text()) if manifest.exists() else []
    if not isinstance(previous, list) or any(not isinstance(e, dict) for e in previous):
        raise ValueError("Existing conversion manifest must be a list of objects")
    entries = []
    # Decode every requested source before replacing any prior generated image.
    with tempfile.TemporaryDirectory(prefix="dds-decode-") as temporary:
        staging = Path(temporary)
        for i, (name, source, destination) in enumerate(sources):
            staged = staging / (str(i) + ".png")
            with Image.open(source) as image:
                if image.format != "DDS":
                    raise ValueError("Expected DDS bytes, not an extension-only match")
                if max(image.size) > 8192 or image.width * image.height > 32 * 1024 * 1024:
                    raise ValueError("DDS dimensions exceed preparation limit")
                image.convert("RGBA").save(staged)
            entries.append({
                "source": "dds/" + name + ".dds",
                "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
                "derived": str(destination),
                "derived_sha256": hashlib.sha256(staged.read_bytes()).hexdigest(),
                "operation": "Pillow DDS decode to lossless PNG; original upstream licence unchanged",
            })
        for i, (_, _, destination) in enumerate(sources):
            destination.parent.mkdir(parents=True, exist_ok=True)
            # Copy from staging to the destination filesystem before atomic replace.
            with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as handle:
                temp_path = Path(handle.name)
                handle.write((staging / (str(i) + ".png")).read_bytes())
            temp_path.replace(destination)
    updated = {e["source"]: e for e in previous if "source" in e}
    updated.update({e["source"]: e for e in entries})
    manifest.write_text(json.dumps(list(updated.values()), indent=2) + "\n")
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--output", type=Path, default=Path("ExternalContent/decoded"))
    parser.add_argument("--texture", action="append", dest="textures",
                        help="Content path without dds/ prefix; repeat for each required skin.")
    args = parser.parse_args()
    print(json.dumps(convert(args.root, args.output,
                             args.textures or ["models/weapons/laser"]), indent=2))


if __name__ == "__main__":
    main()
