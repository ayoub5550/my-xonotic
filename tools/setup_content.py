#!/usr/bin/env python3
"""
setup_content.py — one-command content setup for buyers of the Plasma Verge kit.

The kit ships ONLY original MIT-licensed C#/Python code. The game data (maps,
models, textures, sounds, music) is the freely available Xonotic 0.8.6 release
(GPL, (c) Team Xonotic and contributors). This script:

  1. downloads the official archive  https://dl.xonotic.org/xonotic-0.8.6.zip
     (1.24 GB, resumable) and verifies its published SHA-512;
  2. extracts the seven .pk3 packs into ExternalContent/staging_pk3/;
  3. unpacks   xonotic-20230620-data.pk3  -> ExternalContent/data
               xonotic-20230620-maps.pk3  -> ExternalContent/maps
               xonotic-20230620-music.pk3 -> ExternalContent/music (+ music-manifest.json)
     (in-archive symlinks are resolved to real files, paths are sandboxed);
  4. decodes the DDS textures the Unity importers need into PNG
     (ExternalContent/decoded and ExternalContent/worlddecoded) — needs Pillow;
  5. prints the environment variables for the Unity Editor / tools/local_unity.py.

Everything lands in the git-ignored ExternalContent/ folder. Nothing is executed
from the archives. Re-running is safe: finished steps are skipped.

    python3 tools/setup_content.py            # full setup
    python3 tools/setup_content.py --zip path/to/xonotic-0.8.6.zip   # reuse a download
    python3 tools/setup_content.py --skip-textures                   # data/maps/music only
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import stat
import sys
import urllib.request
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXTERNAL = ROOT / "ExternalContent"
DOWNLOADS = EXTERNAL / "downloads"
STAGING = EXTERNAL / "staging_pk3" / "Xonotic" / "data"

ZIP_URL = "https://dl.xonotic.org/xonotic-0.8.6.zip"
ZIP_BYTES = 1238439495
ZIP_SHA512 = ("cb39879e96f19abb2877588c2d50c5d3e64dd68153bec3dd1bebedf4d765e506"
              "afa419c28381d7005aed664cb1a042571c132b5b319e4308cab67745d996c2a6")

PACK_TARGETS = {
    "xonotic-20230620-data.pk3": EXTERNAL / "data",
    "xonotic-20230620-maps.pk3": EXTERNAL / "maps",
    "xonotic-20230620-music.pk3": EXTERNAL / "music",
}

# cdtrack number -> file name, as used by the maps' worldspawn entities.
MUSIC_TRACKS = [
    "rising-of-the-phoenix", "ninesix", "northern-lights", "meltdown", "stairs",
    "out-there", "zzzzz", "mickrippon_jumpinginspace", "sixtyfour", "traveler",
    "quiet", "neon", "kojinsmokin", "sensation3", "also-t", "heavymetal",
    "inner-peace", "the-laws-of-physics", "variable", "x-force", "nanite", "gogetem",
]

# Character / item / weapon skins the IQM & MD3 importers read from ExternalContent/decoded
# (see docs/UNITY-DEV8.md). Everything under the maps pack's dds/ goes to worlddecoded.
DATA_TEXTURES = [
    "erebus",
    "gak",
    "gakarmor",
    "ignis",
    "ignishead",
    "invincible",
    "models/elecbeam",
    "models/eleccore",
    "models/elecglass",
    "models/items/5hp",
    "models/items/armor",
    "models/items/h25",
    "models/items/h50",
    "models/items/h_mega",
    "models/items/red",
    "models/weapons/laser",
    "nyx",
    "pyria",
    "pyriahair",
    "seraphina",
    "shadowhead",
    "shellsammo",
    "strength",
    "textures/crylink_new",
    "textures/electro_plasma",
    "textures/electronew",
    "textures/glsight01",
    "textures/grenadelauncher",
    "textures/hagar",
    "textures/items/a_bullets",
    "textures/items/cellammo",
    "textures/items/explosiveammo",
    "textures/items/explosiveammo_icon_01",
    "textures/mine",
    "textures/mine_glow",
    "textures/minelayer",
    "textures/minelayer_glow",
    "textures/nexgun",
    "textures/projectiles/crylink_projectile_core",
    "textures/projectiles/crylink_projectile_core_glow",
    "textures/projectiles/crylink_projectile_long",
    "textures/projectiles/crylink_projectile_long_glow",
    "textures/projectiles/electro_projectile_core",
    "textures/projectiles/electro_projectile_core_glow",
    "textures/projectiles/electro_projectile_long",
    "textures/projectiles/electro_projectile_long_glow",
    "textures/projectiles/laser_projectile_core",
    "textures/projectiles/laser_projectile_core_glow",
    "textures/projectiles/laser_projectile_long",
    "textures/projectiles/laser_projectile_long_glow",
    "textures/rl_new",
    "textures/rl_trust01",
    "textures/shotgun2",
    "textures/shotgun_sight",
    "textures/sniperrifle",
    "textures/tuba",
    "textures/uzi",
    "umbra",
]


def log(msg: str) -> None:
    print(f"[setup_content] {msg}", flush=True)


# ----------------------------------------------------------------------------- download
def sha512_of(path: Path) -> str:
    h = hashlib.sha512()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(8 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def download(dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    have = dest.stat().st_size if dest.exists() else 0
    if have >= ZIP_BYTES:
        return
    log(f"downloading {ZIP_URL} ({ZIP_BYTES/1e9:.2f} GB, resuming at {have/1e6:.0f} MB)")
    req = urllib.request.Request(ZIP_URL, headers={"Range": f"bytes={have}-"} if have else {})
    with urllib.request.urlopen(req, timeout=60) as r, dest.open("ab" if have else "wb") as f:
        total = have
        while True:
            chunk = r.read(4 << 20)
            if not chunk:
                break
            f.write(chunk)
            total += len(chunk)
            print(f"\r  {total/1e6:8.0f} / {ZIP_BYTES/1e6:.0f} MB", end="", flush=True)
    print()


def ensure_zip(zip_arg: str | None) -> Path:
    path = Path(zip_arg).resolve() if zip_arg else DOWNLOADS / "xonotic-0.8.6.zip"
    if not path.exists() or path.stat().st_size < ZIP_BYTES:
        if zip_arg:
            sys.exit(f"--zip {path} is missing or incomplete")
        download(path)
    stamp = path.with_suffix(".zip.sha512-ok")
    if not stamp.exists():
        log("verifying SHA-512 (takes a minute)")
        digest = sha512_of(path)
        if digest != ZIP_SHA512:
            sys.exit(f"SHA-512 mismatch for {path}\n  got      {digest}\n  expected {ZIP_SHA512}\n"
                     "Delete the file and run again.")
        stamp.write_text(digest + "\n")
    log(f"archive OK: {path}")
    return path


# ----------------------------------------------------------------------------- extraction
def safe_join(root: Path, member: str) -> Path | None:
    """Returns root/member if it stays inside root, else None."""
    member = member.replace("\\", "/").lstrip("/")
    if not member or member.endswith("/"):
        return None
    parts = [p for p in member.split("/") if p not in ("", ".")]
    if any(p == ".." for p in parts):
        return None
    return root.joinpath(*parts)


def extract_pk3s(zip_path: Path) -> dict[str, Path]:
    STAGING.mkdir(parents=True, exist_ok=True)
    found: dict[str, Path] = {}
    with zipfile.ZipFile(zip_path) as z:
        for info in z.infolist():
            name = info.filename
            if not (name.startswith("Xonotic/data/") and name.endswith(".pk3")):
                continue
            out = STAGING / Path(name).name
            if not out.exists() or out.stat().st_size != info.file_size:
                log(f"extracting {Path(name).name} ({info.file_size/1e6:.0f} MB)")
                with z.open(info) as src, out.open("wb") as dst:
                    for chunk in iter(lambda: src.read(8 << 20), b""):
                        dst.write(chunk)
            found[Path(name).name] = out
    missing = [p for p in PACK_TARGETS if p not in found]
    if missing:
        sys.exit(f"archive lacks packs: {missing}")
    return found


def unpack(pk3: Path, target: Path) -> None:
    done = target / ".unpacked.json"
    if done.exists():
        log(f"{target.relative_to(ROOT)} already unpacked")
        return
    log(f"unpacking {pk3.name} -> {target.relative_to(ROOT)}")
    target.mkdir(parents=True, exist_ok=True)
    files = links = skipped = 0
    with zipfile.ZipFile(pk3) as z:
        infos = {i.filename: i for i in z.infolist()}
        pending_links: list[tuple[zipfile.ZipInfo, Path]] = []
        for info in infos.values():
            if info.is_dir():
                continue
            dest = safe_join(target, info.filename)
            if dest is None:
                skipped += 1
                continue
            is_link = stat.S_ISLNK(info.external_attr >> 16)
            if is_link:
                pending_links.append((info, dest))
                continue
            dest.parent.mkdir(parents=True, exist_ok=True)
            with z.open(info) as src, dest.open("wb") as dst:
                for chunk in iter(lambda: src.read(4 << 20), b""):
                    dst.write(chunk)
            files += 1
        # Symlinks inside the pk3 point at sibling members; copy the real bytes.
        for info, dest in pending_links:
            resolved = resolve_link(z, infos, info)
            if resolved is None:
                skipped += 1
                continue
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(z.read(resolved))
            links += 1
    done.write_text(json.dumps({"source": pk3.name, "files": files, "symlink_copies": links,
                                "skipped": skipped}, indent=2) + "\n")
    log(f"  files={files} symlink_copies={links} skipped={skipped}")


def resolve_link(z: zipfile.ZipFile, infos: dict, info: zipfile.ZipInfo, hops: int = 8):
    current = info
    for _ in range(hops):
        text = z.read(current).decode("utf-8", "replace").strip()
        base = "/".join(current.filename.split("/")[:-1])
        parts = (base.split("/") if base else []) + text.replace("\\", "/").split("/")
        out: list[str] = []
        for p in parts:
            if p in ("", "."):
                continue
            if p == "..":
                if not out:
                    return None
                out.pop()
            else:
                out.append(p)
        name = "/".join(out)
        nxt = infos.get(name)
        if nxt is None:
            return None
        if not stat.S_ISLNK(nxt.external_attr >> 16):
            return nxt
        current = nxt
    return None


def write_music_manifest(music_root: Path) -> None:
    tracks = []
    for i, name in enumerate(MUSIC_TRACKS, start=1):
        entry = f"sound/cdtracks/{name}.ogg"
        if (music_root / entry).exists():
            tracks.append({"number": i, "name": name, "entry": entry})
    (music_root / "music-manifest.json").write_text(json.dumps({"tracks": tracks}, indent=2) + "\n")
    log(f"music-manifest.json: {len(tracks)} tracks")


# ----------------------------------------------------------------------------- textures
def decode_textures() -> None:
    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        log("Pillow missing — run `pip install Pillow` then re-run; skipping texture decode")
        return
    sys.path.insert(0, str(ROOT / "tools" / "content"))
    import prepare_unity_textures as prep  # type: ignore

    decoded = EXTERNAL / "decoded"
    if not (decoded / "conversion-manifest.json").exists():
        log("decoding character/item/weapon skins -> ExternalContent/decoded")
        prep.convert(EXTERNAL / "data", decoded, DATA_TEXTURES)

    world = EXTERNAL / "worlddecoded"
    manifest = world / "world-texture-manifest.json"
    if manifest.exists():
        log("worlddecoded already present")
        return
    from PIL import Image
    src_root = EXTERNAL / "maps" / "dds"
    images, failures = [], []
    dds_files = sorted(src_root.rglob("*.dds"))
    log(f"decoding {len(dds_files)} world textures -> ExternalContent/worlddecoded (several minutes)")
    for n, dds in enumerate(dds_files, 1):
        rel = dds.relative_to(src_root).with_suffix(".png")
        out = world / rel
        try:
            out.parent.mkdir(parents=True, exist_ok=True)
            with Image.open(dds) as im:
                im.convert("RGBA").save(out)
            images.append({"source": str(dds.relative_to(EXTERNAL / "maps")), "decoded": rel.as_posix()})
        except Exception as e:  # unsupported DDS variant: importer falls back to TGA/JPG copies
            failures.append({"source": str(dds.relative_to(EXTERNAL / "maps")), "error": str(e)[:200]})
        if n % 250 == 0:
            print(f"\r  {n}/{len(dds_files)}", end="", flush=True)
    print()
    world.mkdir(parents=True, exist_ok=True)
    manifest.write_text(json.dumps({"scope": "Lossless decoded original pixels", "source_root": "maps",
                                    "images": images, "failures": failures}, indent=1) + "\n")
    log(f"  decoded={len(images)} failed={len(failures)}")


# ----------------------------------------------------------------------------- main
def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--zip", help="already downloaded xonotic-0.8.6.zip (skips download)")
    ap.add_argument("--skip-textures", action="store_true")
    args = ap.parse_args()

    packs = {n: STAGING / n for n in PACK_TARGETS}
    if all(p.exists() for p in packs.values()) and not args.zip:
        log("all .pk3 packs already in ExternalContent/staging_pk3 (Content Pack) — no download")
    else:
        zip_path = ensure_zip(args.zip)
        packs = extract_pk3s(zip_path)
    for pk3_name, target in PACK_TARGETS.items():
        unpack(packs[pk3_name], target)
    write_music_manifest(EXTERNAL / "music")
    if not args.skip_textures:
        decode_textures()

    roots = ":".join(str(EXTERNAL / d) for d in ("decoded", "worlddecoded", "maps", "data"))
    print("\nDone. Before opening Unity (or running tools/local_unity.py) export:\n")
    print(f'  export XONOTIC_CONTENT_ROOTS="{roots}"')
    print(f'  export XONOTIC_MAPS_ROOT="{EXTERNAL / "maps"}"')
    print(f'  export XONOTIC_MUSIC_ROOT="{EXTERNAL / "music"}"')
    print('  export XONOTIC_ALL_MAPS=1\n')
    print("Then in Unity: Plasma Verge > 1 - Configure local project, then 5 - Prepare ALL maps + menu,")
    print("then 4 - Build local Android development APK. See BUYER-GUIDE.md.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
