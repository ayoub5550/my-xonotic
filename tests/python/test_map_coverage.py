"""
Unit tests for tools/content/map_coverage.py.

Stdlib only (unittest + zipfile + struct); no pytest/third-party dependency
required, though pytest can also collect this file if present.

Run: python3 tests/python/test_map_coverage.py

Covers:
  - entity-lump tokenizer correctness (classnames, multiple properties,
    "//" comments, malformed blocks recorded as warnings not exceptions)
  - classification into spawn/pickup/mover/trigger/target/light/other
  - malformed/truncated/oversized-offset BSP header rejection (bad magic,
    unsupported version, truncated header, lump offset/length past EOF,
    oversized declared entities-lump length)
  - real (small, synthetic-only) archive inspection: multiple .bsp entries
    inventoried from one crafted in-memory .pk3, a suspicious entry
    (path traversal) skipped without aborting the whole archive, and a
    malformed .bsp entry recorded as a per-map failure rather than raised
  - the single-pass central-directory guarantee (inspect_entries called
    exactly once per archive regardless of BSP entry count)
"""
from __future__ import annotations

import io
import os
import struct
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "content"))
import map_coverage as mc  # noqa: E402
import pk3_safety as safety  # noqa: E402
import fixture_bsp  # noqa: E402


def _entities_bsp(entities_text: bytes, *, version: int = mc.IBSP_SUPPORTED_VERSION,
                   magic: bytes = mc.IBSP_MAGIC) -> bytes:
    """Builds a minimal IBSP file whose only populated lump is Entities,
    for tests that only care about entity-lump behaviour. All other lumps
    are zero-length, which BspReader.cs / this module both tolerate.
    """
    lumps_data = [b""] * mc.LUMP_COUNT
    lumps_data[mc.LUMP_ENTITIES] = entities_text
    header_size = mc.HEADER_SIZE
    offset = header_size
    dir_entries = []
    body = b""
    for data in lumps_data:
        dir_entries.append((offset, len(data)))
        body += data
        offset += len(data)
    header = magic + struct.pack("<i", version)
    for off, length in dir_entries:
        header += struct.pack("<2i", off, length)
    return header + body


def _zip_bytes(entries) -> bytes:
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for name, payload in entries:
            zf.writestr(name, payload)
    return out.getvalue()


class TestClassification(unittest.TestCase):
    def test_spawn_classes(self):
        self.assertEqual(mc.classify_classname("info_player_deathmatch"), mc.CATEGORY_SPAWN)
        self.assertEqual(mc.classify_classname("info_player_start"), mc.CATEGORY_SPAWN)

    def test_pickup_classes(self):
        self.assertEqual(mc.classify_classname("item_health_small"), mc.CATEGORY_PICKUP)
        self.assertEqual(mc.classify_classname("weapon_vortex"), mc.CATEGORY_PICKUP)

    def test_mover_classes(self):
        self.assertEqual(mc.classify_classname("func_door"), mc.CATEGORY_MOVER)
        self.assertEqual(mc.classify_classname("func_pointparticles"), mc.CATEGORY_MOVER)

    def test_trigger_classes(self):
        self.assertEqual(mc.classify_classname("trigger_push"), mc.CATEGORY_TRIGGER)
        self.assertEqual(mc.classify_classname("trigger_multiple"), mc.CATEGORY_TRIGGER)

    def test_target_and_light_and_worldspawn(self):
        self.assertEqual(mc.classify_classname("target_position"), mc.CATEGORY_TARGET)
        self.assertEqual(mc.classify_classname("path_corner"), mc.CATEGORY_TARGET)
        self.assertEqual(mc.classify_classname("light"), mc.CATEGORY_LIGHT)
        self.assertEqual(mc.classify_classname("light_flicker"), mc.CATEGORY_LIGHT)
        self.assertEqual(mc.classify_classname("worldspawn"), mc.CATEGORY_WORLDSPAWN)

    def test_other_and_empty(self):
        self.assertEqual(mc.classify_classname("misc_model"), mc.CATEGORY_OTHER)
        self.assertEqual(mc.classify_classname(""), mc.CATEGORY_OTHER)

    def test_supported_classnames_are_exactly_the_current_source_subset(self):
        # dev.4 import paths + dev.5 team/race/assault spawns; does not
        # assert map-wide gameplay parity.
        self.assertEqual(
            mc.SUPPORTED_CLASSNAMES,
            {"info_player_deathmatch", "info_player_start",
             "info_player_team1", "info_player_team2", "info_player_team3",
             "info_player_team4", "info_player_race", "info_player_attacker",
             "info_player_defender", "trigger_push",
             "trigger_teleport", "trigger_hurt", "item_health_small",
             "item_health_medium", "item_health_big", "item_health_mega",
             "item_armor_small", "item_armor_medium", "item_armor_big",
             "item_armor_mega", "item_rockets"},
        )


class TestEntityParser(unittest.TestCase):
    def test_basic_multi_entity_multi_property(self):
        text = (
            '{\n"classname" "worldspawn"\n"message" "hi"\n}\n'
            '{\n"classname" "info_player_deathmatch"\n"origin" "0 0 0"\n"angle" "90"\n}\n'
        )
        entities, warnings = mc.parse_entities(text)
        self.assertEqual(len(entities), 2)
        self.assertEqual(warnings, [])
        self.assertEqual(entities[0].get("classname"), "worldspawn")
        self.assertEqual(entities[1].get("angle"), "90")

    def test_line_comments_are_skipped(self):
        text = '{\n// a comment\n"classname" "worldspawn"\n// trailing\n}\n'
        entities, warnings = mc.parse_entities(text)
        self.assertEqual(len(entities), 1)
        self.assertEqual(entities[0].get("classname"), "worldspawn")
        self.assertEqual(warnings, [])

    def test_stray_closing_brace_warns_not_raises(self):
        text = '}\n{\n"classname" "worldspawn"\n}\n'
        entities, warnings = mc.parse_entities(text)
        self.assertEqual(len(entities), 1)
        self.assertTrue(any("stray" in w for w in warnings))

    def test_nested_open_brace_discards_previous_incomplete_block(self):
        text = '{\n"classname" "a"\n{\n"classname" "b"\n}\n'
        entities, warnings = mc.parse_entities(text)
        self.assertEqual(len(entities), 1)
        self.assertEqual(entities[0].get("classname"), "b")
        self.assertTrue(any("nested" in w for w in warnings))

    def test_unterminated_quote_truncates_without_raising(self):
        text = '{\n"classname" "worldspawn'
        entities, warnings = mc.parse_entities(text)
        self.assertEqual(entities, [])
        self.assertTrue(any("unterminated quoted token" in w for w in warnings))

    def test_unterminated_block_at_eof_discarded(self):
        text = '{\n"classname" "worldspawn"\n'
        entities, warnings = mc.parse_entities(text)
        self.assertEqual(entities, [])
        self.assertTrue(any("unterminated block" in w for w in warnings))

    def test_escaped_quote_and_backslash_in_token(self):
        text = '{\n"classname" "quote\\"inside"\n}\n'
        entities, _ = mc.parse_entities(text)
        self.assertEqual(entities[0].get("classname"), 'quote"inside')

    def test_entity_budget_enforced(self):
        text = ('{\n"classname" "x"\n}\n') * 5
        with mock.patch.object(mc, "MAX_ENTITIES", 3):
            with self.assertRaises(mc.MapCoverageError):
                mc.parse_entities(text)

    def test_token_length_budget_enforced(self):
        text = '{\n"classname" "' + ("x" * 100) + '"\n}\n'
        with mock.patch.object(mc, "MAX_TOKEN_LENGTH", 10):
            with self.assertRaises(mc.MapCoverageError):
                mc.parse_entities(text)


class TestHeaderAndEntitiesLumpBounds(unittest.TestCase):
    def test_bad_magic_rejected(self):
        data = _entities_bsp(b'{\n"classname" "worldspawn"\n}\n\x00', magic=b"NOPE")
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(data)

    def test_unsupported_version_rejected(self):
        data = _entities_bsp(b'{\n"classname" "worldspawn"\n}\n\x00', version=999)
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(data)

    def test_truncated_header_rejected(self):
        data = mc.IBSP_MAGIC + struct.pack("<i", mc.IBSP_SUPPORTED_VERSION) + b"\x00" * 10
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(data)

    def test_empty_buffer_rejected(self):
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(b"")

    def test_lump_offset_past_eof_rejected(self):
        data = bytearray(_entities_bsp(b'{\n"classname" "worldspawn"\n}\n\x00'))
        # Corrupt the entities lump's directory entry to point far past EOF,
        # matching fixture_bsp.build_huge_lump_count_attack()'s shape.
        entry_off = 8 + mc.LUMP_ENTITIES * 8
        struct.pack_into("<2i", data, entry_off, mc.HEADER_SIZE, 0x7FFFFFF0)
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(bytes(data))

    def test_negative_lump_offset_rejected(self):
        data = bytearray(_entities_bsp(b'{\n"classname" "worldspawn"\n}\n\x00'))
        entry_off = 8 + mc.LUMP_ENTITIES * 8
        struct.pack_into("<2i", data, entry_off, -1, 4)
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(bytes(data))

    def test_oversized_declared_entities_lump_rejected_before_decode(self):
        # The directory claims a declared length far beyond the safety
        # ceiling; parse_header() must have already bounds-checked
        # offset+length against the real buffer size, so this can only be
        # constructed by also having a large-enough real buffer -- but the
        # entities-lump ceiling check in extract_entities_text() must still
        # fire, independent of the (smaller, real) MAX_ENTITY_LUMP_BYTES.
        text = b'{\n"classname" "worldspawn"\n}\n\x00'
        data = _entities_bsp(text)
        version, lumps = mc.parse_header(data)
        with mock.patch.object(mc, "MAX_ENTITY_LUMP_BYTES", len(text) - 1):
            with self.assertRaises(mc.MapCoverageError):
                mc.extract_entities_text(data, lumps)

    def test_valid_minimal_bsp_parses(self):
        text = b'{\n"classname" "worldspawn"\n}\n{\n"classname" "info_player_start"\n"origin" "0 0 0"\n}\n\x00'
        data = _entities_bsp(text)
        record = mc.inventory_one_bsp("maps/synthetic.bsp", data)
        self.assertEqual(record["entity_count"], 2)
        self.assertEqual(record["category_counts"][mc.CATEGORY_SPAWN], 1)
        self.assertIn("info_player_start", record["supported_classnames_present"])
        self.assertEqual(record["unsupported_classnames_present"], [])


class TestFixtureBspIntegration(unittest.TestCase):
    """Cross-checks against tools/content/fixture_bsp.py's existing
    malformed-input fixtures, so this module rejects the same hostile
    shapes the C# BspReader regression fixtures already cover."""

    def test_fixture_bad_magic(self):
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(fixture_bsp.build_bad_magic())

    def test_fixture_unsupported_version(self):
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(fixture_bsp.build_unsupported_version())

    def test_fixture_truncated_header(self):
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(fixture_bsp.build_truncated_header())

    def test_fixture_huge_lump_count_attack(self):
        with self.assertRaises(mc.MapCoverageError):
            mc.parse_header(fixture_bsp.build_huge_lump_count_attack())

    def test_fixture_good_bsp_inventories_one_spawn(self):
        data = fixture_bsp.build_fixture_bsp()
        record = mc.inventory_one_bsp("maps/good.bsp", data)
        self.assertEqual(record["category_counts"][mc.CATEGORY_SPAWN], 1)
        self.assertIn("info_player_deathmatch", record["supported_classnames_present"])


class TestRealArchiveInspection(unittest.TestCase):
    """Builds small, wholly synthetic in-memory .pk3 archives (never real
    Xonotic content) to exercise the actual zipfile-backed code path."""

    def _write_pk3(self, tmp_dir: str, entries) -> str:
        path = os.path.join(tmp_dir, "test.pk3")
        with open(path, "wb") as f:
            f.write(_zip_bytes(entries))
        return path

    def test_multiple_maps_inventoried(self):
        good = fixture_bsp.build_fixture_bsp()
        other_text = b'{\n"classname" "worldspawn"\n}\n{\n"classname" "trigger_push"\n"model" "*1"\n}\n\x00'
        other = _entities_bsp(other_text)
        with tempfile.TemporaryDirectory() as d:
            pk3_path = self._write_pk3(d, [
                ("maps/good.bsp", good),
                ("maps/other.bsp", other),
                ("textures/notes.txt", b"not a map, must never be opened as one"),
            ])
            report = mc.inventory_pk3(pk3_path)
            self.assertEqual(report["bsp_maps_found"], 2)
            self.assertEqual(report["bsp_maps_inventoried"], 2)
            self.assertEqual(report["bsp_maps_failed"], 0)
            names = sorted(m["map_entry"] for m in report["maps"])
            self.assertEqual(names, ["maps/good.bsp", "maps/other.bsp"])
            self.assertEqual(report["archive_sha256"], safety.sha256_file(pk3_path))

    def test_suspicious_entry_skipped_not_fatal(self):
        good = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as d:
            pk3_path = self._write_pk3(d, [
                ("maps/good.bsp", good),
                ("../evil.bsp", good),  # path traversal: must be skipped, not raised.
            ])
            report = mc.inventory_pk3(pk3_path)
            self.assertEqual(report["bsp_maps_inventoried"], 1)
            self.assertEqual(len(report["failed_maps"]), 1)
            self.assertIn("evil.bsp", report["failed_maps"][0]["map_entry"])

    def test_malformed_bsp_entry_is_recorded_not_raised(self):
        good = fixture_bsp.build_fixture_bsp()
        bad = fixture_bsp.build_bad_magic()
        with tempfile.TemporaryDirectory() as d:
            pk3_path = self._write_pk3(d, [
                ("maps/good.bsp", good),
                ("maps/bad.bsp", bad),
            ])
            report = mc.inventory_pk3(pk3_path)
            self.assertEqual(report["bsp_maps_inventoried"], 1)
            self.assertEqual(len(report["failed_maps"]), 1)
            self.assertEqual(report["failed_maps"][0]["map_entry"], "maps/bad.bsp")

    def test_oversized_entry_rejected_without_full_read(self):
        good = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as d:
            pk3_path = self._write_pk3(d, [("maps/good.bsp", good)])
            report = mc.inventory_pk3(pk3_path, max_bsp_entry_bytes=len(good) - 1)
            self.assertEqual(report["bsp_maps_inventoried"], 0)
            self.assertEqual(len(report["failed_maps"]), 1)

    def test_non_bsp_entries_never_opened(self):
        # A .txt entry with a name ending in .bsp-lookalike must not be
        # treated as a map; only exact ".bsp" (case-insensitive) suffix
        # entries are ever candidates.
        good = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as d:
            pk3_path = self._write_pk3(d, [
                ("maps/good.bsp", good),
                ("maps/notabsp.txt", b"definitely not a bsp"),
                ("maps/UPPER.BSP", good),
            ])
            report = mc.inventory_pk3(pk3_path)
            names = sorted(m["map_entry"] for m in report["maps"])
            self.assertEqual(names, ["maps/UPPER.BSP", "maps/good.bsp"])

    def test_missing_file_raises(self):
        with self.assertRaises(mc.MapCoverageError):
            mc.inventory_pk3("/no/such/archive.pk3")

    def test_single_pass_central_directory_inspection(self):
        """inspect_entries() must be called exactly once per archive,
        regardless of how many .bsp entries it contains -- the O(n^2)
        pattern this module is designed to avoid."""
        good = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as d:
            pk3_path = self._write_pk3(d, [
                (f"maps/m{i}.bsp", good) for i in range(5)
            ])
            with mock.patch.object(safety, "inspect_entries", wraps=safety.inspect_entries) as spy:
                report = mc.inventory_pk3(pk3_path)
                self.assertEqual(report["bsp_maps_inventoried"], 5)
                self.assertEqual(spy.call_count, 1)


class TestCliOutput(unittest.TestCase):
    def test_cli_writes_summary_and_per_map_files(self):
        good = fixture_bsp.build_fixture_bsp()
        with tempfile.TemporaryDirectory() as d:
            pk3_path = os.path.join(d, "test.pk3")
            with open(pk3_path, "wb") as f:
                f.write(_zip_bytes([("maps/good.bsp", good)]))
            out_dir = os.path.join(d, "out")
            rc = mc.main(["inventory", pk3_path, "--out-dir", out_dir])
            self.assertEqual(rc, 0)
            self.assertTrue(os.path.isfile(os.path.join(out_dir, "summary.json")))
            self.assertTrue(os.path.isfile(os.path.join(out_dir, "good.bsp.json")))

    def test_cli_nonzero_exit_on_missing_file(self):
        rc = mc.main(["inventory", "/no/such/file.pk3"])
        self.assertEqual(rc, 3)


if __name__ == "__main__":
    unittest.main()
