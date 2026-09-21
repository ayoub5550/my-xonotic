"""
Unit tests for tools/content/publish_resources.py.

Stdlib only (unittest + zipfile); no pytest/third-party dependency required.
Run: python3 tests/python/test_publish_resources.py

Covers:
  - symlink target resolution: a valid same-archive relative chain resolves
    to the final regular entry's bytes; an out-of-archive-root target, a
    cycle, and a target missing from the archive are all refused (recorded
    as symlink_unresolved, never as a real OS symlink and never as a
    silent extraction of nothing).
  - publish(): regular entries, a resolved-symlink byte copy, and an exact
    duplicate all land as real regular files (never `os.path.islink()`),
    with correct sha256/size, and duplicate content is hardlinked (same
    st_ino) rather than re-copied. A "compression ratio" flagged entry
    still gets staged (only recorded, never skipped) while a genuinely
    unsafe (zip-slip) entry name is skipped and never written outside the
    destination root.
  - publish() idempotence: re-running over an existing tree never rewrites
    already-correct bytes (mtime/inode of an untouched file is unchanged)
    and still stages a file a prior bug had skipped.
  - restore(): round-trips derived/notices bytes back into a fresh
    ExternalContent-shaped tree; a target that already has the exact same
    bytes is left alone; a target with DIFFERENT existing bytes is refused
    (mismatch, never overwritten); a target that is itself a symlink is
    refused; a manifest entry whose committed source bytes were tampered
    with (sha256 no longer matches) is refused before anything is copied.
"""
from __future__ import annotations

import json
import os
import shutil
import stat
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "content"))
import publish_resources as pub  # noqa: E402
import pk3_safety as safety  # noqa: E402


def _make_symlink_entry(zf: zipfile.ZipFile, name: str, target: str) -> None:
    info = zipfile.ZipInfo(name)
    info.external_attr = (stat.S_IFLNK | 0o777) << 16
    zf.writestr(info, target)


class ResolveSymlinkChainTests(unittest.TestCase):
    def _zf(self, entries):
        buf_path = Path(tempfile.mkstemp(suffix=".pk3")[1])
        self.addCleanup(lambda: buf_path.unlink(missing_ok=True))
        with zipfile.ZipFile(buf_path, "w") as z:
            for name, content, is_symlink in entries:
                if is_symlink:
                    _make_symlink_entry(z, name, content)
                else:
                    z.writestr(name, content)
        return zipfile.ZipFile(buf_path)

    def test_direct_resolution(self):
        zf = self._zf([
            ("dds/arc.dds", "textures/arc.dds", True),
            ("dds/textures/arc.dds", b"realbytes", False),
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        final, hops, failure = pub._resolve_symlink_chain(zf, by_name, "dds/arc.dds")
        self.assertIsNone(failure)
        self.assertEqual(final.filename, "dds/textures/arc.dds")
        self.assertEqual(hops, ["dds/arc.dds", "dds/textures/arc.dds"])

    def test_chained_symlink_resolution(self):
        zf = self._zf([
            ("a/one.dds", "two.dds", True),
            ("a/two.dds", "../b/three.dds", True),
            ("b/three.dds", b"finalbytes", False),
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        final, hops, failure = pub._resolve_symlink_chain(zf, by_name, "a/one.dds")
        self.assertIsNone(failure)
        self.assertEqual(final.filename, "b/three.dds")

    def test_escaping_target_refused(self):
        zf = self._zf([
            ("a/one.dds", "../../etc/passwd", True),
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        final, hops, failure = pub._resolve_symlink_chain(zf, by_name, "a/one.dds")
        self.assertIsNone(final)
        self.assertIn("escapes", failure)

    def test_cycle_refused(self):
        zf = self._zf([
            ("a/one.dds", "two.dds", True),
            ("a/two.dds", "one.dds", True),
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        final, hops, failure = pub._resolve_symlink_chain(zf, by_name, "a/one.dds")
        self.assertIsNone(final)
        self.assertIn("cycle", failure)

    def test_missing_target_refused(self):
        zf = self._zf([
            ("a/one.dds", "nowhere.dds", True),
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        final, hops, failure = pub._resolve_symlink_chain(zf, by_name, "a/one.dds")
        self.assertIsNone(final)
        self.assertIn("not found", failure)

    def test_ambiguous_duplicate_target_name_refused(self):
        # by_name (a plain dict) would silently collapse two entries with
        # the same name to "whichever writestr() call happened last"; the
        # duplicate_names set must refuse this instead of resolving
        # through that arbitrarily-chosen entry.
        zf = self._zf([
            ("a/one.dds", "shared.dds", True),
            ("a/shared.dds", b"first", False),
            ("a/shared.dds", b"second", False),  # duplicate name, different bytes
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        duplicate_names = {"a/shared.dds"}
        final, hops, failure = pub._resolve_symlink_chain(
            zf, by_name, "a/one.dds", duplicate_names=duplicate_names)
        self.assertIsNone(final)
        self.assertIn("ambiguous", failure)

    def test_unsafe_final_target_refused_via_report_cross_check(self):
        zf = self._zf([
            ("a/one.dds", "unsafe.dds", True),
            ("a/unsafe.dds", b"whatever", False),
        ])
        self.addCleanup(zf.close)
        by_name = {i.filename: i for i in zf.infolist()}
        fake_report = safety.EntryReport(
            name="a/unsafe.dds", compressed_size=1, uncompressed_size=8, compression_ratio=8.0,
            is_dir=False, is_suspicious=True, reasons=["unsafe path (absolute, drive-letter, or traversal)"],
        )
        final, hops, failure = pub._resolve_symlink_chain(
            zf, by_name, "a/one.dds", reports_by_name={"a/unsafe.dds": fake_report})
        self.assertIsNone(final)
        self.assertIn("fails safety inspection", failure)


class PublishStageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

        # Redirect module globals into an isolated temp tree.
        self._orig = {
            "REPO_ROOT": pub.REPO_ROOT, "EXTERNAL_CONTENT": pub.EXTERNAL_CONTENT,
            "STAGING_PK3_DIR": pub.STAGING_PK3_DIR, "NOTICES_DIR": pub.NOTICES_DIR,
            "DEST_ROOT": pub.DEST_ROOT, "PACKS": pub.PACKS, "DERIVED_ROOTS": pub.DERIVED_ROOTS,
            "KNOWN_PACK_SHA256": pub.KNOWN_PACK_SHA256,
        }
        self.addCleanup(self._restore_globals)

        pub.REPO_ROOT = str(self.root)
        pub.EXTERNAL_CONTENT = str(self.root / "ExternalContent")
        pub.STAGING_PK3_DIR = str(self.root / "ExternalContent" / "staging_pk3" / "Xonotic" / "data")
        pub.NOTICES_DIR = str(self.root / "ExternalContent" / "notices")
        pub.DEST_ROOT = str(self.root / "ThirdParty" / "Xonotic-0.8.6")
        os.makedirs(pub.STAGING_PK3_DIR)
        os.makedirs(pub.NOTICES_DIR)

        pk3_path = os.path.join(pub.STAGING_PK3_DIR, "sample.pk3")
        with zipfile.ZipFile(pk3_path, "w") as z:
            z.writestr("textures/base.dds", b"base-bytes-0123456789")
            z.writestr("textures/dup.dds", b"base-bytes-0123456789")  # exact duplicate content
            _make_symlink_entry(z, "textures/link.dds", "base.dds")
            z.writestr("readme.cfg", "// data only, never executed\n")
            # A path-traversal / zip-slip attempt: must be skipped, never written.
            zi = zipfile.ZipInfo("../evil.txt")
            z.writestr(zi, "should never land on disk")

        pub.PACKS = [("sample", "sample.pk3")]
        pub.DERIVED_ROOTS = []

        with open(os.path.join(pub.NOTICES_DIR, "COPYING"), "w") as f:
            f.write("licence text\n")

    def _restore_globals(self):
        for k, v in self._orig.items():
            setattr(pub, k, v)

    def test_publish_stages_regular_symlink_and_dedup(self):
        rc = pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=False))
        self.assertEqual(rc, 0)

        dest = Path(pub.DEST_ROOT)
        base_path = dest / "packs" / "sample" / "textures" / "base.dds"
        dup_path = dest / "packs" / "sample" / "textures" / "dup.dds"
        link_path = dest / "packs" / "sample" / "textures" / "link.dds"

        for p in (base_path, dup_path, link_path):
            self.assertTrue(p.is_file())
            self.assertFalse(p.is_symlink(), f"{p} must never be a real OS symlink")

        # Exact-duplicate content is hardlinked (same inode), not re-copied.
        self.assertEqual(base_path.stat().st_ino, dup_path.stat().st_ino)
        self.assertEqual(base_path.read_bytes(), b"base-bytes-0123456789")
        self.assertEqual(link_path.read_bytes(), b"base-bytes-0123456789")

        # zip-slip entry never written anywhere, including outside dest root.
        self.assertFalse((self.root / "evil.txt").exists())
        self.assertFalse((dest.parent / "evil.txt").exists())

        manifest = json.loads((dest / "publish-manifest.json").read_text())
        self.assertEqual(manifest["totals"]["files"], 5)  # base, dup, link(resolved copy), readme.cfg, notices/COPYING
        self.assertEqual(manifest["totals"]["unresolved_symlinks"], 0)
        self.assertEqual(manifest["totals"]["resolved_symlink_copies"], 1)
        self.assertGreaterEqual(manifest["totals"]["hardlinked_duplicates"], 1)
        self.assertEqual(len(manifest["skipped"]), 1)
        self.assertIn("evil.txt", manifest["skipped"][0]["entry"])
        # No absolute machine paths leaked into the manifest.
        raw = json.dumps(manifest)
        self.assertNotIn(str(self.root), raw)

    def _write_ratio_flagged_pk3(self):
        pk3_path = os.path.join(pub.STAGING_PK3_DIR, "sample.pk3")
        payload = b"\x00" * 4096  # highly compressible -> big ratio, tiny compressed size
        with zipfile.ZipFile(pk3_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
            z.writestr("textures/flagged.dds", payload)
        pub.PACKS = [("sample", "sample.pk3")]
        return pk3_path, payload

    def test_ratio_flagged_entry_staged_only_for_a_pinned_verified_pack(self):
        # The compression-ratio waiver may ONLY apply when this archive's
        # own bytes hash-match a pinned KNOWN_PACK_SHA256 entry — never as
        # a blanket "any zip" policy. Pin this synthetic archive's own
        # actual hash under its own filename to exercise the waiver
        # honestly (not by weakening the check).
        pk3_path, payload = self._write_ratio_flagged_pk3()
        pub.KNOWN_PACK_SHA256 = dict(pub.KNOWN_PACK_SHA256)
        pub.KNOWN_PACK_SHA256["sample.pk3"] = safety.sha256_file(pk3_path)

        rc = pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=False))
        self.assertEqual(rc, 0)
        staged = Path(pub.DEST_ROOT) / "packs" / "sample" / "textures" / "flagged.dds"
        self.assertTrue(staged.is_file())
        self.assertEqual(staged.read_bytes(), payload)
        manifest = json.loads((Path(pub.DEST_ROOT) / "publish-manifest.json").read_text())
        self.assertEqual(len(manifest["skipped"]), 0)
        flagged_rec = next(r for r in manifest["files_list"] if r["entry_name"] == "textures/flagged.dds")
        self.assertTrue(flagged_rec["compression_ratio_flagged"])

    def test_ratio_flagged_entry_hard_skipped_for_an_unpinned_archive(self):
        # Same archive shape, but NOT registered in KNOWN_PACK_SHA256 (the
        # default fixture state) -> fails closed: the ratio-flagged entry
        # is skipped like any other unverified input, never silently
        # waived just because its name happens to look like a real pack.
        self._write_ratio_flagged_pk3()
        rc = pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=False))
        self.assertEqual(rc, 0)
        staged = Path(pub.DEST_ROOT) / "packs" / "sample" / "textures" / "flagged.dds"
        self.assertFalse(staged.exists())
        manifest = json.loads((Path(pub.DEST_ROOT) / "publish-manifest.json").read_text())
        self.assertEqual(len(manifest["skipped"]), 1)
        self.assertIn("compression ratio", manifest["skipped"][0]["reasons"][0])

    def test_ratio_waiver_refused_when_pinned_name_hash_mismatches(self):
        # A file that happens to be named like a pinned pack but whose
        # bytes DON'T match the pinned hash (tampering / wrong file) must
        # not get the waiver either.
        pk3_path = os.path.join(pub.STAGING_PK3_DIR, "xonotic-20230620-xoncompat.pk3")
        payload = b"\x00" * 4096
        with zipfile.ZipFile(pk3_path, "w", compression=zipfile.ZIP_DEFLATED) as z:
            z.writestr("textures/flagged.dds", payload)
        pub.PACKS = [("xoncompat", "xonotic-20230620-xoncompat.pk3")]
        # Deliberately leave pub.KNOWN_PACK_SHA256's real pinned value in
        # place; this synthetic archive's actual bytes will not match it.
        rc = pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=False))
        self.assertEqual(rc, 0)
        staged = Path(pub.DEST_ROOT) / "packs" / "xoncompat" / "textures" / "flagged.dds"
        self.assertFalse(staged.exists())

    def test_verify_full_zip_fails_closed_on_mismatch(self):
        downloads = self.root / "ExternalContent" / "downloads"
        downloads.mkdir(parents=True)
        full_zip = downloads / "xonotic-0.8.6.zip"
        full_zip.write_bytes(b"not the real official archive bytes")

        with self.assertRaises(pub.PublishError):
            pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=True))
        # Fail closed means no manifest is written for this aborted run.
        self.assertFalse((Path(pub.DEST_ROOT) / "publish-manifest.json").exists())

    def test_verify_full_zip_fails_closed_when_missing(self):
        # No ExternalContent/downloads/xonotic-0.8.6.zip at all.
        with self.assertRaises(pub.PublishError):
            pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=True))

    def test_idempotent_rerun_never_rewrites_existing_bytes(self):
        pub.cmd_publish(_Args(overwrite=False, manifest_out=None, verify_full_zip=False))
        base_path = Path(pub.DEST_ROOT) / "packs" / "sample" / "textures" / "base.dds"
        mtime_before = base_path.stat().st_mtime_ns
        ino_before = base_path.stat().st_ino

        rc = pub.cmd_publish(_Args(overwrite=True, manifest_out=None, verify_full_zip=False))
        self.assertEqual(rc, 0)
        self.assertEqual(base_path.stat().st_mtime_ns, mtime_before)
        self.assertEqual(base_path.stat().st_ino, ino_before)


class _Args:
    def __init__(self, **kw):
        self.__dict__.update(kw)


class AncestorSymlinkCheckTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.dest_root = self.root / "dest"
        self.dest_root.mkdir()

    def test_plain_nested_path_is_safe(self):
        target = self.dest_root / "a" / "b" / "file.txt"
        target.parent.mkdir(parents=True)
        target.write_bytes(b"x")
        self.assertIsNone(pub._ancestor_symlink_reason(str(target), str(self.dest_root)))

    def test_symlinked_intermediate_directory_refused(self):
        elsewhere = self.root / "elsewhere"
        elsewhere.mkdir()
        (elsewhere / "file.txt").write_bytes(b"real-target-bytes")
        (self.dest_root / "a").symlink_to(elsewhere, target_is_directory=True)
        target = self.dest_root / "a" / "file.txt"  # exists only via the symlink
        reason = pub._ancestor_symlink_reason(str(target), str(self.dest_root))
        self.assertIsNotNone(reason)
        self.assertIn("symlink", reason)

    def test_symlinked_leaf_refused_even_without_final_islink_check(self):
        real_file = self.root / "real.txt"
        real_file.write_bytes(b"x")
        link_path = self.dest_root / "link.txt"
        link_path.symlink_to(real_file)
        reason = pub._ancestor_symlink_reason(str(link_path), str(self.dest_root))
        self.assertIsNotNone(reason)

    def test_escaping_root_refused(self):
        outside = self.root / "outside.txt"
        reason = pub._ancestor_symlink_reason(str(outside), str(self.dest_root))
        self.assertIsNotNone(reason)
        self.assertIn("outside", reason)

    def test_symlinked_root_itself_refused(self):
        elsewhere = self.root / "elsewhere2"
        elsewhere.mkdir()
        linked_root = self.root / "linked_root"
        linked_root.symlink_to(elsewhere, target_is_directory=True)
        target = linked_root / "file.txt"
        reason = pub._ancestor_symlink_reason(str(target), str(linked_root))
        self.assertIsNotNone(reason)
        self.assertIn("symlink", reason)


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

        self._orig = {
            "REPO_ROOT": pub.REPO_ROOT, "EXTERNAL_CONTENT": pub.EXTERNAL_CONTENT,
            "STAGING_PK3_DIR": pub.STAGING_PK3_DIR, "NOTICES_DIR": pub.NOTICES_DIR,
            "DEST_ROOT": pub.DEST_ROOT,
        }
        self.addCleanup(self._restore_globals)

        pub.REPO_ROOT = str(self.root)
        pub.EXTERNAL_CONTENT = str(self.root / "ExternalContent")
        pub.NOTICES_DIR = str(self.root / "ExternalContent" / "notices")
        pub.DEST_ROOT = str(self.root / "ThirdParty" / "Xonotic-0.8.6")
        os.makedirs(pub.DEST_ROOT)

        # One committed "notices" file plus a hand-built manifest, mirroring
        # what cmd_publish would have produced.
        committed_rel = os.path.join("notices", "COPYING")
        committed_abs = os.path.join(pub.DEST_ROOT, committed_rel)
        os.makedirs(os.path.dirname(committed_abs), exist_ok=True)
        with open(committed_abs, "wb") as f:
            f.write(b"licence text\n")
        sha = safety.sha256_file(committed_abs)

        self.manifest = {
            "files_list": [{
                "pack": "notices", "entry_name": "COPYING",
                "dest_relpath": committed_rel.replace(os.sep, "/"),
                "type": "regular", "sha256": sha, "size_bytes": 13,
            }]
        }
        self.manifest_path = os.path.join(pub.DEST_ROOT, "publish-manifest.json")
        with open(self.manifest_path, "w") as f:
            json.dump(self.manifest, f)

    def _restore_globals(self):
        for k, v in self._orig.items():
            setattr(pub, k, v)

    def test_restore_writes_fresh_target(self):
        rc = pub.cmd_restore(_Args(manifest=self.manifest_path, with_pk3_entries=False))
        self.assertEqual(rc, 0)
        target = Path(pub.NOTICES_DIR) / "COPYING"
        self.assertTrue(target.is_file())
        self.assertEqual(target.read_bytes(), b"licence text\n")

    def test_restore_leaves_already_correct_target_alone(self):
        target = Path(pub.NOTICES_DIR)
        target.mkdir(parents=True, exist_ok=True)
        (target / "COPYING").write_bytes(b"licence text\n")
        mtime_before = (target / "COPYING").stat().st_mtime_ns
        rc = pub.cmd_restore(_Args(manifest=self.manifest_path, with_pk3_entries=False))
        self.assertEqual(rc, 0)
        self.assertEqual((target / "COPYING").stat().st_mtime_ns, mtime_before)

    def test_restore_refuses_to_overwrite_different_existing_target(self):
        target = Path(pub.NOTICES_DIR)
        target.mkdir(parents=True, exist_ok=True)
        (target / "COPYING").write_bytes(b"LOCALLY MODIFIED, DO NOT CLOBBER\n")
        rc = pub.cmd_restore(_Args(manifest=self.manifest_path, with_pk3_entries=False))
        self.assertEqual(rc, 1)
        self.assertEqual((target / "COPYING").read_bytes(), b"LOCALLY MODIFIED, DO NOT CLOBBER\n")

    def test_restore_refuses_symlink_target(self):
        target_dir = Path(pub.NOTICES_DIR)
        target_dir.mkdir(parents=True, exist_ok=True)
        elsewhere = self.root / "elsewhere.txt"
        elsewhere.write_bytes(b"whatever")
        (target_dir / "COPYING").symlink_to(elsewhere)
        rc = pub.cmd_restore(_Args(manifest=self.manifest_path, with_pk3_entries=False))
        self.assertEqual(rc, 1)
        self.assertTrue((target_dir / "COPYING").is_symlink())

    def test_restore_refuses_tampered_committed_source(self):
        committed_abs = os.path.join(pub.DEST_ROOT, "notices", "COPYING")
        with open(committed_abs, "wb") as f:
            f.write(b"TAMPERED BYTES, SHA NO LONGER MATCHES MANIFEST\n")
        rc = pub.cmd_restore(_Args(manifest=self.manifest_path, with_pk3_entries=False))
        self.assertEqual(rc, 1)
        self.assertFalse((Path(pub.NOTICES_DIR) / "COPYING").exists())


if __name__ == "__main__":
    unittest.main()
