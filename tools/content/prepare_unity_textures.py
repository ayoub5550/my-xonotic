#!/usr/bin/env python3
"""Decode selected official DDS art for Unity with an auditable conversion manifest.

Usage: uv run --with pillow python tools/content/prepare_unity_textures.py <extracted-data>
Original source files are never modified. No network access.
"""
import hashlib
import json
from pathlib import Path
import sys
from PIL import Image

root = Path(sys.argv[1]).resolve()
output = Path("ExternalContent/decoded")
entries = []
for name in ["models/weapons/laser"]:
    source = root / ("dds/"+name+".dds")
    destination = output / (name+".png")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with Image.open(source) as image:
        image.convert("RGBA").save(destination)
    entries.append({
        "source": "dds/"+name+".dds",
        "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
        "derived": str(destination),
        "derived_sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
        "operation": "Pillow DDS decode to lossless PNG; original upstream licence unchanged"
    })
output.mkdir(parents=True, exist_ok=True)
(output/"conversion-manifest.json").write_text(json.dumps(entries,indent=2)+"\n")
print(json.dumps(entries,indent=2))
