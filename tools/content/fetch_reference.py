#!/usr/bin/env python3
"""Fetch two fixed official validation maps with bounded HTTPS byte ranges.

Optional, never called by Unity or a build. Does NOT verify the whole release
SHA512: extracted-member CRC and SHA256 provenance are reported separately.
No downloaded content is executed or licensed for redistribution by this tool.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import urllib.request
import zipfile

URL = "https://dl.xonotic.org/xonotic-0.8.6.zip"
ARCHIVE_BYTES = 1238439495
MAP_ARCHIVE = "Xonotic/data/xonotic-20230620-maps.pk3"
EXPECTED = {
    "maps/_hudsetup.bsp": "d358a3cd897a13f322e271423a6039188026a117082664ba3b3ccde547a561e8",
    "maps/boil.bsp": "5edd1224e89eb9d31cfd8a05cb532546d6f5a43e438078f25234b8f2011093fb",
}
MAX_TRANSFER = 16 * 1024 * 1024


class RangeReader(io.RawIOBase):
    def __init__(self, budget, start=0, length=ARCHIVE_BYTES):
        self.start, self.length, self.position = start, length, 0
        self.budget = budget

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.position

    def seek(self, offset, whence=0):
        if whence not in (0, 1, 2):
            raise ValueError("Invalid seek origin")
        pos = offset if whence == 0 else self.position + offset if whence == 1 else self.length + offset
        if pos < 0 or pos > self.length:
            raise ValueError("Seek outside bounded archive")
        self.position = pos
        return pos

    def read(self, size=-1):
        count = min(self.length - self.position, size if size >= 0 else self.length)
        if count <= 0:
            return b""
        if count > 8 * 1024 * 1024 or self.budget[0] + count > MAX_TRANSFER:
            raise ValueError("Bounded transfer budget exceeded")
        start = self.start + self.position
        if start + count > ARCHIVE_BYTES:
            raise ValueError("Nested archive outside release")
        request = urllib.request.Request(URL, headers={
            "Range": f"bytes={start}-{start + count - 1}",
            "User-Agent": "my-xonotic-local-reference/0.1",
            "Accept-Encoding": "identity",
        })
        with urllib.request.urlopen(request, timeout=45) as response:
            expected = f"bytes {start}-{start + count - 1}/{ARCHIVE_BYTES}"
            if response.status != 206 or response.headers.get("Content-Range") != expected:
                raise ValueError("Server did not honor the exact byte range; full download refused")
            data = response.read(count + 1)
        if len(data) != count:
            raise ValueError("Range length mismatch")
        self.position += count
        self.budget[0] += count
        return data


def read_member(archive, member):
    info = archive.getinfo(member)
    if info.file_size > 4 * 1024 * 1024 or info.flag_bits & 1:
        raise ValueError("Unexpected/encrypted reference member")
    with archive.open(info) as source:
        data = source.read(4 * 1024 * 1024 + 1)
    if len(data) != info.file_size or len(data) > 4 * 1024 * 1024:
        raise ValueError("Member size mismatch")
    return data, info


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out-dir", type=Path,
                        default=Path(__file__).resolve().parents[2] / "ExternalContent/reference")
    args = parser.parse_args()
    output = args.out_dir.resolve()
    if output.exists():
        parser.error("Choose a NEW output directory (no-clobber).")
    budget = [0]
    payloads = {}
    report = {
        "source": URL, "release": "0.8.6", "archive_bytes": ARCHIVE_BYTES,
        "retrieval": "HTTPS byte ranges; extracted-member CRC verified",
        "whole_release_sha512_verified": False,
        "note": "Local parser-validation copy; upstream licences apply. Reviewed originals/source are separately indexed in ThirdParty/Xonotic. No automatic APK inclusion.",
        "files": [],
    }
    outer_reader = RangeReader(budget)
    with zipfile.ZipFile(outer_reader) as outer:
        copying, _ = read_member(outer, "Xonotic/COPYING")
        payloads["release-COPYING.txt"] = copying
        nested_info = outer.getinfo(MAP_ARCHIVE)
        if nested_info.compress_type != zipfile.ZIP_STORED or nested_info.flag_bits & 1:
            raise ValueError("Nested archive must be stored and unencrypted")
        outer_reader.seek(nested_info.header_offset)
        header = outer_reader.read(30)
        if header[:4] != b"PK\x03\x04":
            raise ValueError("Invalid local ZIP header")
        name_len, extra_len = struct.unpack_from("<HH", header, 26)
        nested_start = nested_info.header_offset + 30 + name_len + extra_len
        if nested_start + nested_info.file_size > ARCHIVE_BYTES:
            raise ValueError("Nested archive range exceeds release")
        nested_reader = RangeReader(budget, nested_start, nested_info.file_size)
        with zipfile.ZipFile(nested_reader) as maps:
            report["bsp_members_in_maps_archive"] = sum(i.filename.endswith(".bsp") for i in maps.infolist())
            for member, expected in EXPECTED.items():
                data, info = read_member(maps, member)
                digest = hashlib.sha256(data).hexdigest()
                if digest != expected:
                    raise ValueError(f"Reference SHA256 changed: {member}")
                if data[:4] != b"IBSP" or struct.unpack_from("<i", data, 4)[0] != 46:
                    raise ValueError("Reference is not IBSP v46")
                payloads[Path(member).name] = data
                report["files"].append({
                    "archive_member": MAP_ARCHIVE, "member": member,
                    "bytes": len(data), "sha256": digest, "crc32": f"{info.CRC:08x}",
                    "magic": "IBSP", "version": 46,
                })
    report["bytes_transferred"] = budget[0]
    payloads["provenance.json"] = (json.dumps(report, indent=2) + "\n").encode()
    # Nothing is written until all expected hashes and member CRCs pass.
    output.mkdir(parents=True, exist_ok=False)
    for name, data in payloads.items():
        with (output / name).open("xb") as target:
            target.write(data)
    print(json.dumps(report, indent=2))
    print("Local reference extraction complete; no whole-release hash, import or gameplay proof.")


if __name__ == "__main__":
    main()
