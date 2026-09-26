# Tests and Evidence Limits

## Baseline Session Result — 2026-09-20

| Check | Result | What does it prove? |
|---|---|---|
| C# host API compilation | Passed for 26 runtime files and 4 Editor files | Type correctness and reference-library APIs only |
| asmdef + builtin packages | Passed | Reference names and local package versions |
| C# BSP/geometry checks | 53 assertions passed | 49 synthetic + 4 on two original maps |
| Python | 57 tests passed | Protection for extraction, provenance, fixtures and HTTP ranges, and the resource index |
| .meta GUIDs | Check completed without errors | Completeness and no duplicate GUIDs for original code and assets |
| Original resources | 207 files / 203,918,053 bytes with no differences | Files match the index; this does not establish licensing completeness or runtime functionality |
| Unity import/compile | Blocked at activation 401 | No compilation result from the Editor |
| Editor tests / Play Mode | Not run | The code exists, but this is not counted as a pass |
| APK/device/visual/audio | Not run | No evidence of phone runtime behavior or visual match |

Successful host compilation does not verify the shader compiler, asset importing, the actual
MonoBehaviour lifecycle ordering, or IL2CPP/stripping.

## Reproducible Commands

```bash
bash tests/run_all.sh mono mcs \
  ExternalContent/maps/maps/_hudsetup.bsp \
  ExternalContent/maps/maps/boil.bsp
python3 tools/asset_meta.py
python3 tools/content/verify_resources.py

python3 tools/host_compile.py --editor-data "$UNITY_EDITOR_DATA" \
  --ui-dll "$LOCAL_UNITY_UI_DLL"
```

The map paths may be omitted to run only the synthetic checks. Do not copy the maps into the
synthetic fixtures directory or modify them to make the test pass. Any parsing failure for an
original test map genuinely fails the test; there is no catch that turns failure into success.

## Results for the Two Original Maps

From `Xonotic/data/xonotic-20230620-maps.pk3` in official release 0.8.6:

| Map | Entities | Vertices in file | Surfaces | models | Generated model0 vertices | Visible triangles | Collision triangles |
|---|---:|---:|---:|---:|---:|---:|---:|
| `_hudsetup` | 5 | 1359 | 93 | 2 | 1695 | 1661 | 1661 |
| `boil` | 92 | 17021 | 2969 | 6 | 18335 | 12682 | 12654 |

The C# reader and geometry generator returned zero diagnostics for these files. **This does not
mean that no features are missing**: the Editor importer warns about unimplemented submodels and
entities. Boil contains 7 deathmatch spawn points. The scene was not visually inspected in Unity.
SHA256 and the source of every file are recorded in the resource manifests. BSP extraction verified
the member CRC, but did not verify the SHA512 of the complete official archive.

## Written Editor and Play Mode Tests

- Editor: friction retains vertical velocity, armor distribution, splash limit, bootstrap scene with no missing script, shader presence, collision and spawn-point synthetic patch, and repeated importing preserves GUIDs.
- Play Mode: enemy and pickup counts, actual walking and jumping, ground collision, hitscan and projectile hits, kill scores, temporary respawn, armor, match reset, and movement stopping when paused.
- The report `Artifacts/playtest-result.json` is generated only by the actual runner.
- A test-input injection does not prove that phone touch works; an independent device test is required.

## Phone Checklist Before Any Player Release

- [ ] Clean installation and startup without a crash; package/architecture/signature are correct.
- [ ] Movement, looking, and firing simultaneously with independent fingers.
- [ ] Safe area, landscape rotation, pause, loss of focus, and return without stuck touch input.
- [ ] Ground and wall collision; projectiles do not pass through a nearby wall; splash does not pass through a wall.
- [ ] Ammunition, pickups, armor, scores, respawning, and match reset.
- [ ] Rendering, shaders, and audio; frame rate, memory, and temperature on a named device.
- [ ] Record actual Xonotic gaps; do not rely solely on a successful development arena.
