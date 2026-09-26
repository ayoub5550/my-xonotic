"""Artifact/receipt gate tests. These are not real Unity builds."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

module_path = Path(__file__).resolve().parents[2] / "tools/local_unity.py"
spec = importlib.util.spec_from_file_location("local_unity_receipt", module_path)
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class ReceiptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "Builds").mkdir()
        self.artifact = self.root / "Builds/plasma-verge-boil.apk"
        self.artifact.write_bytes(b"synthetic artifact, NOT an APK")
        self.receipt = {
            "target": "Android", "invocation": "fresh-id", "result": "Succeeded",
            "errors": 0, "output": self.artifact.name,
            "artifactBytes": self.artifact.stat().st_size,
            "sha256": hashlib.sha256(self.artifact.read_bytes()).hexdigest(),
        }
        self.write_receipt()

    def write_receipt(self):
        (self.root / "Builds/build-receipt.json").write_text(json.dumps(self.receipt))

    def valid(self):
        return runner.validate_build_receipt(self.root, "android", "fresh-id", True)

    def test_matching_current_receipt(self):
        self.assertTrue(self.valid())

    def test_old_receipt_cannot_pass(self):
        self.receipt["invocation"] = "previous-run"
        self.write_receipt()
        self.assertFalse(self.valid())

    def test_missing_receipt(self):
        (self.root / "Builds/build-receipt.json").unlink()
        self.assertFalse(self.valid())

    def test_malformed_receipt(self):
        (self.root / "Builds/build-receipt.json").write_text("{")
        self.assertFalse(self.valid())

    def test_wrong_shape(self):
        self.receipt = []
        self.write_receipt()
        self.assertFalse(self.valid())

    def test_missing_artifact(self):
        self.artifact.unlink()
        self.assertFalse(self.valid())

    def test_empty_artifact(self):
        self.artifact.write_bytes(b"")
        self.assertFalse(self.valid())

    def test_same_size_tamper(self):
        self.artifact.write_bytes(b"x" * self.artifact.stat().st_size)
        self.assertFalse(self.valid())

    def test_symlink_artifact_rejected(self):
        target = self.root / "old.apk"
        self.artifact.rename(target)
        self.artifact.symlink_to(target)
        self.assertFalse(self.valid())

    def test_wrong_target_or_result_or_size_or_output(self):
        for key, value in [("target", "StandaloneLinux64"), ("result", "Failed"),
                           ("artifactBytes", 0), ("output", "../old.apk"), ("errors", 1)]:
            original = self.receipt[key]
            self.receipt[key] = value
            self.write_receipt()
            self.assertFalse(self.valid(), key)
            self.receipt[key] = original

    def test_fixture_cannot_be_reported_as_original_content(self):
        self.assertFalse(runner.validate_build_receipt(
            self.root, "android", "fresh-id", False))


class RunnerEarlyExitTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.fake = self.root / "fake-unity"
        self.fake.write_text("#!/bin/sh\nexit 0\n")
        self.fake.chmod(0o700)
        (self.root / "Artifacts").mkdir()
        (self.root / "Builds").mkdir()

    def invoke(self, task):
        argv = ["local_unity.py", task, "--unity", str(self.fake), "--timeout", "5"]
        with mock.patch.object(runner, "ROOT", self.root), \
                mock.patch.object(runner.sys, "argv", argv), \
                mock.patch.dict(runner.os.environ, {"UNITY_USER": "", "UNITY_PASS": ""}):
            return runner.main()

    def test_exit_zero_without_build_is_failure(self):
        (self.root / "Builds/build-receipt.json").write_text('{"result":"Succeeded"}')
        self.assertEqual(self.invoke("android"), 1)
        self.assertFalse((self.root / "Builds/build-receipt.json").exists())
        self.assertFalse((self.root / "Artifacts/local-unity.lock").exists())

    def test_exit_zero_cannot_reuse_old_editor_tests(self):
        (self.root / "Artifacts/editor-tests.txt").write_text("PASS old run")
        self.assertEqual(self.invoke("test"), 1)

    def test_exit_zero_cannot_reuse_old_playtest(self):
        (self.root / "Artifacts/playtest-result.json").write_text('{"passed":true}')
        self.assertEqual(self.invoke("playtest"), 1)

    def test_exit_zero_cannot_reuse_gameplay_tests(self):
        (self.root / "Artifacts/gameplay-integration-tests.txt").write_text("PASS old")
        self.assertEqual(self.invoke("gameplay-test"), 1)

    def test_exit_zero_cannot_reuse_gameplay_playtest(self):
        (self.root / "Artifacts/gameplay-playtest.json").write_text('{"passed":true}')
        self.assertEqual(self.invoke("gameplay-playtest"), 1)

    def test_exit_zero_cannot_reuse_old_compile(self):
        (self.root / "Artifacts/compile-result.json").write_text(
            '{"passed":true,"invocation":"old"}')
        self.assertEqual(self.invoke("compile"), 1)

    def test_launch_error_still_releases_lock(self):
        self.fake.chmod(0o600)
        with self.assertRaises(PermissionError):
            self.invoke("android")
        self.assertFalse((self.root / "Artifacts/local-unity.lock").exists())


if __name__ == "__main__":
    unittest.main()
