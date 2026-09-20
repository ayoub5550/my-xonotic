#!/usr/bin/env python3
"""
pk3_tool.py — safe, read-mostly intake for Xonotic .pk3 (ZIP) archives.

Subcommands:
  inventory   List every entry in a .pk3 with size/ratio/safety flags and a
              whole-archive SHA256, without extracting anything.
  extract     Extract ONE named entry (typically a .bsp) to an output
              directory, after re-checking every safety rule, with a
              provenance report (SHA256 of source archive + extracted file).
  provenance  Recompute/verify a SHA256 provenance record for a file.
  fixture     Write the synthetic, wholly-original test .bsp fixtures used
              by tests/ (never derived from any Xonotic/Quake asset).

Design notes:
  - Stdlib only (zipfile/hashlib/argparse/json). No network access, no
    third-party packages, no execution of archive content.
  - Default output directories are under ExternalContent/, which is
    .gitignore'd at the project root — nothing this tool extracts is
    intended to be committed. Recommended distribution-review discipline:
    keep upstream Xonotic content (GPL, art-asset licences, etc.) entirely
    out of this MIT-licensed Git repository; see AGENTS.md.
"""
from __future__ import annotations

import argparse
import dataclasses
import errno
import json
import os
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pk3_safety as safety  # noqa: E402
import fixture_bsp  # noqa: E402

def _write_new_file_exclusive(path: str, data: bytes) -> None:
    """Writes `data` to `path`, refusing to overwrite or follow an existing
    symlink at that path (exclusive, no-clobber, no-follow creation)."""
    flags = os.O_CREAT | os.O_EXCL | os.O_WRONLY
    flags |= getattr(os, "O_NOFOLLOW", 0)
    try:
        fd = os.open(path, flags, 0o644)
    except FileExistsError:
        raise safety.Pk3SafetyError(f"'{path}' already exists; refusing to overwrite (no-clobber).")
    except OSError as exc:
        if exc.errno == errno.ELOOP:
            raise safety.Pk3SafetyError(f"'{path}' is a symlink; refusing (no-follow).")
        raise
    with os.fdopen(fd, "wb") as f:
        f.write(data)


DEFAULT_EXTERNAL_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "ExternalContent",
)


def cmd_inventory(args: argparse.Namespace) -> int:
    if not os.path.isfile(args.pk3):
        print(f"error: no such file: {args.pk3}", file=sys.stderr)
        return 2

    archive_sha256 = safety.sha256_file(args.pk3)
    report = {
        "archive_path": os.path.abspath(args.pk3),
        "archive_sha256": archive_sha256,
        "archive_size_bytes": os.path.getsize(args.pk3),
        "crc_verified": False,
        "crc_verification_note": (
            "Inventory reads central-directory metadata only and never decompresses entries "
            "(ZipFile.testzip() is intentionally NOT called here, since doing so before size/ratio "
            "limits are enforced would fully decompress a potential zip bomb just to 'check' it). "
            "CRC32 is verified only for the single entry chosen by the 'extract' subcommand, which "
            "streams that one entry under the same byte-budget guard used for the size cap."
        ),
        "entries": [],
    }

    try:
        with zipfile.ZipFile(args.pk3, "r") as zf:
            entries = safety.inspect_entries(zf)
    except safety.Pk3SafetyError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3
    except zipfile.BadZipFile as exc:
        print(f"error: not a valid zip/pk3 archive: {exc}", file=sys.stderr)
        return 3

    bsp_entries = []
    suspicious_entries = []
    for e in entries:
        d = dataclasses.asdict(e)
        report["entries"].append(d)
        if e.name.lower().endswith(".bsp"):
            bsp_entries.append(e.name)
        if e.is_suspicious:
            suspicious_entries.append(d)

    report["bsp_entries"] = bsp_entries
    report["suspicious_entry_count"] = len(suspicious_entries)

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        print(f"Archive: {report['archive_path']}")
        print(f"SHA256:  {archive_sha256}")
        print(f"Size:    {report['archive_size_bytes']} bytes, {len(entries)} entries")
        print(f"BSP maps found ({len(bsp_entries)}): " + (", ".join(bsp_entries) if bsp_entries else "(none)"))
        if suspicious_entries:
            print(f"\n/!\\ {len(suspicious_entries)} suspicious entr{'y' if len(suspicious_entries)==1 else 'ies'}:")
            for d in suspicious_entries:
                print(f"  - {d['name']}: {', '.join(d['reasons'])}")
        else:
            print("No suspicious entries detected.")

    if args.report_out:
        os.makedirs(os.path.dirname(os.path.abspath(args.report_out)) or ".", exist_ok=True)
        _write_new_file_exclusive(args.report_out, json.dumps(report, indent=2).encode("utf-8"))

    return 0 if not suspicious_entries else 1


def cmd_extract(args: argparse.Namespace) -> int:
    if not os.path.isfile(args.pk3):
        print(f"error: no such file: {args.pk3}", file=sys.stderr)
        return 2

    out_dir = args.out_dir or DEFAULT_EXTERNAL_DIR
    os.makedirs(out_dir, exist_ok=True)
    archive_sha256 = safety.sha256_file(args.pk3)

    try:
        with zipfile.ZipFile(args.pk3, "r") as zf:
            dest_path = safety.safe_extract_one(
                zf, args.entry, out_dir, allow_scripts=args.allow_scripts, verify_crc=not args.skip_crc_check,
            )
    except safety.Pk3SafetyError as exc:
        print(f"error: refused to extract '{args.entry}': {exc}", file=sys.stderr)
        return 3
    except zipfile.BadZipFile as exc:
        print(f"error: not a valid zip/pk3 archive: {exc}", file=sys.stderr)
        return 3

    entry_sha256 = safety.sha256_file(dest_path)
    provenance = {
        "source_archive": os.path.abspath(args.pk3),
        "source_archive_sha256": archive_sha256,
        "entry_name": args.entry,
        "extracted_path": dest_path,
        "extracted_sha256": entry_sha256,
        "extracted_size_bytes": os.path.getsize(dest_path),
        "note": "Extracted content is third-party (Xonotic/community) unless proven otherwise; "
                "keep out of Git and review licence/distribution terms before shipping.",
    }

    prov_path = dest_path + ".provenance.json"
    try:
        _write_new_file_exclusive(prov_path, json.dumps(provenance, indent=2).encode("utf-8"))
    except (OSError, safety.Pk3SafetyError):
        # No successful extraction without its matching provenance record.
        os.remove(dest_path)
        raise

    print(f"Extracted: {dest_path}")
    print(f"SHA256:    {entry_sha256}")
    print(f"Provenance report: {prov_path}")
    return 0


def cmd_provenance(args: argparse.Namespace) -> int:
    if not os.path.isfile(args.path):
        print(f"error: no such file: {args.path}", file=sys.stderr)
        return 2
    digest = safety.sha256_file(args.path)
    record = {
        "path": os.path.abspath(args.path),
        "size_bytes": os.path.getsize(args.path),
        "sha256": digest,
    }
    if args.expect_sha256 and args.expect_sha256.lower() != digest.lower():
        print(json.dumps(record, indent=2))
        print(f"error: SHA256 mismatch: expected {args.expect_sha256}, got {digest}", file=sys.stderr)
        return 1
    print(json.dumps(record, indent=2))
    return 0


def cmd_fixture(args: argparse.Namespace) -> int:
    out_dir = args.out_dir
    os.makedirs(out_dir, exist_ok=True)

    files = {
        "good.bsp": fixture_bsp.build_fixture_bsp(),
        "shader_asymmetry.bsp": fixture_bsp.build_shader_asymmetry_bsp(),
        "bad_magic.bsp": fixture_bsp.build_bad_magic(),
        "unsupported_version.bsp": fixture_bsp.build_unsupported_version(),
        "truncated_header.bsp": fixture_bsp.build_truncated_header(),
        "huge_lump_attack.bsp": fixture_bsp.build_huge_lump_count_attack(),
        "nan_vertex_attack.bsp": fixture_bsp.build_nan_vertex_attack(),
    }
    for name, data in files.items():
        path = os.path.join(out_dir, name)
        with open(path, "wb") as f:
            f.write(data)
        print(f"wrote {path} ({len(data)} bytes, sha256={safety.sha256_bytes(data)})")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pk3_tool.py",
        description="Safe inventory/extraction of Xonotic .pk3 archives (BSP-focused).",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    p_inv = sub.add_parser("inventory", help="List and safety-check every entry in a .pk3, without extracting.")
    p_inv.add_argument("pk3", help="Path to the .pk3 (ZIP) archive.")
    p_inv.add_argument("--json", action="store_true", help="Print the full report as JSON instead of a summary.")
    p_inv.add_argument("--report-out", metavar="PATH", help="Also write the full JSON report to PATH.")
    p_inv.set_defaults(func=cmd_inventory)

    p_ext = sub.add_parser("extract", help="Extract one entry (e.g. a .bsp) from a .pk3, with safety checks + provenance report.")
    p_ext.add_argument("pk3", help="Path to the .pk3 (ZIP) archive.")
    p_ext.add_argument("entry", help="Exact entry name inside the archive to extract (see 'inventory').")
    p_ext.add_argument("--out-dir", metavar="DIR", help=f"Destination directory (default: {DEFAULT_EXTERNAL_DIR}, git-ignored).")
    p_ext.add_argument("--allow-scripts", action="store_true", help="Allow extracting script/executable-looking extensions (still never executed).")
    p_ext.add_argument("--skip-crc-check", action="store_true", help="Skip post-extraction CRC verification (not recommended).")
    p_ext.set_defaults(func=cmd_extract)

    p_prov = sub.add_parser("provenance", help="Compute (and optionally verify) a SHA256 provenance record for a file.")
    p_prov.add_argument("path", help="File to hash.")
    p_prov.add_argument("--expect-sha256", metavar="HEX", help="Fail if the computed SHA256 does not match this value.")
    p_prov.set_defaults(func=cmd_provenance)

    p_fix = sub.add_parser("fixture", help="Write the synthetic, original test .bsp fixtures (no upstream content).")
    p_fix.add_argument("--out-dir", metavar="DIR", default="tests/fixtures/generated", help="Output directory (default: %(default)s).")
    p_fix.set_defaults(func=cmd_fixture)

    return parser


def main(argv=None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        return args.func(args)
    except (OSError, safety.Pk3SafetyError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
