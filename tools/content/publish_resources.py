#!/usr/bin/env python3
"""
publish_resources.py — stage the ACTUAL extracted bytes of all seven
verified official Xonotic 0.8.6 `.pk3` archives (plus the already-converted
derived PNGs and licence notices) into
`ThirdParty/Xonotic-0.8.6/` so they can be committed to Git as real files,
not a download script.

This is a NEW, separate tree from `ThirdParty/Xonotic/` (the existing
207-file resource index and its own `verify_resources.py`). Nothing here
reads, writes, or changes `ThirdParty/Xonotic/` or `tools/content/
verify_resources.py`; that verifier's 207-file scope is untouched.

Ownership / scope
------------------
This tool and `ThirdParty/Xonotic-0.8.6/` are owned together, as one unit,
by whoever runs `publish`. It never touches Unity, `Assets/`, `Editor/`,
or any other worker's tree. It never launches Unity and never executes
anything found inside a `.pk3` (`.cfg`/`.shader`/`.qc`-looking bytes are
copied as opaque bytes, exactly like every other entry, never imported,
evaled, or subprocessed).

Safety, mirroring pk3_safety.py / stage_full_maps.py (reused, not
re-invented)
------------------------------------------------------------------------
  - Each source `.pk3`'s central directory is inspected via
    `pk3_safety.inspect_entries()` **exactly once** per archive, regardless
    of how many of its entries get staged — the same anti-O(n^2) shape
    `stage_full_maps.py` uses, deliberately NOT calling
    `pk3_safety.safe_extract_one()` per entry (that function re-runs the
    full metadata pass on every call).
  - Every entry actually written to disk goes through
    `pk3_safety._safe_makedirs()` (symlink-swap-safe directory creation)
    and no-clobber `O_EXCL|O_NOFOLLOW` creation, then bounded chunked
    streaming with the same per-entry byte ceiling
    (`pk3_safety.MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES`) as
    `pk3_safety.safe_extract_one()`'s write loop. CRC verification happens
    via `zipfile` raising `BadZipFile` when a corrupted stream is read to
    EOF; a CRC failure removes the partial file and is recorded as a
    per-entry failure, never aborting the run.
  - Path traversal / absolute paths / encrypted entries / oversized
    entries / bad per-entry compression ratio / duplicate-name ambiguity
    are all read from the single `inspect_entries()` report; any such
    entry (other than a plain symlink, handled below) is skipped and
    recorded, never staged.
  - **No OS symlinks are ever created.** A ZIP symlink entry's "content"
    (the target text) is read, decoded, and recorded verbatim in the
    manifest (`symlink_target_text`). If, and only if, that target
    resolves — relative to the symlink's own directory, normalized,
    without any ".." escaping the archive root, without an entry-name
    ambiguity, and without a cycle across at most
    `MAX_SYMLINK_HOPS` hops — to an existing plain (non-symlink,
    non-directory) entry in the SAME archive, this tool copies that
    target entry's actual bytes to the symlink's own path as a regular
    file (`type: "symlink_resolved_copy"`), and also records
    `symlink_resolved_from` (the final target member name) and
    `symlink_hops` (the chain) for provenance. Any symlink that cannot be
    safely resolved this way is recorded as `type: "symlink_unresolved"`
    with only the target text kept; no file is written for it, and no
    real symlink is ever created.
  - Byte-identical files (by SHA-256, computed while streaming/copying —
    never assumed) are hardlinked (`os.link`) to the first copy already
    written under `ThirdParty/Xonotic-0.8.6/`, instead of being
    duplicated on disk, PROVIDED the file is never opened for writing
    again afterwards (true here: every file this tool writes is written
    exactly once, then left read-only content). This includes resolved
    symlink copies that happen to match a plain entry already staged
    elsewhere (a very common shape upstream: many "compat" skin symlinks
    point at the same handful of base textures).
  - No network access. Stdlib only (`zipfile`, `hashlib`, `json`, `os`).
    This tool never downloads anything; every byte it stages already
    exists locally under `ExternalContent/`.

What gets staged
-----------------
  packs/<pack-key>/...   one directory per source `.pk3`, entries kept at
                          their original ZIP member paths (this is
                          intentionally NOT merged with any other pack —
                          "keeping separation" per the owning task).
  derived/<root>/...     already-produced conversions this tool does not
                          regenerate: `ExternalContent/decoded`,
                          `ExternalContent/worlddecoded`,
                          `ExternalContent/characters`,
                          `ExternalContent/music` (manifest + audio).
                          Exact-duplicate bytes within `derived/` are
                          hardlinked together (same global dedup table as
                          `packs/`); paths are still each recorded.
  notices/...             `ExternalContent/notices` licence texts, copied
                          verbatim (never treated as code).

Excluded by design: `ExternalContent/downloads/*.zip` (the huge original
archive itself — its SHA-512 is already recorded in
`docs/FULL-GAME-GATES.md` / `AGENTS.md` and re-verified below at publish
time; re-committing the 1.2 GB zip on top of its unpacked contents would
just double the bytes for no benefit), any `*.provenance.json` staging-
side sidecar files under `ExternalContent/staging_pk3/` (their content is
folded into this tool's own manifest instead), credentials, logs,
keystores, temp/build caches and `.apk` files (there are none under the
paths this tool reads; it does not walk outside `ExternalContent/`).

`restore` subcommand (materialize committed bytes back into the ignored
`ExternalContent/` working roots importers/tools expect, no download)
------------------------------------------------------------------------
`publish` is one-way (`ExternalContent` -> `ThirdParty/Xonotic-0.8.6`).
`restore` is the inverse, for a fresh checkout that only has the Git-
committed `ThirdParty/Xonotic-0.8.6/` tree and needs the working
`ExternalContent/staging_pk3/...`, `ExternalContent/decoded`,
`ExternalContent/worlddecoded`, `ExternalContent/characters`,
`ExternalContent/music` roots back (all `.gitignore`d, ephemeral, and
exactly what `tools/content/prepare_unity_textures.py`,
`stage_full_maps.py`, `stage_weapon_art.py` etc. read). For every file
recorded in a manifest, `restore`:
  - if the destination already exists AND its SHA-256 matches the
    manifest -> leaves it alone (never re-copies unchanged bytes);
  - if the destination already exists AND its SHA-256 differs -> refuses
    to overwrite and records a `mismatch` (never silently clobbers a
    locally modified file);
  - otherwise copies the committed bytes back (regular files only; a
    `symlink_unresolved` entry has no bytes to restore and is skipped
    with a note, matching `publish`'s own limitation).
`restore` never re-derives anything and never downloads; it only ever
copies bytes already present in `ThirdParty/Xonotic-0.8.6/`.
"""
from __future__ import annotations

import argparse
import dataclasses
import errno
import hashlib
import json
import os
import shutil
import stat
import sys
import time
import zipfile
from typing import Dict, List, Optional, Tuple

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pk3_safety as safety  # noqa: E402

TOOL_VERSION = 1
MAX_SYMLINK_HOPS = 8
MAX_ENTRY_BYTES = safety.MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES
MAX_COMMIT_FILE_BYTES = 100 * 1024 * 1024  # 100 MiB, matches verify_resources.py's ceiling

# Already independently verified against the publisher's own published value
# (see docs/FULL-GAME-GATES.md / AGENTS.md "Complete official 0.8.6 archive
# SHA512 verified"). Recorded here so an ordinary `publish` run does not
# need to re-hash the 1.2 GB download just to repeat that confirmation;
# pass --verify-full-zip to actually re-hash and assert it still matches.
KNOWN_FULL_ZIP_SHA512 = (
    "cb39879e96f19abb2877588c2d50c5d3e64dd68153bec3dd1bebedf4d765e506afa419c28381d7005aed664cb1a042571c132b5b319e4308cab67745d996c2a6"
)
KNOWN_FULL_ZIP_SHA512_SOURCE = "docs/FULL-GAME-GATES.md (re-verified against the publisher's published hash)"

# Pinned SHA-256 of exactly the seven verified official 0.8.6 pk3 archives
# this tool is scoped to (six from their own
# ExternalContent/staging_pk3/.../*.provenance.json sidecars; xonotic-
# 20230620-maps.pk3 has no such sidecar in this tree, so its hash was
# independently computed and cross-checked against docs/FULL-GAME-GATES.md,
# where it is also recorded). This is the ONLY allowlist that may waive the
# metadata-only "compression ratio" zip-bomb heuristic in _stage_pack(): the
# waiver is a property of these seven specific, already-verified byte
# sequences, never a blanket "any zip" policy — an archive whose bytes
# don't hash-match its expected pack name (tampering, a different release,
# a caller passing an arbitrary third-party zip) gets NO ratio waiver and
# every ratio-flagged entry in it is hard-skipped like any other unverified
# archive would be.
KNOWN_PACK_SHA256: Dict[str, str] = {
    "font-unifont-20230620.pk3": "7cc9ca2e4cccaa7533d7e9fe7a6e5b5dd6e4af0c8551c1e9edc40d32b90dba2b",
    "font-xolonium-20230620.pk3": "d6404f31eea4b90d7fe4951b41b692261ef26e6b512b5c7de7a9e99d5d57b586",
    "xonotic-20230620-data.pk3": "7602be0d44a4f1ce4f0918c54ceb215c9bc963a103623d6c7874f081330c8505",
    "xonotic-20230620-maps.pk3": "d10e5a8eac468a4a5b6d80f9603d95949971c6b10f86bec65a79e19f0d424f54",
    "xonotic-20230620-music.pk3": "133fbcfa5766cd6849c2918bb34ce4d749aeafcbf89de54df467a61296353f48",
    "xonotic-20230620-nexcompat.pk3": "34021da1176e2b47383c94707b5922d9a5f3c4ff43f8ac4ac0e39da44648ccbe",
    "xonotic-20230620-xoncompat.pk3": "e3aaff8e6dfb7a0031e2a04d62e5146fd7a9710264c7af1c3be752b62629b739",
}

REPO_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
EXTERNAL_CONTENT = os.path.join(REPO_ROOT, "ExternalContent")
STAGING_PK3_DIR = os.path.join(EXTERNAL_CONTENT, "staging_pk3", "Xonotic", "data")
NOTICES_DIR = os.path.join(EXTERNAL_CONTENT, "notices")
DEST_ROOT = os.path.join(REPO_ROOT, "ThirdParty", "Xonotic-0.8.6")

# The seven verified official archives this task covers. `pack_key` is the
# directory name under packs/; deliberately the plain pk3 stem so the
# manifest and the on-disk tree agree without any renaming logic.
PACKS: List[Tuple[str, str]] = [
    ("font-unifont-20230620", "font-unifont-20230620.pk3"),
    ("font-xolonium-20230620", "font-xolonium-20230620.pk3"),
    ("xonotic-20230620-data", "xonotic-20230620-data.pk3"),
    ("xonotic-20230620-maps", "xonotic-20230620-maps.pk3"),
    ("xonotic-20230620-music", "xonotic-20230620-music.pk3"),
    ("xonotic-20230620-nexcompat", "xonotic-20230620-nexcompat.pk3"),
    ("xonotic-20230620-xoncompat", "xonotic-20230620-xoncompat.pk3"),
]

# Derived/already-converted roots this tool copies as-is (never regenerates).
DERIVED_ROOTS: List[Tuple[str, str]] = [
    ("decoded", os.path.join(EXTERNAL_CONTENT, "decoded")),
    ("worlddecoded", os.path.join(EXTERNAL_CONTENT, "worlddecoded")),
    ("characters", os.path.join(EXTERNAL_CONTENT, "characters")),
    ("music", os.path.join(EXTERNAL_CONTENT, "music")),
]

# Extensions never executed here regardless (belt-and-suspenders parity
# with pk3_safety's own default deny-list); this tool still STAGES their
# bytes (the owning task explicitly wants cfg/script/qc kept as reference),
# it only refuses to ever run any of them, which it never does anyway.
_NEVER_EXECUTE_NOTE = (
    "Bytes only; this tool never imports, evals, or subprocesses archive content."
)


class PublishError(ValueError):
    pass


@dataclasses.dataclass
class FileRecord:
    pack: str                      # pack key, or "derived/<root>", or "notices"
    entry_name: str                # original ZIP member name, or source-relative path
    dest_relpath: str              # path under ThirdParty/Xonotic-0.8.6/
    type: str                      # "regular" | "symlink_resolved_copy" | "symlink_unresolved"
    sha256: Optional[str]
    size_bytes: Optional[int]
    source_pk3: Optional[str] = None
    source_pk3_sha256: Optional[str] = None
    hardlinked_from: Optional[str] = None
    compression_ratio_flagged: bool = False
    symlink_target_text: Optional[str] = None
    symlink_resolved_from: Optional[str] = None
    symlink_hops: Optional[List[str]] = None
    note: Optional[str] = None


def _sha256_file(path: str) -> str:
    return safety.sha256_file(path)


def _safe_write_new_file(dest_path_abs: str, dest_root_abs: str) -> Tuple[object, bool]:
    """Symlink-swap-safe, no-clobber file creation. Returns (fd, created).
    Raises PublishError (never overwrites, never follows a symlink)."""
    safety._safe_makedirs(os.path.dirname(dest_path_abs), dest_path_abs)  # noqa: SLF001 (deliberate reuse)
    flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY
    flags |= getattr(os, "O_NOFOLLOW", 0)
    try:
        fd = os.open(dest_path_abs, flags, 0o644)
    except FileExistsError:
        raise PublishError(f"Destination '{dest_path_abs}' already exists; refusing to overwrite.")
    except OSError as exc:
        if exc.errno == errno.ELOOP:
            raise PublishError(f"Destination '{dest_path_abs}' is a symlink; refusing.")
        raise
    return fd, True


def _stream_zip_entry(zf: zipfile.ZipFile, info: zipfile.ZipInfo, dest_path_abs: str) -> Tuple[int, str, bool]:
    """Streams one already-vetted, non-directory, non-symlink ZIP entry to
    dest_path_abs with the same bounded, chunked shape as
    pk3_safety.safe_extract_one()'s write loop. Returns (size, sha256).
    Removes any partial file and raises PublishError on CRC/size failure.

    If dest_path_abs already exists as a plain regular file (a previous,
    interrupted or partial `publish` run, or a deliberate re-run adding
    entries a bug had wrongly skipped before), this NEVER rewrites it:
    existing committed/staged bytes are load-bearing and must not mutate.
    It only re-hashes the existing file and returns its already-there size/
    sha256, so re-running `publish` stays idempotent and side-effect-free
    for files that already exist correctly."""
    if os.path.isfile(dest_path_abs) and not os.path.islink(dest_path_abs):
        return os.path.getsize(dest_path_abs), _sha256_file(dest_path_abs), False
    fd, _ = _safe_write_new_file(dest_path_abs, DEST_ROOT)
    h = hashlib.sha256()
    written = 0
    wrote_any = False
    try:
        with os.fdopen(fd, "wb") as dst, zf.open(info, "r") as src:
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > MAX_ENTRY_BYTES:
                    raise PublishError(
                        f"Entry '{info.filename}' exceeded {MAX_ENTRY_BYTES} bytes while streaming "
                        "(zip-bomb guard); aborted."
                    )
                wrote_any = True
                h.update(chunk)
                dst.write(chunk)
    except zipfile.BadZipFile as exc:
        if wrote_any or os.path.exists(dest_path_abs):
            try:
                os.remove(dest_path_abs)
            except OSError:
                pass
        raise PublishError(f"Entry '{info.filename}' failed CRC verification while streaming: {exc}") from exc
    except BaseException:
        if wrote_any or os.path.exists(dest_path_abs):
            try:
                os.remove(dest_path_abs)
            except OSError:
                pass
        raise
    return written, h.hexdigest(), True


def _dedupe_or_keep(dest_path_abs: str, sha256_hex: str, dedup: Dict[str, str],
                     freshly_written: bool = True) -> Optional[str]:
    """If sha256_hex was already written elsewhere under DEST_ROOT, deletes
    dest_path_abs and hardlinks it to the first copy instead, returning the
    relative path it was linked from. Returns None if this is the first
    copy of this content (dest_path_abs is kept as a real file).

    `freshly_written=False` means dest_path_abs already existed from a
    prior run (its bytes were only re-hashed, never rewritten this call);
    in that case this function ONLY registers it in `dedup` if it's the
    first copy of this hash seen so far — it never deletes/relinks a
    pre-existing file, so an idempotent re-run never mutates bytes that
    were already correct on disk, even to "optimize" them into a hardlink.
    """
    existing = dedup.get(sha256_hex)
    if existing is None:
        dedup[sha256_hex] = dest_path_abs
        return None
    if not freshly_written:
        return os.path.relpath(existing, DEST_ROOT) if existing != dest_path_abs else None
    os.remove(dest_path_abs)
    try:
        os.link(existing, dest_path_abs)
    except OSError:
        # Cross-device or filesystem without hardlink support: fall back to
        # a plain copy rather than failing the whole publish run.
        shutil.copyfile(existing, dest_path_abs)
    return os.path.relpath(existing, DEST_ROOT)


def _is_symlink_info(info: zipfile.ZipInfo) -> bool:
    mode = info.external_attr >> 16
    return bool(mode) and stat.S_ISLNK(mode)


def _resolve_symlink_chain(zf: zipfile.ZipFile, by_name: Dict[str, zipfile.ZipInfo],
                            start_name: str, duplicate_names: Optional[set] = None,
                            reports_by_name: Optional[Dict[str, "safety.EntryReport"]] = None
                            ) -> Tuple[Optional[zipfile.ZipInfo], List[str], Optional[str]]:
    """Follows a ZIP symlink entry's target text, relative to its own
    directory, up to MAX_SYMLINK_HOPS times. Returns
    (final_regular_info_or_None, hop_chain, failure_reason_or_None).
    Refuses escaping the archive root, refuses cycles, and refuses an
    unresolved/ambiguous target name.

    `by_name` is a plain `{filename: ZipInfo}` dict, which by construction
    silently keeps only the LAST entry for any name that appears more than
    once in a malformed/adversarial archive's central directory — exactly
    the "duplicate entry" ambiguity `pk3_safety.inspect_entries()` already
    flags. To avoid ever silently resolving through that collapsed,
    arbitrarily-chosen entry, callers MUST pass `duplicate_names` (the set
    of every canonical name that occurs more than once, from the single
    `inspect_entries()` pass already done for this archive): any hop whose
    name is in that set is refused as ambiguous, before `by_name` is even
    consulted for it. Callers should also pass `reports_by_name` (name ->
    that same pass's `EntryReport`) so the FINAL target is cross-checked
    against its own safety report (refusing an unsafe path, encrypted, or
    oversized final target), not just "does `by_name` happen to have it"."""
    duplicate_names = duplicate_names or set()
    reports_by_name = reports_by_name or {}
    hops: List[str] = [start_name]
    visited = {start_name}
    current_name = start_name
    for _ in range(MAX_SYMLINK_HOPS):
        if current_name in duplicate_names:
            return None, hops, f"'{current_name}' is an ambiguous/duplicate archive entry name"
        info = by_name.get(current_name)
        if info is None:
            return None, hops, f"'{current_name}' not found in archive"
        target_text = zf.read(info).decode("utf-8", errors="strict").strip()
        if not target_text or "\x00" in target_text:
            return None, hops, f"'{current_name}' has an empty/unsafe symlink target"
        base_dir = os.path.dirname(current_name)
        candidate = os.path.normpath(os.path.join(base_dir, target_text)).replace(os.sep, "/")
        if candidate.startswith("../") or candidate == ".." or candidate.startswith("/"):
            return None, hops, f"'{current_name}' -> '{target_text}' escapes the archive root"
        if candidate in visited:
            return None, hops, f"'{current_name}' -> '{candidate}' is a symlink cycle"
        visited.add(candidate)
        hops.append(candidate)
        if candidate in duplicate_names:
            return None, hops, f"target '{candidate}' is an ambiguous/duplicate archive entry name"
        next_info = by_name.get(candidate)
        if next_info is None:
            return None, hops, f"target '{candidate}' not found in archive"
        if next_info.is_dir():
            return None, hops, f"target '{candidate}' is a directory, not a plain file"
        if _is_symlink_info(next_info):
            current_name = candidate
            continue
        final_report = reports_by_name.get(candidate)
        if final_report is not None:
            hard_reasons = [r for r in final_report.reasons if "symlink entry" not in r and "compression ratio" not in r]
            if hard_reasons:
                return None, hops, f"target '{candidate}' fails safety inspection: {hard_reasons}"
        return next_info, hops, None
    return None, hops, "exceeded max symlink hop count"


def _pk3_sha256(pk3_path: str) -> str:
    return _sha256_file(pk3_path)


def _stage_pack(pack_key: str, pk3_filename: str, dedup: Dict[str, str],
                 skipped: List[dict]) -> List[FileRecord]:
    pk3_path = os.path.join(STAGING_PK3_DIR, pk3_filename)
    if not os.path.isfile(pk3_path) or os.path.islink(pk3_path):
        raise PublishError(f"Expected verified staged pack missing or not a plain file: {pk3_path}")
    pk3_sha256 = _pk3_sha256(pk3_path)
    dest_dir = os.path.join(DEST_ROOT, "packs", pack_key)
    records: List[FileRecord] = []

    # The "compression ratio" metadata-only zip-bomb heuristic may ONLY be
    # waived for these exact seven, already-verified official byte
    # sequences (see KNOWN_PACK_SHA256's docstring) — never for an
    # arbitrary/future archive, even one that happens to share a pinned
    # pack's filename. A mismatch here (tampering, a different release, a
    # caller passing something else entirely) is reported but the run
    # still proceeds with NO waiver for that archive; every ratio-flagged
    # entry in it is then hard-skipped like any other unverified input.
    expected_pk3_sha256 = KNOWN_PACK_SHA256.get(pk3_filename)
    is_pinned_verified_pack = expected_pk3_sha256 is not None and expected_pk3_sha256 == pk3_sha256
    if expected_pk3_sha256 is not None and not is_pinned_verified_pack:
        print(f"WARNING: '{pk3_filename}' sha256 {pk3_sha256} does not match the pinned known-good value "
              f"{expected_pk3_sha256}; NOT waiving the compression-ratio heuristic for this archive.",
              file=sys.stderr)

    with zipfile.ZipFile(pk3_path) as zf:
        reports = safety.inspect_entries(zf)  # single O(n) pass for this whole archive
        by_name: Dict[str, zipfile.ZipInfo] = {i.filename: i for i in zf.infolist()}
        reports_by_name: Dict[str, "safety.EntryReport"] = {r.name: r for r in reports}
        name_counts: Dict[str, int] = {}
        for r in reports:
            name_counts[r.name] = name_counts.get(r.name, 0) + 1
        duplicate_names = {n for n, c in name_counts.items() if c > 1}

        for report in reports:
            if report.is_dir:
                continue
            info = by_name[report.name]
            # "symlink entry" is handled below, separately, and never
            # triggers a hard skip on its own. "compression ratio" is only
            # excused from a hard skip when this archive's own bytes are
            # the pinned, already-verified official pack (see above) —
            # every flagged entry in one of the real seven packs is a
            # legitimately mostly-empty-mipmap DDS well under the absolute
            # per-entry/per-archive byte ceilings pk3_safety already
            # enforced in inspect_entries() above. Anything else (unsafe
            # path, encrypted, oversized, duplicate/ambiguous name, or a
            # ratio flag on an archive that is NOT a pinned-verified pack)
            # is a real hard skip.
            hard_reasons = [
                r for r in report.reasons
                if "symlink entry" not in r
                and not ("compression ratio" in r and is_pinned_verified_pack)
            ]
            if hard_reasons:
                skipped.append({"pack": pack_key, "entry": report.name, "reasons": hard_reasons})
                continue

            dest_relpath = os.path.join("packs", pack_key, report.name.replace("/", os.sep))
            dest_path_abs = os.path.normpath(os.path.join(DEST_ROOT, dest_relpath))
            if not (dest_path_abs == DEST_ROOT or dest_path_abs.startswith(DEST_ROOT + os.sep)):
                skipped.append({"pack": pack_key, "entry": report.name, "reasons": ["resolves outside dest root"]})
                continue

            ratio_flagged = any("compression ratio" in r for r in report.reasons)

            if _is_symlink_info(info):
                target_text = zf.read(info).decode("utf-8", errors="replace").strip()
                final_info, hops, failure = _resolve_symlink_chain(
                    zf, by_name, report.name, duplicate_names=duplicate_names, reports_by_name=reports_by_name)
                if final_info is None:
                    records.append(FileRecord(
                        pack=pack_key, entry_name=report.name, dest_relpath=dest_relpath,
                        type="symlink_unresolved", sha256=None, size_bytes=None,
                        source_pk3=pk3_filename, source_pk3_sha256=pk3_sha256,
                        symlink_target_text=target_text, symlink_hops=hops,
                        note=f"unresolved: {failure}",
                    ))
                    continue
                size, sha256_hex, freshly_written = _stream_zip_entry(zf, final_info, dest_path_abs)
                linked_from = _dedupe_or_keep(dest_path_abs, sha256_hex, dedup, freshly_written)
                records.append(FileRecord(
                    pack=pack_key, entry_name=report.name, dest_relpath=dest_relpath,
                    type="symlink_resolved_copy", sha256=sha256_hex, size_bytes=size,
                    source_pk3=pk3_filename, source_pk3_sha256=pk3_sha256,
                    hardlinked_from=linked_from, compression_ratio_flagged=ratio_flagged,
                    symlink_target_text=target_text, symlink_resolved_from=final_info.filename,
                    symlink_hops=hops, note=_NEVER_EXECUTE_NOTE,
                ))
                continue

            size, sha256_hex, freshly_written = _stream_zip_entry(zf, info, dest_path_abs)
            linked_from = _dedupe_or_keep(dest_path_abs, sha256_hex, dedup, freshly_written)
            records.append(FileRecord(
                pack=pack_key, entry_name=report.name, dest_relpath=dest_relpath,
                type="regular", sha256=sha256_hex, size_bytes=size,
                source_pk3=pk3_filename, source_pk3_sha256=pk3_sha256,
                hardlinked_from=linked_from, compression_ratio_flagged=ratio_flagged,
                note=_NEVER_EXECUTE_NOTE,
            ))
    return records


def _copy_plain_tree(root_key: str, src_root: str, dest_subdir: str, dedup: Dict[str, str],
                      skipped: List[dict]) -> List[FileRecord]:
    """Copies every plain regular file under src_root (no symlinks followed,
    no execution) into ThirdParty/Xonotic-0.8.6/<dest_subdir>/, hashing and
    deduping exactly like _stage_pack. Used for decoded/worlddecoded/
    characters/music/notices, which are already-materialized directory
    trees rather than ZIP archives."""
    records: List[FileRecord] = []
    if not os.path.isdir(src_root) or os.path.islink(src_root):
        return records
    dest_root_dir = os.path.join(DEST_ROOT, dest_subdir)
    for dirpath, dirnames, filenames in os.walk(src_root, followlinks=False):
        dirnames[:] = [d for d in dirnames if not os.path.islink(os.path.join(dirpath, d))]
        for name in filenames:
            src_path = os.path.join(dirpath, name)
            if os.path.islink(src_path):
                relpath = os.path.relpath(src_path, src_root).replace(os.sep, "/")
                skipped.append({"pack": root_key, "entry": relpath, "reasons": ["symlink in derived tree; not followed"]})
                continue
            if not os.path.isfile(src_path):
                continue
            relpath = os.path.relpath(src_path, src_root).replace(os.sep, "/")
            dest_relpath = os.path.join(dest_subdir, relpath.replace("/", os.sep))
            dest_path_abs = os.path.normpath(os.path.join(DEST_ROOT, dest_relpath))
            if not (dest_path_abs == DEST_ROOT or dest_path_abs.startswith(DEST_ROOT + os.sep)):
                skipped.append({"pack": root_key, "entry": relpath, "reasons": ["resolves outside dest root"]})
                continue
            already_present = os.path.isfile(dest_path_abs) and not os.path.islink(dest_path_abs)
            if already_present:
                # Idempotent re-run: never rewrite bytes already staged
                # correctly by a previous run. Only re-hash to record them.
                sha256_hex = _sha256_file(dest_path_abs)
                size = os.path.getsize(dest_path_abs)
                existing = dedup.get(sha256_hex)
                linked_from = (os.path.relpath(existing, DEST_ROOT)
                               if existing is not None and existing != dest_path_abs else None)
                if existing is None:
                    dedup[sha256_hex] = dest_path_abs
            else:
                safety._safe_makedirs(os.path.dirname(dest_path_abs), dest_path_abs)  # noqa: SLF001
                sha256_hex = _sha256_file(src_path)
                size = os.path.getsize(src_path)
                existing = dedup.get(sha256_hex)
                if existing is not None:
                    try:
                        os.link(existing, dest_path_abs)
                    except OSError:
                        shutil.copyfile(src_path, dest_path_abs)
                    linked_from = os.path.relpath(existing, DEST_ROOT)
                else:
                    shutil.copyfile(src_path, dest_path_abs)
                    dedup[sha256_hex] = dest_path_abs
                    linked_from = None
            records.append(FileRecord(
                pack=root_key, entry_name=relpath, dest_relpath=dest_relpath,
                type="regular", sha256=sha256_hex, size_bytes=size,
                hardlinked_from=linked_from, note="copied from an already-materialized ExternalContent tree",
            ))
    return records


def cmd_publish(args) -> int:
    if os.path.isdir(DEST_ROOT) and not args.overwrite:
        raise PublishError(
            f"'{DEST_ROOT}' already exists; pass --overwrite to re-publish into it "
            "(no existing file is ever silently overwritten either way)."
        )
    os.makedirs(DEST_ROOT, exist_ok=True)

    dedup: Dict[str, str] = {}
    skipped: List[dict] = []
    all_records: List[FileRecord] = []
    pack_summaries = []

    for pack_key, pk3_filename in PACKS:
        t0 = time.time()
        records = _stage_pack(pack_key, pk3_filename, dedup, skipped)
        all_records.extend(records)
        pack_summaries.append({
            "pack": pack_key,
            "source_pk3": pk3_filename,
            "files": len(records),
            "seconds": round(time.time() - t0, 1),
        })
        print(f"staged pack {pack_key}: {len(records)} files ({pack_summaries[-1]['seconds']}s)")

    for root_key, src_root in DERIVED_ROOTS:
        records = _copy_plain_tree(f"derived/{root_key}", src_root, os.path.join("derived", root_key), dedup, skipped)
        all_records.extend(records)
        print(f"staged derived/{root_key}: {len(records)} files")

    notice_records = _copy_plain_tree("notices", NOTICES_DIR, "notices", dedup, skipped)
    all_records.extend(notice_records)
    print(f"staged notices: {len(notice_records)} files")

    total_bytes = sum(r.size_bytes or 0 for r in all_records)
    oversized = [r for r in all_records if (r.size_bytes or 0) > MAX_COMMIT_FILE_BYTES]
    resolved_symlinks = [r for r in all_records if r.type == "symlink_resolved_copy"]
    unresolved_symlinks = [r for r in all_records if r.type == "symlink_unresolved"]
    hardlinked = [r for r in all_records if r.hardlinked_from]

    full_zip_path = os.path.join(EXTERNAL_CONTENT, "downloads", "xonotic-0.8.6.zip")
    full_zip_info = {
        "path": os.path.relpath(full_zip_path, REPO_ROOT).replace(os.sep, "/"),
        "sha512": KNOWN_FULL_ZIP_SHA512,
        "sha512_source": KNOWN_FULL_ZIP_SHA512_SOURCE,
        "rehashed_this_run": False,
    }
    if args.verify_full_zip:
        if not os.path.isfile(full_zip_path) or os.path.islink(full_zip_path):
            raise PublishError(
                f"--verify-full-zip requested but '{full_zip_path}' is missing or is a symlink; "
                "failing closed rather than silently skipping the check."
            )
        h = hashlib.sha512()
        with open(full_zip_path, "rb") as f:
            while True:
                chunk = f.read(4 * 1024 * 1024)
                if not chunk:
                    break
                h.update(chunk)
        actual = h.hexdigest()
        full_zip_info["rehashed_this_run"] = True
        full_zip_info["rehash_matches_known_value"] = (actual == KNOWN_FULL_ZIP_SHA512)
        if actual != KNOWN_FULL_ZIP_SHA512:
            full_zip_info["rehash_sha512_actual"] = actual
            # Fail CLOSED: a mismatch here means the on-disk download no
            # longer matches the value every other verified-fixity claim in
            # this repo is built on. This must abort the whole publish run
            # (never just annotate the manifest and continue as if nothing
            # happened), so no manifest is written and nothing is treated
            # as freshly re-verified for this run.
            raise PublishError(
                f"--verify-full-zip: '{full_zip_path}' SHA-512 {actual} does NOT match the known-good "
                f"value {KNOWN_FULL_ZIP_SHA512} (see docs/FULL-GAME-GATES.md); aborting, manifest not written."
            )

    manifest = {
        "tool": "publish_resources.py",
        "tool_version": TOOL_VERSION,
        "generated_at_unix": int(time.time()),
        "dest_root": os.path.relpath(DEST_ROOT, REPO_ROOT).replace(os.sep, "/"),
        "packs": pack_summaries,
        "totals": {
            "files": len(all_records),
            "bytes": total_bytes,
            "hardlinked_duplicates": len(hardlinked),
            "resolved_symlink_copies": len(resolved_symlinks),
            "unresolved_symlinks": len(unresolved_symlinks),
            "skipped_entries": len(skipped),
            "files_over_100mib": len(oversized),
        },
        "source_archive": full_zip_info,
        "oversized_files": [dataclasses.asdict(r) for r in oversized],
        "skipped": skipped,
        "files_list": [dataclasses.asdict(r) for r in all_records],
    }

    manifest_path = args.manifest_out or os.path.join(DEST_ROOT, "publish-manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"\nTOTAL files={len(all_records)} bytes={total_bytes}")
    print(f"resolved_symlink_copies={len(resolved_symlinks)} unresolved_symlinks={len(unresolved_symlinks)}")
    print(f"hardlinked_duplicates={len(hardlinked)} skipped_entries={len(skipped)} files_over_100MiB={len(oversized)}")
    print(f"manifest written: {manifest_path}")
    if oversized:
        print("WARNING: files over 100 MiB present (see manifest 'oversized_files'); "
              "review before a giant single commit.", file=sys.stderr)
    return 0


# Raw pack -> the actual git-ignored ExternalContent root XonoticContentResolver
# / BspImportPipeline / IqmWeaponImporter read today (see
# Assets/MyXonotic/Editor/Import/XonoticContentResolver.cs:51 and
# Assets/MyXonotic/Editor/Import/BspImportPipeline.cs:238):
#   ExternalContent/maps, ExternalContent/data (extracted upstream pk3s).
# Importers need the raw extracted BSP/model/texture members themselves,
# not only the already-converted derived/ PNGs, so these two packs restore
# unconditionally (not behind --with-pk3-entries). The remaining five packs
# (fonts, music, nexcompat, xoncompat) are not read from a bare extracted
# tree by any importer today and only restore with --with-pk3-entries.
_PACK_RAW_RESTORE_TARGETS = {
    "xonotic-20230620-data": os.path.join(EXTERNAL_CONTENT, "data"),
    "xonotic-20230620-maps": os.path.join(EXTERNAL_CONTENT, "maps"),
}
_PACK_OPTIONAL_RESTORE_TARGETS = {
    "font-unifont-20230620": os.path.join(STAGING_PK3_DIR, "_extracted", "font-unifont-20230620"),
    "font-xolonium-20230620": os.path.join(STAGING_PK3_DIR, "_extracted", "font-xolonium-20230620"),
    "xonotic-20230620-music": os.path.join(STAGING_PK3_DIR, "_extracted", "xonotic-20230620-music"),
    "xonotic-20230620-nexcompat": os.path.join(STAGING_PK3_DIR, "_extracted", "xonotic-20230620-nexcompat"),
    "xonotic-20230620-xoncompat": os.path.join(STAGING_PK3_DIR, "_extracted", "xonotic-20230620-xoncompat"),
}


def _ancestor_symlink_reason(path_abs: str, root_abs: str) -> Optional[str]:
    """Walks EVERY path component from root_abs down to path_abs (root
    itself, every intermediate directory, AND the final leaf if it
    exists), refusing if any of them is a symlink. Unlike checking only
    `os.path.islink(final_path)`, this also refuses a symlinked ANCESTOR
    directory silently redirecting a same-looking final path elsewhere
    (the classic "pre-create a symlinked directory" trick) — for both a
    read (source) and a not-yet-written target (whose leaf may not exist
    yet, but whose ancestor directories might already be a symlink).
    Returns None if every component is safe, else a reason string."""
    root_abs = os.path.normpath(root_abs)
    path_abs = os.path.normpath(path_abs)
    if not (path_abs == root_abs or path_abs.startswith(root_abs + os.sep)):
        return f"'{path_abs}' resolves outside its root '{root_abs}'"
    if os.path.islink(root_abs):
        return f"root '{root_abs}' is itself a symlink"
    rel = os.path.relpath(path_abs, root_abs)
    current = root_abs
    parts = [] if rel == "." else rel.split(os.sep)
    for part in parts:
        current = os.path.join(current, part)
        if os.path.islink(current):
            return f"path component '{current}' is a symlink"
    return None


def _safe_copy_verified(src_path_abs: str, target_path_abs: str, target_root_abs: str,
                         expected_sha256: str, entry_label: str, mismatches: List[dict]) -> Optional[str]:
    """Hardened restore-copy of one already-committed file:
      - refuses if ANY component of the source path (root, every
        intermediate directory, or the final leaf) or of the target path
        is a symlink — checked via `_ancestor_symlink_reason()` for BOTH
        paths before either is ever opened for reading, not merely
        `os.path.islink()` on the final component (which misses a
        symlinked ancestor directory);
      - refuses if src is outside DEST_ROOT or target is outside target_root_abs
        (containment, independent of whatever the manifest claims);
      - re-hashes the COMMITTED source and compares to the manifest's
        recorded sha256 before ever copying it anywhere (catches a
        corrupted/edited commit rather than faithfully reproducing it);
      - if target already exists: leaves it alone when its hash already
        matches, records a `mismatch` (never overwrites) when it differs;
      - writes the target with the same no-clobber, no-follow, symlink-
        swap-safe primitives publish() itself uses (`_safe_write_new_file` /
        `pk3_safety._safe_makedirs`), not `shutil.copyfile` (which would
        silently follow a pre-existing destination symlink).
    Returns None on success (or a legitimate immutable "already correct"
    no-op), or the mismatch reason string (already appended to
    `mismatches` by this function) on failure.
    """
    src_abs = os.path.normpath(src_path_abs)
    src_reason = _ancestor_symlink_reason(src_abs, DEST_ROOT)
    if src_reason is not None:
        reason = f"committed source path unsafe: {src_reason}"
        mismatches.append({"entry": entry_label, "reason": reason})
        return reason
    if not os.path.isfile(src_abs):
        reason = f"committed source missing: {os.path.relpath(src_abs, REPO_ROOT)}"
        mismatches.append({"entry": entry_label, "reason": reason})
        return reason

    target_abs = os.path.normpath(target_path_abs)
    target_root_abs = os.path.normpath(target_root_abs)
    target_reason = _ancestor_symlink_reason(target_abs, target_root_abs)
    if target_reason is not None:
        reason = f"restore target path unsafe: {target_reason}"
        mismatches.append({"entry": entry_label, "reason": reason})
        return reason

    actual_src_sha256 = _sha256_file(src_abs)
    if actual_src_sha256 != expected_sha256:
        reason = (f"committed source sha256 mismatch vs manifest "
                   f"(expected {expected_sha256}, got {actual_src_sha256}); refusing to propagate corrupted bytes")
        mismatches.append({"entry": entry_label, "reason": reason})
        return reason

    if os.path.lexists(target_abs):
        if os.path.islink(target_abs):
            reason = "existing restore target is a symlink; refusing (no-follow)"
            mismatches.append({"entry": entry_label, "reason": reason})
            return reason
        if not os.path.isfile(target_abs):
            reason = "existing restore target is not a regular file; refusing"
            mismatches.append({"entry": entry_label, "reason": reason})
            return reason
        existing_sha256 = _sha256_file(target_abs)
        if existing_sha256 == expected_sha256:
            return None  # already correct; never re-copy unchanged bytes
        reason = "existing restore target sha256 differs from committed bytes; not overwritten"
        mismatches.append({"entry": entry_label, "reason": reason})
        return reason

    try:
        safety._safe_makedirs(os.path.dirname(target_abs), target_abs)  # noqa: SLF001 (deliberate reuse)
        flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY | getattr(os, "O_NOFOLLOW", 0)
        fd = os.open(target_abs, flags, 0o644)
        with os.fdopen(fd, "wb") as dst, open(src_abs, "rb") as src:
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                dst.write(chunk)
    except safety.Pk3SafetyError as exc:
        mismatches.append({"entry": entry_label, "reason": str(exc)})
        return str(exc)
    except OSError as exc:
        mismatches.append({"entry": entry_label, "reason": f"write failed: {exc}"})
        return str(exc)
    return None


def cmd_restore(args) -> int:
    manifest_path = args.manifest or os.path.join(DEST_ROOT, "publish-manifest.json")
    if not os.path.isfile(manifest_path):
        raise PublishError(f"Manifest not found: {manifest_path} (run 'publish' first, or pass --manifest).")
    with open(manifest_path, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    derived_targets = {
        "decoded": os.path.join(EXTERNAL_CONTENT, "decoded"),
        "worlddecoded": os.path.join(EXTERNAL_CONTENT, "worlddecoded"),
        "characters": os.path.join(EXTERNAL_CONTENT, "characters"),
        "music": os.path.join(EXTERNAL_CONTENT, "music"),
    }

    restored = 0
    already_correct = 0
    mismatches: List[dict] = []
    skipped_no_bytes = 0
    skipped_optional = 0

    for rec in manifest["files_list"]:
        pack = rec["pack"]
        entry_label = f"{pack}:{rec['entry_name']}"

        if pack.startswith("derived/"):
            root_key = pack.split("/", 1)[1]
            target_root_abs = derived_targets.get(root_key)
            if target_root_abs is None:
                continue
        elif pack == "notices":
            target_root_abs = NOTICES_DIR
        elif pack in _PACK_RAW_RESTORE_TARGETS:
            target_root_abs = _PACK_RAW_RESTORE_TARGETS[pack]
        elif pack in _PACK_OPTIONAL_RESTORE_TARGETS:
            if not args.with_pk3_entries:
                skipped_optional += 1
                continue
            target_root_abs = _PACK_OPTIONAL_RESTORE_TARGETS[pack]
        else:
            continue

        if rec["type"] == "symlink_unresolved":
            skipped_no_bytes += 1
            continue
        if not safety._is_safe_relative_name(rec["entry_name"]):  # noqa: SLF001 (deliberate reuse)
            mismatches.append({"entry": entry_label, "reason": "entry_name failed path-safety check; refusing"})
            continue

        src_abs = os.path.normpath(os.path.join(DEST_ROOT, rec["dest_relpath"]))
        target_abs = os.path.normpath(os.path.join(target_root_abs, rec["entry_name"].replace("/", os.sep)))

        before_mismatches = len(mismatches)
        target_existed_correct = os.path.isfile(target_abs) and not os.path.islink(target_abs)
        result = _safe_copy_verified(src_abs, target_abs, target_root_abs, rec["sha256"], entry_label, mismatches)
        if len(mismatches) == before_mismatches:
            if target_existed_correct and result is None:
                already_correct += 1
            else:
                restored += 1

    print(f"restored={restored} already_correct={already_correct} mismatches={len(mismatches)} "
          f"skipped_no_bytes(unresolved symlinks)={skipped_no_bytes} skipped_optional_packs={skipped_optional}")
    for m in mismatches[:50]:
        print(f"  MISMATCH: {m['entry']}: {m['reason']}")
    if len(mismatches) > 50:
        print(f"  ... and {len(mismatches) - 50} more (see full list by rerunning with output captured)")
    return 0 if not mismatches else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="publish_resources.py",
        description="Stage real extracted Xonotic 0.8.6 pk3/derived bytes into ThirdParty/Xonotic-0.8.6/, "
                     "and restore them back into ignored ExternalContent/ working roots.",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_pub = sub.add_parser("publish", help="Extract+copy all seven packs and derived roots into ThirdParty/Xonotic-0.8.6/.")
    p_pub.add_argument("--overwrite", action="store_true", help="Allow publishing into an existing ThirdParty/Xonotic-0.8.6/ (still never overwrites an individual existing file).")
    p_pub.add_argument("--manifest-out", metavar="PATH", help="Manifest JSON destination (default: <dest>/publish-manifest.json).")
    p_pub.add_argument("--verify-full-zip", dest="verify_full_zip", action="store_true", default=False,
                        help="Re-hash the 1.2GB original download zip's SHA-512 and assert it still matches "
                             "the already-known-good value (default: reuse the known value, no rehash).")
    p_pub.set_defaults(func=cmd_publish)

    p_res = sub.add_parser("restore", help="Materialize committed derived/ and notices bytes back into ExternalContent/ (no download).")
    p_res.add_argument("--manifest", metavar="PATH", help="Manifest JSON to restore from (default: <dest>/publish-manifest.json).")
    p_res.add_argument("--with-pk3-entries", action="store_true",
                        help="Also restore the five packs no importer reads from a bare extracted tree today "
                             "(fonts, music, nexcompat, xoncompat) into ExternalContent/staging_pk3/.../_extracted/<pack>/. "
                             "xonotic-20230620-data -> ExternalContent/data and xonotic-20230620-maps -> "
                             "ExternalContent/maps always restore (importers require them).")
    p_res.set_defaults(func=cmd_restore)

    return parser


def main(argv=None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (PublishError, safety.Pk3SafetyError, OSError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
