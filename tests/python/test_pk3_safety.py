"""
Unit tests for tools/content/pk3_safety.py and fixture_bsp.py. Stdlib only
(unittest + zipfile + io); no pytest/third-party dependency required, though
pytest can also collect this file if present.

Run: python3 tests/python/test_pk3_safety.py
"""
from __future__ import annotations

import argparse
import io
import os
import struct
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools", "content"))
import pk3_safety as safety  # noqa: E402
import fixture_bsp  # noqa: E402
import pk3_tool  # noqa: E402


def _make_zip(entries: dict, *, zip64: bool = False) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED, allowZip64=zip64) as zf:
        for name, data in entries.items():
            zf.writestr(name, data)
    return buf.getvalue()


class TestPathTraversal(unittest.TestCase):
    def test_rejects_parent_traversal(self):
        self.assertFalse(safety._is_safe_relative_name("../../etc/passwd"))
        self.assertFalse(safety._is_safe_relative_name("maps/../../../etc/passwd"))

    def test_rejects_absolute_paths(self):
        self.assertFalse(safety._is_safe_relative_name("/etc/passwd"))
        self.assertFalse(safety._is_safe_relative_name("\\\\server\\share\\x"))

    def test_accepts_normal_relative_path(self):
        self.assertTrue(safety._is_safe_relative_name("maps/boil.bsp"))
        self.assertTrue(safety._is_safe_relative_name("textures/common/caulk.tga"))

    def test_rejects_windows_drive_paths(self):
        self.assertFalse(safety._is_safe_relative_name("C:\\foo"))
        self.assertFalse(safety._is_safe_relative_name("C:/foo/bar.bsp"))
        self.assertFalse(safety._is_safe_relative_name("c:foo"))
        self.assertFalse(safety._is_safe_relative_name("\\\\server\\share\\x"))

    def test_rejects_dot_segments_without_relying_on_normalization(self):
        self.assertFalse(safety._is_safe_relative_name("maps/./good.bsp"))
        self.assertFalse(safety._is_safe_relative_name("maps/../good.bsp"))
        self.assertFalse(safety._is_safe_relative_name("./good.bsp"))
        self.assertFalse(safety._is_safe_relative_name(""))
        self.assertFalse(safety._is_safe_relative_name("maps//good.bsp"))  # empty component

    def test_rejects_nul_byte(self):
        self.assertFalse(safety._is_safe_relative_name("maps/good.bsp\x00.exe"))

    def test_inventory_flags_traversal_entry(self):
        data = _make_zip({"maps/good.bsp": b"x" * 100})
        # zipfile.writestr won't create a traversal entry directly via the
        # high-level API; construct one by hand instead.
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr(zipfile.ZipInfo("../../etc/passwd"), b"evil")
            zf.writestr("maps/good.bsp", b"x" * 50)
        with zipfile.ZipFile(buf, "r") as zf:
            reports = safety.inspect_entries(zf)
        bad = [r for r in reports if r.name == "../../etc/passwd"]
        self.assertEqual(len(bad), 1)
        self.assertTrue(bad[0].is_suspicious)
        self.assertIn("unsafe path", bad[0].reasons[0])

    def test_extract_refuses_traversal_entry(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr(zipfile.ZipInfo("../evil.bsp"), b"evil-bsp-bytes")
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(buf, "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "../evil.bsp", tmp)


class TestZipBombGuards(unittest.TestCase):
    def test_rejects_high_compression_ratio_entry(self):
        # 10 MB of zeros compresses extremely well; ratio should exceed the ceiling.
        huge_zero_payload = b"\x00" * (10 * 1024 * 1024)
        data = _make_zip({"maps/bomb.bsp": huge_zero_payload})
        with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
            reports = safety.inspect_entries(zf)
        self.assertTrue(any(r.is_suspicious and "compression ratio" in r.reasons[0] for r in reports))

    def test_extract_refuses_high_ratio_entry(self):
        huge_zero_payload = b"\x00" * (10 * 1024 * 1024)
        data = _make_zip({"maps/bomb.bsp": huge_zero_payload})
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "maps/bomb.bsp", tmp)

    def test_normal_bsp_like_entry_extracts_fine(self):
        payload = fixture_bsp.build_fixture_bsp()
        data = _make_zip({"maps/good.bsp": payload})
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                dest = safety.safe_extract_one(zf, "maps/good.bsp", tmp)
            with open(dest, "rb") as f:
                self.assertEqual(f.read(), payload)


class TestScriptExtensionGuard(unittest.TestCase):
    def test_refuses_script_extension_by_default(self):
        data = _make_zip({"progs/evil.dat": b"not really a progs file but has the extension"})
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "progs/evil.dat", tmp)

    def test_allows_script_extension_when_explicitly_forced(self):
        data = _make_zip({"progs/evil.dat": b"content"})
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                dest = safety.safe_extract_one(zf, "progs/evil.dat", tmp, allow_scripts=True)
            self.assertTrue(os.path.isfile(dest))


class TestSymlinkGuard(unittest.TestCase):
    def test_refuses_symlink_entry(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            info = zipfile.ZipInfo("maps/link.bsp")
            # Encode a symlink Unix mode (0o120777 << 16) into external_attr.
            info.external_attr = (0o120777) << 16
            zf.writestr(info, "/etc/passwd")
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(buf, "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "maps/link.bsp", tmp)


class TestProvenanceHashing(unittest.TestCase):
    def test_sha256_stable_and_matches_hashlib(self):
        import hashlib
        payload = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "fixture.bsp")
            with open(path, "wb") as f:
                f.write(payload)
            got = safety.sha256_file(path)
        expected = hashlib.sha256(payload).hexdigest()
        self.assertEqual(got, expected)

    def test_extract_writes_provenance_via_cli(self):
        import subprocess
        payload = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as tmp:
            pk3_path = os.path.join(tmp, "test.pk3")
            with zipfile.ZipFile(pk3_path, "w") as zf:
                zf.writestr("maps/good.bsp", payload)
            out_dir = os.path.join(tmp, "out")
            tool = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tools", "content", "pk3_tool.py")
            result = subprocess.run(
                [sys.executable, tool, "extract", pk3_path, "maps/good.bsp", "--out-dir", out_dir],
                capture_output=True, text=True,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            extracted = os.path.join(out_dir, "maps", "good.bsp")
            self.assertTrue(os.path.isfile(extracted))
            prov_path = extracted + ".provenance.json"
            self.assertTrue(os.path.isfile(prov_path))
            import json
            with open(prov_path) as f:
                prov = json.load(f)
            self.assertIn("source_archive_sha256", prov)
            self.assertIn("extracted_sha256", prov)


class TestFixtureFormat(unittest.TestCase):
    def test_good_fixture_has_ibsp_v46_header(self):
        data = fixture_bsp.build_fixture_bsp()
        self.assertEqual(data[:4], b"IBSP")
        (version,) = struct.unpack("<i", data[4:8])
        self.assertEqual(version, 46)

    def test_bad_magic_fixture_is_not_ibsp(self):
        data = fixture_bsp.build_bad_magic()
        self.assertNotEqual(data[:4], b"IBSP")

    def test_unsupported_version_fixture_has_wrong_version(self):
        data = fixture_bsp.build_unsupported_version()
        (version,) = struct.unpack("<i", data[4:8])
        self.assertNotEqual(version, 46)

    def test_nan_vertex_fixture_actually_contains_nan(self):
        import math
        data = fixture_bsp.build_nan_vertex_attack()
        entry_off = 8 + fixture_bsp.LUMP_VERTEXES * 8
        voff, _ = struct.unpack_from("<2i", data, entry_off)
        (x,) = struct.unpack_from("<f", data, voff)
        self.assertTrue(math.isnan(x))

    def test_huge_lump_attack_points_past_eof(self):
        data = fixture_bsp.build_huge_lump_count_attack()
        entry_off = 8 + fixture_bsp.LUMP_VERTEXES * 8
        offset, length = struct.unpack_from("<2i", data, entry_off)
        self.assertGreater(offset + length, len(data))

    def test_shader_asymmetry_fixture_has_distinct_fields(self):
        data = fixture_bsp.build_shader_asymmetry_bsp()
        entry_off = 8 + fixture_bsp.LUMP_SHADERS * 8
        soff, slen = struct.unpack_from("<2i", data, entry_off)
        self.assertEqual(slen, 2 * 72)
        # shader 0: surface(@64)=0, content(@68)=1
        surf0, cont0 = struct.unpack_from("<2i", data, soff + 64)
        self.assertEqual((surf0, cont0), (0, 1))
        # shader 1: surface(@64)=0x80, content(@68)=0
        surf1, cont1 = struct.unpack_from("<2i", data, soff + 72 + 64)
        self.assertEqual((surf1, cont1), (0x80, 0))


@unittest.skipUnless(hasattr(os, "symlink"), "requires a filesystem with symlink support")
class TestSymlinkFilesystemHardening(unittest.TestCase):
    """Real-filesystem symlink attacks against safe_extract_one, not just
    zip-entry-level symlink flags."""

    def test_refuses_to_extract_through_symlinked_directory_component(self):
        payload = fixture_bsp.build_fixture_bsp()
        data = _make_zip({"maps/good.bsp": payload})
        with tempfile.TemporaryDirectory() as tmp:
            real_outside = os.path.join(tmp, "outside")
            os.makedirs(real_outside)
            out_dir = os.path.join(tmp, "out")
            os.makedirs(out_dir)
            # "maps" inside out_dir is a symlink pointing outside out_dir.
            os.symlink(real_outside, os.path.join(out_dir, "maps"))
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "maps/good.bsp", out_dir)
            # Nothing should have been written through the symlink.
            self.assertEqual(os.listdir(real_outside), [])

    def test_refuses_when_destination_itself_is_a_symlink(self):
        payload = fixture_bsp.build_fixture_bsp()
        data = _make_zip({"good.bsp": payload})
        with tempfile.TemporaryDirectory() as tmp:
            real_out = os.path.join(tmp, "real_out")
            os.makedirs(real_out)
            link_out = os.path.join(tmp, "link_out")
            os.symlink(real_out, link_out)
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "good.bsp", link_out)

    def test_refuses_to_follow_preexisting_symlink_at_target_path(self):
        payload = fixture_bsp.build_fixture_bsp()
        data = _make_zip({"good.bsp": payload})
        with tempfile.TemporaryDirectory() as tmp:
            out_dir = os.path.join(tmp, "out")
            os.makedirs(out_dir)
            evil_target = os.path.join(tmp, "evil_target.bsp")
            with open(evil_target, "wb") as f:
                f.write(b"should never be overwritten")
            os.symlink(evil_target, os.path.join(out_dir, "good.bsp"))
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "good.bsp", out_dir)
            with open(evil_target, "rb") as f:
                self.assertEqual(f.read(), b"should never be overwritten")


class TestNoClobber(unittest.TestCase):
    def test_refuses_to_overwrite_existing_regular_file(self):
        payload = fixture_bsp.build_fixture_bsp()
        data = _make_zip({"good.bsp": payload})
        with tempfile.TemporaryDirectory() as tmp:
            existing = os.path.join(tmp, "good.bsp")
            with open(existing, "wb") as f:
                f.write(b"pre-existing content, must survive")
            with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "good.bsp", tmp)
            with open(existing, "rb") as f:
                self.assertEqual(f.read(), b"pre-existing content, must survive")


class TestCorruptCrcCleansPartialFile(unittest.TestCase):
    def test_crc_mismatch_removes_partial_output(self):
        # Build a zip with a stored (uncompressed) entry, then flip a byte
        # of the entry's *data* after the CRC was computed, so the local
        # payload no longer matches the recorded CRC.
        buf = io.BytesIO()
        payload = b"A" * 1024
        with zipfile.ZipFile(buf, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr("good.bsp", payload)
        raw = bytearray(buf.getvalue())
        # Corrupt one byte of the payload itself (appears once, right after
        # the local file header + filename for a ZIP_STORED entry).
        idx = raw.find(payload)
        self.assertNotEqual(idx, -1)
        raw[idx] = raw[idx] ^ 0xFF

        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(io.BytesIO(bytes(raw)), "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "good.bsp", tmp, allow_scripts=True)
            self.assertEqual(os.listdir(tmp), [], "partially written file must be removed on CRC failure")


class TestDuplicateEntryRejection(unittest.TestCase):
    def test_refuses_ambiguous_duplicate_entry_name(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr("good.bsp", b"first copy")
            zf.writestr("good.bsp", b"second, different copy")
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(buf, "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "good.bsp", tmp, allow_scripts=True)

    def test_inventory_flags_duplicate_entry_name(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr("good.bsp", b"first copy")
            zf.writestr("good.bsp", b"second, different copy")
        with zipfile.ZipFile(buf, "r") as zf:
            reports = safety.inspect_entries(zf)
        dup = [r for r in reports if r.name == "good.bsp"]
        self.assertEqual(len(dup), 2)
        self.assertTrue(all(r.is_suspicious for r in dup))
        self.assertTrue(all("duplicate entry name" in reason for r in dup for reason in r.reasons))


class TestEncryptedEntryRejection(unittest.TestCase):
    def test_refuses_encrypted_entry(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr("good.bsp", b"data")
            # Flip the encryption bit (bit 0 of the general-purpose flag)
            # on the entry we just wrote, without actually encrypting it —
            # enough to exercise the "refuse encrypted" code path.
            info = zf.infolist()[0]
            info.flag_bits |= 0x1
        with tempfile.TemporaryDirectory() as tmp:
            with zipfile.ZipFile(buf, "r") as zf:
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(zf, "good.bsp", tmp, allow_scripts=True)


class TestInventoryNeverCallsTestzip(unittest.TestCase):
    def test_inventory_command_never_invokes_zipfile_testzip(self):
        payload = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as tmp:
            pk3_path = os.path.join(tmp, "test.pk3")
            with zipfile.ZipFile(pk3_path, "w") as zf:
                zf.writestr("maps/good.bsp", payload)

            def _boom(self, *a, **kw):
                raise AssertionError("ZipFile.testzip() must never be called by inventory (decompresses everything)")

            import unittest.mock as mock
            with mock.patch.object(zipfile.ZipFile, "testzip", _boom):
                ns = argparse.Namespace(pk3=pk3_path, json=True, report_out=None)
                rc = pk3_tool.cmd_inventory(ns)
        self.assertEqual(rc, 0)

    def test_inventory_report_states_crc_not_verified(self):
        payload = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as tmp:
            pk3_path = os.path.join(tmp, "test.pk3")
            with zipfile.ZipFile(pk3_path, "w") as zf:
                zf.writestr("maps/good.bsp", payload)
            import io as _io
            import contextlib
            import json as _json
            ns = argparse.Namespace(pk3=pk3_path, json=True, report_out=None)
            captured = _io.StringIO()
            with contextlib.redirect_stdout(captured):
                pk3_tool.cmd_inventory(ns)
            report = _json.loads(captured.getvalue())
        self.assertFalse(report["crc_verified"])
        self.assertIn("never", report["crc_verification_note"].lower())


if __name__ == "__main__":
    unittest.main()
