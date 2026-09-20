"""
Shared safety checks for reading Quake/Xonotic .pk3 archives (which are
plain ZIP files). Stdlib only, no third-party dependencies.

Threat model this module defends against:
  - path traversal / absolute-path / drive-letter entries
    ("../../etc/passwd", "/etc/x", "C:\\Windows\\x", dot-segments)
  - zip bombs (one tiny compressed entry expanding to gigabytes) — inventory
    NEVER decompresses to check this; see inspect_entries() docstring
  - pre-existing symlinks in the destination tree used to redirect a write
    outside the intended output directory ("symlink swap")
  - clobbering an existing output file
  - ambiguous/duplicate zip entries with the same name (classic zip
    "confusion" trick) and encrypted entries (which we cannot inspect
    without a password and must not blindly decrypt/execute)
  - anything that would make us *execute* archive content: this module
    never imports, evals, or subprocesses anything found inside a PK3; it
    only reads bytes.

Known, accepted limitation: all checks are best-effort against a
*local, single-actor* filesystem. There is no protection against another
process concurrently swapping a directory for a symlink between our check
and our write (TOCTOU); this tool is not designed to run against a
destination directory that an untrusted party can modify concurrently.
"""
from __future__ import annotations

import dataclasses
import errno
import hashlib
import os
import stat
import zipfile
from typing import List

# Generous but finite ceilings. A legitimate Xonotic map pk3 is a few MB to
# a few hundred MB (large texture packs); these bounds are chosen to reject
# obviously hostile ratios/sizes rather than to model a "typical" map pack.
MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES = 512 * 1024 * 1024  # 512 MiB
MAX_TOTAL_UNCOMPRESSED_BYTES = 4 * 1024 * 1024 * 1024  # 4 GiB
MAX_COMPRESSION_RATIO = 200  # uncompressed / compressed, per entry, from metadata only
MAX_ENTRY_COUNT = 200_000

# Extensions we refuse to extract by default because they are executable or
# script-like on common platforms/engines. Data files (.bsp/.pk3/.tga/...)
# are unaffected. --allow-scripts permits extracting those bytes, never
# executing them. Inventory lists them regardless of the extraction policy.
DEFAULT_BLOCKED_EXTRACT_EXTENSIONS = {
    ".exe", ".dll", ".so", ".dylib", ".bat", ".cmd", ".sh", ".ps1",
    ".qc", ".dat", ".py", ".pl", ".jar", ".vbs", ".scr",
}

# ZIP general-purpose bit flag 0 = "entry is encrypted".
_ZIP_ENCRYPTED_FLAG = 0x1


class Pk3SafetyError(ValueError):
    """Raised for any archive entry or overall archive that fails a safety check."""


@dataclasses.dataclass
class EntryReport:
    name: str
    compressed_size: int
    uncompressed_size: int
    compression_ratio: float
    is_dir: bool
    is_suspicious: bool
    reasons: list


def sha256_file(path: str, chunk_size: int = 1024 * 1024) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            chunk = f.read(chunk_size)
            if not chunk:
                break
            h.update(chunk)
    return h.hexdigest()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _is_symlink_entry(info: zipfile.ZipInfo) -> bool:
    mode = info.external_attr >> 16
    return bool(mode) and stat.S_ISLNK(mode) if mode else False


def _is_encrypted_entry(info: zipfile.ZipInfo) -> bool:
    return bool(info.flag_bits & _ZIP_ENCRYPTED_FLAG)


def _is_safe_relative_name(name: str) -> bool:
    """Rejects absolute paths, drive letters, NUL bytes, empty/'.'/'..'
    path components, on both POSIX and Windows conventions. Deliberately
    does not rely on pathlib normalization: components are inspected
    exactly as split, before any dot-segment collapsing could hide a
    traversal attempt from a naive check.
    """
    if not name:
        return False
    if "\x00" in name:
        return False
    # Absolute paths on POSIX ("/x") or Windows ("\x", "\\x").
    if name.startswith("/") or name.startswith("\\"):
        return False
    # Drive-letter / UNC-style prefixes: "C:\...", "C:/...", "C:foo".
    # A ':' anywhere in the name is never valid in a legitimate archive
    # member path and is only useful to smuggle a Windows drive reference.
    if ":" in name:
        return False
    normalized = name.replace("\\", "/")
    parts = normalized.split("/")
    for part in parts:
        if part in ("", ".", ".."):
            return False
        # Windows aliases, device files and NTFS stream syntax are not
        # portable archive names, even when this tool runs on Linux.
        if part.endswith((" ", ".")) or any(ord(c) < 32 or c in '<>"|?*' for c in part):
            return False
        stem = part.split(".", 1)[0].upper()
        if stem in {"CON", "PRN", "AUX", "NUL"} or stem in {
            f"{prefix}{n}" for prefix in ("COM", "LPT") for n in range(1, 10)
        }:
            return False
    return True


def _canonical_name(name: str) -> str:
    return name.replace("\\", "/").casefold()


def inspect_entries(zf: zipfile.ZipFile) -> List[EntryReport]:
    """Read-only pass producing a safety report for every entry, from
    central-directory metadata alone.

    IMPORTANT: this function never calls ZipFile.testzip() (or any other
    API that decompresses entries) — doing so before size/ratio limits are
    enforced would itself decompress a potential zip bomb in full just to
    "check" it. CRC verification therefore only ever happens for the one
    entry actually selected by safe_extract_one(), which streams that
    single entry through the same byte budget used for the size cap. This
    function's `is_suspicious`/reasons are computed purely from metadata
    (name, compressed/uncompressed size, flags) and do not prove archive
    integrity.
    """
    infos = zf.infolist()
    if len(infos) > MAX_ENTRY_COUNT:
        raise Pk3SafetyError(
            f"Archive has {len(infos)} entries, exceeding the safety ceiling of {MAX_ENTRY_COUNT}."
        )

    reports = []
    total_uncompressed = 0
    for info in infos:
        reasons = []
        is_dir = info.is_dir() or info.filename.endswith("/")
        path_to_check = info.filename[:-1] if is_dir else info.filename
        if not _is_safe_relative_name(path_to_check):
            reasons.append("unsafe path (absolute, drive-letter, or traversal)")
        if _is_symlink_entry(info):
            reasons.append("symlink entry")
        if _is_encrypted_entry(info):
            reasons.append("encrypted entry (cannot be inspected/extracted without a password)")
        if info.file_size > MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES:
            reasons.append(
                f"uncompressed size {info.file_size} exceeds per-entry ceiling {MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES}"
            )
        ratio = (info.file_size / info.compress_size) if info.compress_size > 0 else (
            float("inf") if info.file_size > 0 else 1.0
        )
        if ratio > MAX_COMPRESSION_RATIO:
            reasons.append(f"compression ratio {ratio:.1f}x exceeds ceiling {MAX_COMPRESSION_RATIO}x (possible zip bomb, from metadata only)")

        total_uncompressed += info.file_size
        reports.append(
            EntryReport(
                name=info.filename,
                compressed_size=info.compress_size,
                uncompressed_size=info.file_size,
                compression_ratio=ratio,
                is_dir=is_dir,
                is_suspicious=bool(reasons),
                reasons=reasons,
            )
        )

    if total_uncompressed > MAX_TOTAL_UNCOMPRESSED_BYTES:
        raise Pk3SafetyError(
            f"Archive's total uncompressed size {total_uncompressed} exceeds the safety ceiling {MAX_TOTAL_UNCOMPRESSED_BYTES}."
        )

    # Duplicate/ambiguous names: two central-directory records for the same
    # literal path is a well-known confusion trick (different tools may
    # resolve "which one wins" differently). Flag them so callers can
    # decide; safe_extract_one() hard-refuses to extract an ambiguous name.
    seen = {}
    for r in reports:
        key = _canonical_name(r.name)
        seen[key] = seen.get(key, 0) + 1
    for r in reports:
        count = seen[_canonical_name(r.name)]
        if count > 1:
            r.reasons.append(f"duplicate entry name ({count} canonical occurrences; ambiguous)")
            r.is_suspicious = True

    return reports


def _ensure_no_symlink_containment_break(dest_dir_abs: str, dest_path_abs: str) -> None:
    """Walks every directory component between dest_dir_abs and the parent
    of dest_path_abs and refuses if any of them is a symlink. This blocks
    the classic "pre-create a symlinked directory, then have the archive
    'extract into' it" trick, regardless of what the symlink points to.
    """
    if os.path.islink(dest_dir_abs):
        raise Pk3SafetyError(f"Destination directory '{dest_dir_abs}' is itself a symlink; refusing.")

    rel = os.path.relpath(dest_path_abs, dest_dir_abs)
    parts = rel.split(os.sep)
    current = dest_dir_abs
    for part in parts[:-1]:  # exclude the final filename component
        current = os.path.join(current, part)
        if os.path.islink(current):
            raise Pk3SafetyError(
                f"Path component '{current}' is a symlink; refusing to extract through it (symlink-swap protection)."
            )
        if os.path.exists(current) and not os.path.isdir(current):
            raise Pk3SafetyError(f"Path component '{current}' exists and is not a directory; refusing.")


def _safe_makedirs(dest_dir_abs: str, dest_path_abs: str) -> None:
    """Like os.makedirs(..., exist_ok=True) but refuses to traverse or
    silently reuse any symlinked directory component."""
    _ensure_no_symlink_containment_break(dest_dir_abs, dest_path_abs)
    os.makedirs(dest_dir_abs, exist_ok=True)
    parent = os.path.dirname(dest_path_abs)
    rel = os.path.relpath(parent, dest_dir_abs)
    current = dest_dir_abs
    if rel != ".":
        for part in rel.split(os.sep):
            current = os.path.join(current, part)
            if os.path.islink(current):
                raise Pk3SafetyError(f"Path component '{current}' is a symlink; refusing.")
            if not os.path.exists(current):
                os.mkdir(current, 0o755)
            elif not os.path.isdir(current):
                raise Pk3SafetyError(f"Path component '{current}' exists and is not a directory; refusing.")


def safe_extract_one(zf: zipfile.ZipFile, entry_name: str, dest_dir: str,
                      allow_scripts: bool = False, verify_crc: bool = True) -> str:
    """Extracts exactly one entry after re-checking every safety rule.
    Refuses directory entries, unsafe/ambiguous names, symlinks, encrypted
    entries, oversized/zip-bomb entries, script/executable-looking
    extensions (by default), pre-existing destination files (no-clobber),
    and any symlinked path component in the destination tree. On any
    failure during the write (CRC mismatch, byte-budget overrun), the
    partially written file is removed. Returns the destination path.
    """
    # Enforce archive-wide entry/count limits even when only one file is requested.
    inspect_entries(zf)
    canonical = _canonical_name(entry_name)
    matches = [i for i in zf.infolist() if _canonical_name(i.filename) == canonical]
    if not matches:
        raise Pk3SafetyError(f"Entry '{entry_name}' not found in archive.")
    if len(matches) > 1:
        raise Pk3SafetyError(
            f"Entry '{entry_name}' appears {len(matches)} times in the archive's central directory "
            "(ambiguous/ill-formed archive); refusing to extract."
        )
    info = matches[0]
    if info.filename != entry_name:
        raise Pk3SafetyError("Use the exact, case-sensitive member name reported by inventory.")

    if info.is_dir() or entry_name.endswith("/"):
        raise Pk3SafetyError("Refusing to extract a directory entry.")
    if not _is_safe_relative_name(entry_name):
        raise Pk3SafetyError(f"Entry '{entry_name}' has an unsafe path; refusing to extract.")
    if _is_symlink_entry(info):
        raise Pk3SafetyError(f"Entry '{entry_name}' is a symlink; refusing to extract.")
    if _is_encrypted_entry(info):
        raise Pk3SafetyError(f"Entry '{entry_name}' is encrypted; refusing to extract without a password.")
    if info.file_size > MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES:
        raise Pk3SafetyError(
            f"Entry '{entry_name}' uncompressed size {info.file_size} exceeds the safety ceiling."
        )
    ratio = (info.file_size / info.compress_size) if info.compress_size > 0 else (
        float("inf") if info.file_size > 0 else 1.0
    )
    if ratio > MAX_COMPRESSION_RATIO:
        raise Pk3SafetyError(
            f"Entry '{entry_name}' compression ratio {ratio:.1f}x exceeds the safety ceiling (possible zip bomb)."
        )

    ext = os.path.splitext(entry_name)[1].lower()
    if not allow_scripts and ext in DEFAULT_BLOCKED_EXTRACT_EXTENSIONS:
        raise Pk3SafetyError(
            f"Entry '{entry_name}' has extension '{ext}' which looks executable/script-like; "
            "refusing to extract without --allow-scripts. This tool never executes archive content regardless."
        )

    dest_path = os.path.normpath(os.path.join(dest_dir, entry_name.replace("\\", "/")))
    dest_dir_abs = os.path.abspath(dest_dir)
    dest_path_abs = os.path.abspath(dest_path)
    if not (dest_path_abs == dest_dir_abs or dest_path_abs.startswith(dest_dir_abs + os.sep)):
        raise Pk3SafetyError(f"Entry '{entry_name}' resolves outside the destination directory; refusing.")

    _safe_makedirs(dest_dir_abs, dest_path_abs)

    # Exclusive, no-follow, no-clobber creation: fails if anything (file,
    # symlink, or directory) already exists at dest_path_abs. O_NOFOLLOW is
    # POSIX-only (absent on Windows, where O_EXCL alone already refuses to
    # open through a reparse point/symlink target for a new file).
    open_flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY
    open_flags |= getattr(os, "O_NOFOLLOW", 0)
    try:
        fd = os.open(dest_path_abs, open_flags, 0o644)
    except FileExistsError:
        raise Pk3SafetyError(f"Destination '{dest_path_abs}' already exists; refusing to overwrite (no-clobber).")
    except OSError as exc:
        if exc.errno == errno.ELOOP:
            raise Pk3SafetyError(f"Destination '{dest_path_abs}' is a symlink; refusing (no-follow).")
        raise

    wrote_any = False
    try:
        with os.fdopen(fd, "wb") as dst, zf.open(info, "r") as src:
            written = 0
            while True:
                chunk = src.read(1024 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES:
                    raise Pk3SafetyError(
                        f"Entry '{entry_name}' produced more than {MAX_SINGLE_ENTRY_UNCOMPRESSED_BYTES} bytes "
                        "while extracting; aborted (zip-bomb guard)."
                    )
                wrote_any = True
                dst.write(chunk)

        if verify_crc:
            actual_crc = _crc32_of_file(dest_path_abs)
            if actual_crc != info.CRC:
                raise Pk3SafetyError(
                    f"Entry '{entry_name}' failed CRC verification after extraction "
                    f"(expected {info.CRC:#010x}, got {actual_crc:#010x})."
                )
    except zipfile.BadZipFile as exc:
        if os.path.exists(dest_path_abs) or wrote_any:
            try:
                os.remove(dest_path_abs)
            except OSError:
                pass
        raise Pk3SafetyError(f"Entry '{entry_name}' failed CRC verification while streaming: {exc}") from exc
    except BaseException:
        if os.path.exists(dest_path_abs) or wrote_any:
            try:
                os.remove(dest_path_abs)
            except OSError:
                pass
        raise

    return dest_path_abs


def _crc32_of_file(path: str) -> int:
    import zlib
    crc = 0
    with open(path, "rb") as f:
        while True:
            chunk = f.read(1024 * 1024)
            if not chunk:
                break
            crc = zlib.crc32(chunk, crc)
    return crc & 0xFFFFFFFF
