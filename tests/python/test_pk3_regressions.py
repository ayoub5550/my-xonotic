"""Additional parent-review regressions; no archive bytes are executed."""
import io
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/content"))
import pk3_safety as safety
import pk3_tool


def archive(entries):
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w") as z:
        for name, payload in entries:
            z.writestr(name, payload)
    out.seek(0)
    return out


class ParentReviewRegressions(unittest.TestCase):
    def test_extraction_checks_archive_entry_count(self):
        with zipfile.ZipFile(archive([("x", b"1"), ("y", b"2")])) as z:
            with tempfile.TemporaryDirectory() as d, mock.patch.object(safety, "MAX_ENTRY_COUNT", 1):
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(z, "x", d)

    def test_extraction_checks_archive_total_size(self):
        with zipfile.ZipFile(archive([("x", b"12"), ("y", b"34")])) as z:
            with tempfile.TemporaryDirectory() as d, mock.patch.object(safety, "MAX_TOTAL_UNCOMPRESSED_BYTES", 3):
                with self.assertRaises(safety.Pk3SafetyError):
                    safety.safe_extract_one(z, "x", d)

    def test_canonical_duplicates_are_ambiguous(self):
        for other in ("maps\\x.bsp", "MAPS/X.BSP"):
            with self.subTest(other=other), zipfile.ZipFile(archive([("maps/x.bsp", b"1"), (other, b"2")])) as z:
                with tempfile.TemporaryDirectory() as d:
                    with self.assertRaises(safety.Pk3SafetyError):
                        safety.safe_extract_one(z, "maps/x.bsp", d)

    def test_windows_device_and_alias_names_rejected(self):
        for path in ("maps/NUL.bsp", "CON", "COM1.txt", "maps/name.", "maps/name ", "bad\x01.bsp"):
            with self.subTest(path=path):
                self.assertFalse(safety._is_safe_relative_name(path))

    def test_normal_directory_is_not_suspicious(self):
        with zipfile.ZipFile(archive([("maps/", b""), ("maps/x.bsp", b"x")])) as z:
            self.assertFalse(any(r.is_suspicious for r in safety.inspect_entries(z)))

    def test_destination_can_be_created(self):
        with tempfile.TemporaryDirectory() as d:
            dest = Path(d) / "new"
            with zipfile.ZipFile(archive([("maps/x.bsp", b"x")])) as z:
                path = safety.safe_extract_one(z, "maps/x.bsp", str(dest))
            self.assertEqual(Path(path).read_bytes(), b"x")

    def test_failed_sidecar_does_not_leave_untracked_extraction(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            pk3 = root / "sample.pk3"
            pk3.write_bytes(archive([("x.bsp", b"x")]).getvalue())
            output = root / "out"
            output.mkdir()
            sidecar = output / "x.bsp.provenance.json"
            sidecar.write_text("keep")
            self.assertEqual(pk3_tool.main(["extract", str(pk3), "x.bsp", "--out-dir", str(output)]), 3)
            self.assertFalse((output / "x.bsp").exists())
            self.assertEqual(sidecar.read_text(), "keep")

    @unittest.skipUnless(hasattr(os, "symlink"), "Requires symlink support")
    def test_sidecar_symlink_never_writes_target(self):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            pk3 = root / "sample.pk3"
            pk3.write_bytes(archive([("x.bsp", b"x")]).getvalue())
            output = root / "out"
            output.mkdir()
            target = root / "private.txt"
            target.write_text("untouched")
            (output / "x.bsp.provenance.json").symlink_to(target)
            self.assertEqual(pk3_tool.main(["extract", str(pk3), "x.bsp", "--out-dir", str(output)]), 3)
            self.assertEqual(target.read_text(), "untouched")
            self.assertFalse((output / "x.bsp").exists())


if __name__ == "__main__":
    unittest.main()
