#!/usr/bin/env python3
"""Bundle all unmodified PK3s from the official release zip into an offline APK.

Usage: python3 tools/bundle_data.py /path/to/xonotic-0.8.6.zip
Run make_touch_pk3.py first. No assets are fetched at app runtime.
"""
import hashlib
from pathlib import Path
import shutil
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "android/app/src/main/assets"
NAMES = [
    "font-unifont-20230620.pk3", "font-xolonium-20230620.pk3",
    "xonotic-20230620-data.pk3", "xonotic-20230620-maps.pk3",
    "xonotic-20230620-music.pk3", "xonotic-20230620-nexcompat.pk3",
    "xonotic-20230620-xoncompat.pk3",
]
def main():
    dest = ASSETS / "game"
    dest.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(sys.argv[1]) as release:
        for name in NAMES:
            with release.open("Xonotic/data/" + name) as src, (dest / name).open("wb") as out:
                shutil.copyfileobj(src, out, 1 << 20)
    touch = "zz-xonotic-android-touch.pk3"
    shutil.copyfile(ASSETS / touch, dest / touch)
    rows = []
    for name in NAMES + [touch]:
        file = dest / name
        h = hashlib.sha256()
        with file.open("rb") as f:
            for b in iter(lambda: f.read(1 << 20), b""):
                h.update(b)
        rows.append(f"{name}\t{file.stat().st_size}\t{h.hexdigest()}\n")
    (ASSETS / "game-manifest.tsv").write_text("".join(rows))
    print("Bundled", len(rows), "verified resources;", sum(f.stat().st_size for f in dest.iterdir()), "bytes")

if __name__ == "__main__":
    main()
