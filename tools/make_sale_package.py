#!/usr/bin/env python3
"""Build the marketplace ZIP(s) for the Plasma Verge kit.

    python3 tools/make_sale_package.py                 # code-only kit  -> PlasmaVerge-UnitySource-<ver>.zip
    python3 tools/make_sale_package.py --with-content  # + ContentPack  -> PlasmaVerge-ContentPack-0.8.6.zip
    python3 tools/make_sale_package.py --out DIR

Code kit  = MIT-licensed project only (no Xonotic data): Assets/, Packages/, ProjectSettings/,
            tools/, tests/, docs/, buyer guide, licences. Buyers fetch the data with
            tools/setup_content.py.
ContentPack = the 7 official Xonotic 0.8.6 .pk3 packs (GPL) + licence texts, for stores that
            allow bundling GPL data (itch.io, Gumroad, Lemon Squeezy). Requires that
            tools/setup_content.py has run once (it leaves the pk3s in ExternalContent/staging_pk3).
Excluded everywhere: .git, ThirdParty/ (multi-GB extracted upstream tree), generated assets, builds,
internal agent handoff notes, credentials.
"""
from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOP = "PlasmaVerge"

EXCLUDE_DIRS = {".git", ".worktrees", "Library", "Temp", "Logs", "Builds", "obj", "Obj", "UserSettings",
                "__pycache__", "ExternalContent", "Artifacts", "ThirdParty"}
EXCLUDE_PATHS = {
    "AGENTS.md", "CHANGELOG.md", ".gitignore",
    "Assets/MyXonotic/Generated", "Assets/MyXonotic/Generated.meta",
    "Assets/MyXonotic/Resources/Weapons", "Assets/MyXonotic/Resources/Weapons.meta",
    "Assets/StreamingAssets/Xonotic", "Assets/StreamingAssets/Xonotic.meta",
    "tests/fixtures/generated",
    "tools/content/publish_resources.py",   # only meaningful with the ThirdParty tree
    "tools/ftl_robo.sh",                     # owner's Firebase Test Lab wrapper
    "docs/releases", "docs/testlab", "docs/ROADMAP.md", "docs/FULL-GAME-GATES.md",  # internal planning (Arabic)
}
EXCLUDE_SUFFIXES = (".csproj", ".sln", ".apk", ".aab", ".keystore", ".jks", ".pyc", ".ulf", ".alf",
                    ".pfx", ".pem", ".env")
# Internal per-release engineering notes / build receipts are not part of the product.
EXCLUDE_DOC_PREFIXES = ("docs/UNITY-DEV", "docs/unity-", "docs/HANDOFF", "docs/UNITY-RETURN",
                        "docs/UNITY-CONTINUATION", "docs/DEVICE-TESTING", "docs/RELEASING")

PK3S = [
    "font-unifont-20230620.pk3", "font-xolonium-20230620.pk3", "xonotic-20230620-data.pk3",
    "xonotic-20230620-maps.pk3", "xonotic-20230620-music.pk3", "xonotic-20230620-nexcompat.pk3",
    "xonotic-20230620-xoncompat.pk3",
]


def wanted(rel: Path) -> bool:
    if any(part in EXCLUDE_DIRS for part in rel.parts):
        return False
    s = rel.as_posix()
    if s in EXCLUDE_PATHS or any(s.startswith(p + "/") for p in EXCLUDE_PATHS):
        return False
    if s.startswith(EXCLUDE_DOC_PREFIXES):
        return False
    if s.endswith(EXCLUDE_SUFFIXES) or rel.name.startswith(".env"):
        return False
    return True


def build_code_kit(out_dir: Path, version: str) -> Path:
    out = out_dir / f"PlasmaVerge-UnitySource-{version}.zip"
    n = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(ROOT.rglob("*")):
            if not p.is_file() or p.is_symlink():
                continue
            rel = p.relative_to(ROOT)
            if wanted(rel):
                z.write(p, Path(TOP) / rel)
                n += 1
    print(f"{out}  files={n}  size={out.stat().st_size/1e6:.1f} MB")
    return out


def build_content_pack(out_dir: Path) -> Path:
    staging = ROOT / "ExternalContent/staging_pk3/Xonotic/data"
    missing = [n for n in PK3S if not (staging / n).exists()]
    if missing:
        sys.exit(f"run tools/setup_content.py first; missing {missing}")
    out = out_dir / "PlasmaVerge-ContentPack-Xonotic-0.8.6.zip"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_STORED) as z:   # pk3s are already compressed
        for n in PK3S:
            z.write(staging / n, f"ContentPack/pk3/{n}")
        notices = ROOT / "ThirdParty/Xonotic-0.8.6/notices"
        if notices.is_dir():
            for f in sorted(notices.rglob("*")):
                if f.is_file():
                    z.write(f, Path("ContentPack/LICENSES") / f.relative_to(notices))
        z.writestr("ContentPack/README.txt", CONTENT_README)
    print(f"{out}  size={out.stat().st_size/1e9:.2f} GB")
    return out


CONTENT_README = """Plasma Verge — Content Pack (Xonotic 0.8.6 game data)

These are the unmodified official Xonotic 0.8.6 data packs (.pk3), copyright
Team Xonotic and contributors, licensed under the GNU GPL (v3 or later for the
game data, v2 or later for some textures; see LICENSES/). They are NOT part of the
MIT-licensed Plasma Verge source code and are provided here only for convenience —
the same files are available for free at https://dl.xonotic.org/xonotic-0.8.6.zip.

Use: copy the pk3/ folder to  <PlasmaVerge>/ExternalContent/staging_pk3/Xonotic/data/
then run  python3 tools/setup_content.py  (the download is skipped when all packs are
already present) — or simply run setup_content.py with internet and skip this pack.

Redistributing an APK built with this data means redistributing GPL content:
keep the credits/licence screen, provide the source of your build on request.
Plasma Verge is an independent project, not endorsed by Team Xonotic.
"""


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, default=ROOT.parent)
    ap.add_argument("--with-content", action="store_true")
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    version = (ROOT / "VERSION").read_text().strip()
    build_code_kit(args.out, version)
    if args.with_content:
        build_content_pack(args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
