#!/usr/bin/env python3
"""
map_coverage.py — bounded, read-only entity-class inventory for official
Xonotic BSP maps staged inside a verified `maps.pk3`.

Scope and intent
-----------------
This tool answers exactly one question, safely and cheaply: *which entity
classes (spawn points, pickups, movers, triggers, targets, lights, other)
does each official compiled map actually contain, and how many of each?*
It does **not** claim any map is playable, does not run any gameplay code,
does not import geometry, and does not extract or even touch anything in
the archive other than the handful of `.bsp` central-directory entries
(31 in the current staged `xonotic-20230620-maps.pk3`, out of ~4,844 total
entries — textures, sounds, shaders, etc. are never opened by this tool).

Bounded/safe-by-construction design, mirroring
`Assets/MyXonotic/Runtime/Content/Bsp/BspReader.cs` and
`Assets/MyXonotic/Runtime/Content/Bsp/BspEntityParser.cs` (reimplemented
here from scratch in Python against the same publicly documented IBSP v46
layout — no GPL engine/QuakeC source read or copied):

  - The archive's central directory is inspected via
    `tools/content/pk3_safety.inspect_entries()` **exactly once** per
    archive (O(n) over the ~4,844 entries), never once per BSP file — the
    naive "call safe_extract_one() per map" approach would re-run that full
    metadata pass 31 times over, which is the O(n^2) pattern this module
    deliberately avoids. See `_load_bsp_entries()`.
  - No BSP is ever written to disk. Each candidate `.bsp` entry is read
    straight from the open `ZipFile` into memory, streamed in bounded
    chunks with a hard byte ceiling enforced *during* decompression
    (`_read_entry_bounded()`), exactly like `pk3_safety.safe_extract_one()`
    does for its zip-bomb guard — just without the disk write, because this
    tool only needs the bytes, not a persistent extracted copy.
  - Every BSP-internal offset/length (header lump directory, entities lump)
    is bounds-checked against the actual buffer before use, with the same
    finite safety ceilings `BspReader.cs` uses (32 MiB entity lump, IBSP
    version 46 only, 17 fixed lumps). A malformed/hostile BSP raises
    `MapCoverageError` and the caller records it as a per-map failure; it
    never crashes the whole inventory run and never allocates memory
    proportional to a hostile *claimed* size before that size is checked.
  - The entity lump text parser (`parse_entities()`) reimplements the same
    token grammar and budgets as `BspEntityParser.cs` (max 65,536 entities,
    max 262,144 properties, max 1,000 warnings, max 65,536-byte token) so a
    hostile/corrupt entity lump cannot cause unbounded work either.
  - Nothing extracted or parsed here is ever executed, evaluated, or
    imported as code. This module has no non-stdlib dependencies.

This tool proves *presence* of entity classes in the official compiled
maps. It says nothing about whether the current importer/gameplay code
actually spawns, renders, or simulates any of them at runtime — that is a
separate, already-documented gap (see `AGENTS.md`,
`Assets/MyXonotic/Editor/Import/BspImportPipeline.cs` and
`BspGameplayImporter.cs`). `docs/FULL-GAME-GATES.md` cross-references the
`supported_classnames_present` / `unsupported_classnames_present` fields
this tool produces against the actual current source, not against any
completion estimate.
"""
from __future__ import annotations

import argparse
import dataclasses
import hashlib
import json
import os
import struct
import sys
import zipfile
from typing import Dict, List, Optional, Tuple

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pk3_safety as safety  # noqa: E402

# --------------------------------------------------------------------------
# IBSP v46 constants (matches Assets/MyXonotic/Runtime/Content/Bsp/BspReader.cs
# and BspRecords.cs; independently reimplemented from the same publicly
# documented layout, not copied from any GPL engine source).
# --------------------------------------------------------------------------
IBSP_MAGIC = b"IBSP"
IBSP_SUPPORTED_VERSION = 46
LUMP_COUNT = 17
LUMP_ENTITIES = 0
HEADER_MAGIC_SIZE = 4
LUMP_DIR_ENTRY_SIZE = 8  # int32 offset, int32 length
HEADER_SIZE = HEADER_MAGIC_SIZE + 4 + LUMP_COUNT * LUMP_DIR_ENTRY_SIZE  # 8 + 17*8 = 144

MAX_ENTITY_LUMP_BYTES = 32 * 1024 * 1024  # matches BspReader.MaxEntityLumpBytes

# A real official Xonotic map .bsp is a few hundred KB to tens of MB
# (catharsis.bsp is the largest in the 0.8.6 maps.pk3 at ~37 MB
# uncompressed). This ceiling is generous but finite: it bounds the
# in-memory read of a single archive entry regardless of what its
# (attacker-controlled, if the archive were untrusted) central-directory
# metadata claims, independent of pk3_safety's own per-entry ceiling.
MAX_BSP_ENTRY_BYTES = 256 * 1024 * 1024

# Entity-lump parser budgets, matching BspEntityParser.cs exactly.
MAX_ENTITIES = 65536
MAX_PROPERTIES = 262144
MAX_WARNINGS = 1000
MAX_TOKEN_LENGTH = 65536

MAP_COVERAGE_TOOL_VERSION = 2


class MapCoverageError(ValueError):
    """Raised for any malformed/oversized/out-of-bounds BSP input.
    Mirrors BspFormatException's role: a per-map failure, never a crash of
    the whole inventory run when the caller catches it (see inventory_pk3)."""


# --------------------------------------------------------------------------
# Entity classname categorisation. Matches the classname prefixes/exact
# names actually referenced by BspImportPipeline.cs (spawn markers,
# unsupported-class warnings) and BspGameplayImporter.cs (trigger_push /
# trigger_teleport / trigger_hurt), not an invented taxonomy.
# --------------------------------------------------------------------------
CATEGORY_SPAWN = "spawn"
CATEGORY_PICKUP = "pickup"
CATEGORY_MOVER = "mover"
CATEGORY_TRIGGER = "trigger"
CATEGORY_TARGET = "target"
CATEGORY_LIGHT = "light"
CATEGORY_WORLDSPAWN = "worldspawn"
CATEGORY_OTHER = "other"

_EXACT_SPAWN_CLASSES = {"info_player_deathmatch", "info_player_start"}

# The exact classnames BspGameplayImporter.cs actually builds MapTrigger
# volumes for today. Any other trigger_* classname is present-but-unsupported.
SUPPORTED_TRIGGER_CLASSES = {"trigger_push", "trigger_teleport", "trigger_hurt"}
# The exact classnames BspImportPipeline.cs actually builds BspSpawnPoint
# markers for today.
SUPPORTED_SPAWN_CLASSES = set(_EXACT_SPAWN_CLASSES) | {
    "info_player_team1", "info_player_team2", "info_player_team3", "info_player_team4",
    "info_player_race", "info_player_attacker", "info_player_defender",
}
# dev.4 BspPickupImporter mappings, verified on Boil in Editor/Play Mode.
# Presence in this set means a code path exists, NOT that every map works.
SUPPORTED_PICKUP_CLASSES = {
    "item_health_small", "item_health_medium", "item_health_big", "item_health_mega",
    "item_armor_small", "item_armor_medium", "item_armor_big", "item_armor_mega",
    "item_rockets",
}
SUPPORTED_CLASSNAMES = (
    SUPPORTED_SPAWN_CLASSES | SUPPORTED_TRIGGER_CLASSES | SUPPORTED_PICKUP_CLASSES
)


def classify_classname(classname: str) -> str:
    """Buckets a raw entity classname into one coarse coverage category.
    Purely a reporting aid: it does not imply any of these are imported.
    """
    if not classname:
        return CATEGORY_OTHER
    if classname == "worldspawn":
        return CATEGORY_WORLDSPAWN
    if classname in _EXACT_SPAWN_CLASSES:
        return CATEGORY_SPAWN
    if classname.startswith("item_") or classname.startswith("weapon_"):
        return CATEGORY_PICKUP
    if classname.startswith("func_"):
        return CATEGORY_MOVER
    if classname.startswith("trigger_"):
        return CATEGORY_TRIGGER
    if classname.startswith("target_") or classname.startswith("path_") or classname.startswith("misc_teleporter_dest"):
        return CATEGORY_TARGET
    if classname == "light" or classname.startswith("light_"):
        return CATEGORY_LIGHT
    return CATEGORY_OTHER


# --------------------------------------------------------------------------
# Bounded IBSP header + entities-lump extraction (pure functions over an
# in-memory buffer already read to completion; no I/O here).
# --------------------------------------------------------------------------

def _read_u32_le(data: bytes, offset: int) -> int:
    return struct.unpack_from("<I", data, offset)[0]


def _read_i32_le(data: bytes, offset: int) -> int:
    return struct.unpack_from("<i", data, offset)[0]


@dataclasses.dataclass
class BspLumpEntry:
    offset: int
    length: int


def parse_header(data: bytes) -> Tuple[int, List[BspLumpEntry]]:
    """Validates magic/version and returns (version, lump directory).
    Every offset/length is bounds-checked against len(data); this never
    reads lump *contents*, only the fixed 144-byte directory.
    """
    if not isinstance(data, (bytes, bytearray)):
        raise MapCoverageError("BSP data must be bytes.")
    if len(data) < HEADER_SIZE:
        raise MapCoverageError(
            f"BSP file too small to contain a header ({len(data)} bytes, need at least {HEADER_SIZE})."
        )
    magic = bytes(data[0:4])
    if magic != IBSP_MAGIC:
        raise MapCoverageError(f"Unsupported BSP magic {magic!r}: only IBSP (Quake III family) is supported.")
    version = _read_i32_le(data, 4)
    if version != IBSP_SUPPORTED_VERSION:
        raise MapCoverageError(
            f"Unsupported IBSP version {version}: only version {IBSP_SUPPORTED_VERSION} (Quake III / Xonotic) is implemented."
        )

    lumps: List[BspLumpEntry] = []
    for i in range(LUMP_COUNT):
        entry_offset = 8 + i * LUMP_DIR_ENTRY_SIZE
        offset = _read_i32_le(data, entry_offset)
        length = _read_i32_le(data, entry_offset + 4)
        if offset < 0 or length < 0:
            raise MapCoverageError(f"Lump {i} has a negative offset/length ({offset}/{length}).")
        end = offset + length  # Python ints don't overflow; still explicit.
        if end > len(data):
            raise MapCoverageError(
                f"Lump {i} claims range [{offset},{end}) which exceeds file size {len(data)}."
            )
        lumps.append(BspLumpEntry(offset=offset, length=length))
    return version, lumps


def extract_entities_text(data: bytes, lumps: List[BspLumpEntry]) -> str:
    """Returns the decoded entities-lump text, bounds- and size-checked
    against MAX_ENTITY_LUMP_BYTES *before* any slicing/decoding happens
    (mirrors BspReader.ReadEntities' ordering: the declared length is
    rejected first, so a hostile huge declared length never reaches the
    decode step even if the underlying buffer happened to be large enough).
    """
    lump = lumps[LUMP_ENTITIES]
    if lump.length > MAX_ENTITY_LUMP_BYTES:
        raise MapCoverageError(
            f"Entities lump is {lump.length} bytes, exceeding the {MAX_ENTITY_LUMP_BYTES}-byte safety ceiling."
        )
    if lump.length == 0:
        return ""
    raw = bytes(data[lump.offset:lump.offset + lump.length])
    # .NET's Encoding.ASCII.GetString maps any byte >= 0x80 to '?'; Python's
    # ascii/replace maps it to U+FFFD instead. Both are lossy-but-safe for
    # non-ASCII bytes and immaterial for classnames (always plain ASCII
    # identifiers in every known Xonotic .ent block); documented divergence,
    # not a functional difference for anything this tool reports.
    text = raw.decode("ascii", errors="replace")
    nul = text.find("\x00")
    if nul >= 0:
        text = text[:nul]
    return text


@dataclasses.dataclass
class ParsedEntity:
    properties: Dict[str, str]

    def get(self, key: str) -> Optional[str]:
        return self.properties.get(key)


def parse_entities(text: str) -> Tuple[List[ParsedEntity], List[str]]:
    """Tokenizes the BSP entities-lump text. Reimplemented from scratch in
    Python against the same "{ "key" "value" ... }" grammar (with "//"
    line comments) as BspEntityParser.cs, including its exact budgets, so a
    hostile/corrupt lump cannot cause unbounded entities/properties/work
    here either. Never raises for malformed *content*; malformed blocks are
    recorded as warnings and skipped, matching the C# implementation's
    "warnings, not exceptions, for per-entity issues" style.
    """
    entities: List[ParsedEntity] = []
    warnings: List[str] = []
    i = 0
    n = len(text)
    current: Optional[Dict[str, str]] = None
    pending_key: Optional[str] = None
    properties = 0

    def _warn(msg: str) -> None:
        if len(warnings) < MAX_WARNINGS:
            warnings.append(msg)

    while i < n:
        if len(entities) >= MAX_ENTITIES or properties >= MAX_PROPERTIES or len(warnings) >= MAX_WARNINGS:
            raise MapCoverageError("Entity text exceeds the entity/property/diagnostic budget.")
        c = text[i]

        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue

        if c.isspace():
            i += 1
            continue

        if c == "{":
            if current is not None:
                _warn("Entity lump: nested '{'; discarding incomplete previous block.")
            current = {}
            pending_key = None
            i += 1
            continue

        if c == "}":
            if current is None:
                _warn("Entity lump: stray '}' with no open block; ignoring.")
            else:
                entities.append(ParsedEntity(properties=current))
                current = None
                pending_key = None
            i += 1
            continue

        if c == '"':
            i += 1
            start = i
            chars: List[str] = []
            while i < n and text[i] != '"':
                if len(chars) >= MAX_TOKEN_LENGTH:
                    raise MapCoverageError("Entity token exceeds the length budget.")
                if text[i] == "\\" and i + 1 < n and text[i + 1] in ("\\", '"'):
                    i += 1
                chars.append(text[i])
                i += 1
            if i >= n:
                _warn("Entity lump: unterminated quoted token at end of lump; truncating parse.")
                break
            i += 1  # skip closing quote
            token = "".join(chars)
            if current is None:
                _warn("Entity lump: quoted token outside of any '{' block; ignoring.")
            elif pending_key is None:
                pending_key = token
            else:
                current[pending_key] = token
                properties += 1
                pending_key = None
            continue

        # Any other stray character is skipped defensively.
        i += 1

    if current is not None:
        _warn("Entity lump: unterminated block at end of lump; discarding incomplete entity.")

    return entities, warnings


# --------------------------------------------------------------------------
# Archive-level, single-pass BSP entry discovery + bounded per-entry read.
# --------------------------------------------------------------------------

@dataclasses.dataclass
class BspEntryHandle:
    name: str
    zip_info: "zipfile.ZipInfo"
    report: "safety.EntryReport"


def _load_bsp_entries(zf: zipfile.ZipFile) -> Tuple[List[BspEntryHandle], List[Dict]]:
    """Single O(n) pass over the archive's central directory (via
    pk3_safety.inspect_entries, called exactly once) to find every `.bsp`
    entry and its safety report. Entries flagged suspicious by that single
    pass (unsafe path, symlink, encrypted, oversized, bad ratio, duplicate
    name) are excluded and returned separately with the reason, never
    opened. This is the only full-archive metadata pass this module ever
    performs, regardless of how many BSP entries are subsequently read.
    """
    reports = safety.inspect_entries(zf)  # O(n) once, not once per map.
    by_name = {info.filename: info for info in zf.infolist()}

    handles: List[BspEntryHandle] = []
    skipped: List[Dict] = []
    for report in reports:
        if not report.name.lower().endswith(".bsp") or report.is_dir:
            continue
        if report.is_suspicious:
            skipped.append({"name": report.name, "reasons": report.reasons})
            continue
        info = by_name.get(report.name)
        if info is None:
            skipped.append({"name": report.name, "reasons": ["entry vanished between listing passes"]})
            continue
        handles.append(BspEntryHandle(name=report.name, zip_info=info, report=report))
    return handles, skipped


def _read_entry_bounded(zf: zipfile.ZipFile, info: "zipfile.ZipInfo", max_bytes: int) -> bytes:
    """Streams exactly one archive entry fully into memory, enforcing a
    hard byte ceiling *during* decompression (not just checking the
    declared metadata first) — the same zip-bomb guard shape as
    pk3_safety.safe_extract_one()'s write loop, just producing bytes in
    memory instead of a file on disk. CRC verification happens implicitly:
    zipfile raises BadZipFile on close() if the decompressed CRC does not
    match the central-directory record.
    """
    if info.file_size > max_bytes:
        raise MapCoverageError(
            f"Entry '{info.filename}' declares {info.file_size} bytes, exceeding the {max_bytes}-byte read ceiling."
        )
    chunks: List[bytes] = []
    total = 0
    try:
        with zf.open(info, "r") as src:
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                total += len(chunk)
                if total > max_bytes:
                    raise MapCoverageError(
                        f"Entry '{info.filename}' produced more than {max_bytes} bytes while streaming; aborted (zip-bomb guard)."
                    )
                chunks.append(chunk)
    except zipfile.BadZipFile as exc:
        raise MapCoverageError(f"Entry '{info.filename}' failed CRC verification while streaming: {exc}") from exc
    return b"".join(chunks)


# --------------------------------------------------------------------------
# Per-map and whole-archive inventory.
# --------------------------------------------------------------------------

def inventory_one_bsp(name: str, data: bytes) -> Dict:
    """Pure function: given one BSP file's raw bytes (already bounded-read
    into memory by the caller), returns the per-map coverage record. Raises
    MapCoverageError for malformed input; the caller decides whether that
    is fatal or recorded as a per-map failure (inventory_pk3 does the
    latter, so one bad map cannot abort the whole archive's inventory).
    """
    version, lumps = parse_header(data)
    entities_text = extract_entities_text(data, lumps)
    entities, warnings = parse_entities(entities_text)

    class_counts: Dict[str, int] = {}
    category_counts: Dict[str, int] = {
        CATEGORY_SPAWN: 0, CATEGORY_PICKUP: 0, CATEGORY_MOVER: 0,
        CATEGORY_TRIGGER: 0, CATEGORY_TARGET: 0, CATEGORY_LIGHT: 0,
        CATEGORY_WORLDSPAWN: 0, CATEGORY_OTHER: 0,
    }
    supported_present = set()
    unsupported_present = set()

    for ent in entities:
        classname = ent.get("classname") or ""
        if not classname:
            category_counts[CATEGORY_OTHER] += 1
            continue
        class_counts[classname] = class_counts.get(classname, 0) + 1
        category = classify_classname(classname)
        category_counts[category] += 1
        if classname == "worldspawn":
            continue
        if classname in SUPPORTED_CLASSNAMES:
            supported_present.add(classname)
        else:
            unsupported_present.add(classname)

    return {
        "map_entry": name,
        "ibsp_version": version,
        "source_sha256": safety.sha256_bytes(data),
        "source_size_bytes": len(data),
        "entity_count": len(entities),
        "entity_lump_bytes": lumps[LUMP_ENTITIES].length,
        "classname_counts": dict(sorted(class_counts.items())),
        "category_counts": category_counts,
        "supported_classnames_present": sorted(supported_present),
        "unsupported_classnames_present": sorted(unsupported_present),
        "parse_warnings": warnings,
    }


def inventory_pk3(pk3_path: str, *, max_bsp_entry_bytes: int = MAX_BSP_ENTRY_BYTES) -> Dict:
    """Inventories every `.bsp` entry in `pk3_path`. Never extracts/opens
    any non-`.bsp` entry. A per-map failure (malformed BSP, oversized/
    suspicious entry, CRC mismatch) is recorded in `failed_maps` and does
    not abort the run; a whole-archive failure (not a zip, entry count
    ceiling exceeded) raises like `pk3_tool.py inventory` does.
    """
    if not os.path.isfile(pk3_path):
        raise MapCoverageError(f"No such file: {pk3_path}")

    archive_sha256 = safety.sha256_file(pk3_path)
    archive_size = os.path.getsize(pk3_path)

    maps: List[Dict] = []
    failed_maps: List[Dict] = []

    with zipfile.ZipFile(pk3_path, "r") as zf:
        handles, skipped_entries = _load_bsp_entries(zf)
        for skipped in skipped_entries:
            failed_maps.append({"map_entry": skipped["name"], "error": "; ".join(skipped["reasons"])})

        for handle in handles:
            try:
                data = _read_entry_bounded(zf, handle.zip_info, max_bsp_entry_bytes)
                record = inventory_one_bsp(handle.name, data)
                record["compressed_size_bytes"] = handle.zip_info.compress_size
                maps.append(record)
            except MapCoverageError as exc:
                failed_maps.append({"map_entry": handle.name, "error": str(exc)})

    maps.sort(key=lambda r: r["map_entry"])
    failed_maps.sort(key=lambda r: r["map_entry"])

    totals: Dict[str, int] = {
        CATEGORY_SPAWN: 0, CATEGORY_PICKUP: 0, CATEGORY_MOVER: 0,
        CATEGORY_TRIGGER: 0, CATEGORY_TARGET: 0, CATEGORY_LIGHT: 0,
        CATEGORY_WORLDSPAWN: 0, CATEGORY_OTHER: 0,
    }
    for record in maps:
        for k, v in record["category_counts"].items():
            totals[k] = totals.get(k, 0) + v

    return {
        "tool": "tools/content/map_coverage.py",
        "tool_version": MAP_COVERAGE_TOOL_VERSION,
        "archive_path": os.path.abspath(pk3_path),
        "archive_sha256": archive_sha256,
        "archive_size_bytes": archive_size,
        "bsp_maps_found": len(handles) + len(skipped_entries),
        "bsp_maps_inventoried": len(maps),
        "bsp_maps_failed": len(failed_maps),
        "category_totals": totals,
        "maps": maps,
        "failed_maps": failed_maps,
        "note": (
            "Entity-class presence only. Import/gameplay support for any listed "
            "classname is NOT implied; cross-reference supported_classnames_present "
            "against the current source (see docs/FULL-GAME-GATES.md). No map is "
            "claimed runnable, tested, or textured/lit by this tool."
        ),
    }


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def _write_new_file_exclusive(path: str, data: bytes) -> None:
    flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY
    flags |= getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(path, flags, 0o644)
    with os.fdopen(fd, "wb") as f:
        f.write(data)


def cmd_inventory(args: argparse.Namespace) -> int:
    try:
        report = inventory_pk3(args.pk3, max_bsp_entry_bytes=args.max_bsp_entry_bytes)
    except MapCoverageError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3
    except zipfile.BadZipFile as exc:
        print(f"error: not a valid zip/pk3 archive: {exc}", file=sys.stderr)
        return 3
    except safety.Pk3SafetyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3

    if args.out_dir:
        os.makedirs(args.out_dir, exist_ok=True)
        summary_path = os.path.join(args.out_dir, "summary.json")
        if os.path.exists(summary_path):
            os.remove(summary_path)  # explicit re-run overwrite, not silent append/merge.
        _write_new_file_exclusive(summary_path, json.dumps(report, indent=2).encode("utf-8"))
        for record in report["maps"]:
            safe_stem = os.path.basename(record["map_entry"]).replace("/", "_")
            per_map_path = os.path.join(args.out_dir, safe_stem + ".json")
            if os.path.exists(per_map_path):
                os.remove(per_map_path)
            _write_new_file_exclusive(per_map_path, json.dumps(record, indent=2).encode("utf-8"))
        print(f"Wrote {1 + len(report['maps'])} report file(s) under {args.out_dir}")

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Archive: {report['archive_path']}")
        print(f"SHA256:  {report['archive_sha256']}")
        print(f"BSP maps found: {report['bsp_maps_found']}, inventoried: {report['bsp_maps_inventoried']}, failed: {report['bsp_maps_failed']}")
        print(f"Category totals: {json.dumps(report['category_totals'])}")
        if report["failed_maps"]:
            print("Failed maps:")
            for f in report["failed_maps"]:
                print(f"  - {f['map_entry']}: {f['error']}")

    return 0 if report["bsp_maps_failed"] == 0 else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="map_coverage.py",
        description="Bounded, read-only entity-class inventory of official BSP maps inside a staged Xonotic maps.pk3.",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_inv = sub.add_parser("inventory", help="Inventory every .bsp entry's entity classes in a .pk3.")
    p_inv.add_argument("pk3", help="Path to the staged maps .pk3 (ZIP) archive.")
    p_inv.add_argument("--json", action="store_true", help="Print the full report as JSON instead of a summary.")
    p_inv.add_argument("--out-dir", metavar="DIR", help="Write summary.json + one per-map JSON file to DIR (e.g. Artifacts/map_coverage).")
    p_inv.add_argument("--max-bsp-entry-bytes", type=int, default=MAX_BSP_ENTRY_BYTES,
                        help="Per-entry in-memory read ceiling (default: %(default)s).")
    p_inv.set_defaults(func=cmd_inventory)

    return parser


def main(argv=None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (OSError, MapCoverageError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
