"""Tests use invented bytes; no upstream resource is required."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/content"))
from verify_resources import verify


class ResourceVerifierTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "maps").mkdir()
        (self.root / "maps/original.map").write_bytes(b"original")
        self.entry = {
            "path": "maps/original.map",
            "bytes": 8,
            "sha256": hashlib.sha256(b"original").hexdigest(),
            "source_url": "https://example.invalid/upstream/original.map",
        }
        self.write_index([self.entry])

    def write_index(self, entries):
        (self.root / "resource-index.json").write_text(
            json.dumps({"schema_version": 1, "files": entries}))

    def test_valid(self):
        result = verify(self.root)
        self.assertTrue(result["passed"], result)
        self.assertEqual((result["files_checked"], result["bytes_checked"]), (1, 8))

    def test_same_size_tampering(self):
        (self.root / self.entry["path"]).write_bytes(b"modified")
        self.assertFalse(verify(self.root)["passed"])

    def test_size_tampering(self):
        (self.root / self.entry["path"]).write_bytes(b"larger content")
        self.assertFalse(verify(self.root)["passed"])

    def test_missing_file(self):
        (self.root / self.entry["path"]).unlink()
        self.assertFalse(verify(self.root)["passed"])

    def test_unlisted_file(self):
        (self.root / "extra.ogg").write_bytes(b"not listed")
        self.assertFalse(verify(self.root)["passed"])

    def test_symlink_file(self):
        p = self.root / self.entry["path"]
        p.unlink()
        p.symlink_to(self.root / "resource-index.json")
        self.assertFalse(verify(self.root)["passed"])

    def test_symlink_directory(self):
        (self.root / "maps/original.map").unlink()
        (self.root / "maps").rmdir()
        (self.root / "maps").symlink_to(self.root)
        self.assertFalse(verify(self.root)["passed"])

    def test_unsafe_paths(self):
        for path in ["../escape", "/escape", r"C:\escape", "a//b", "./a", "a/../b"]:
            with self.subTest(path=path):
                self.write_index([{**self.entry, "path": path}])
                self.assertFalse(verify(self.root)["passed"])

    def test_duplicate_case_alias(self):
        for path in [self.entry["path"], self.entry["path"].upper()]:
            self.write_index([self.entry, {**self.entry, "path": path}])
            self.assertFalse(verify(self.root)["passed"])

    def test_invalid_index(self):
        for data in ["{", "[]", '{"schema_version":1,"files":[]}']:
            (self.root / "resource-index.json").write_text(data)
            self.assertFalse(verify(self.root)["passed"])

    def test_metadata_is_not_an_asset(self):
        (self.root / "README.md").write_text("Project notes")
        self.assertTrue(verify(self.root)["passed"])
        self.write_index([{**self.entry, "path": "README.md"}])
        self.assertFalse(verify(self.root)["passed"])

    def test_invalid_size_hash_and_provenance(self):
        for key, value in [("bytes", -1), ("bytes", True), ("sha256", "not-a-hash"),
                           ("source_url", "file:///private")]:
            self.write_index([{**self.entry, key: value}])
            self.assertFalse(verify(self.root)["passed"])


if __name__ == "__main__":
    unittest.main()
