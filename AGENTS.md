# my-xonotic — developer and agent handoff

## Read this first: current evidence, 2026-09-20

**Goal:** genuinely bring Xonotic to Unity/Android, not a renamed LibreQuake game.
**Current checkpoint:** `0.1.0-dev.1`, source + a bounded original upstream resource
pack. It is NOT the complete game, a verified Unity project import, or an APK.

| Gate | Current result |
|---|---|
| Host C# API/type check | PASS: runtime + Editor sources against local Unity 2022.3.62f3 assemblies |
| Named asmdef / builtin package check | PASS; UGUI assembly is `UnityEngine.UI`, not `Unity.ugui` |
| Standalone BSP checks | PASS: 53 assertions including two actual release maps; 49 synthetic-only |
| Python intake/range/resource-index checks | PASS: 57 tests |
| Source asset GUID check | PASS: generated once and checked for missing/duplicate GUIDs |
| Unity Editor import/compilation | BLOCKED before project import: local activation returned HTTP 401 |
| Editor tests / Play Mode | Written and host-compiled, NOT RUN |
| Android build / installation / device play | NOT RUN; no APK produced |

The local Editor existed; matching Android support/JDK/NDK/SDK were installed during
this task. Installation checks are not build proof. Do not use an earlier
`my-librequake` result as proof for this project.

## Owner instructions and repository discipline

- All code, structure, real resources, development records and releases belong here.
- **Build locally only.** No Unity Cloud Build, GitHub Actions builds, remote build
  farms, paid services or purchases.
- Owner explicitly asked for **actual resources in Git**, not just download tools.
  Keep upstream originals/source in `ThirdParty/Xonotic/`, with their own notices
  and manifests, outside Unity `Assets`; preserve upstream copyrights.
- Owner then explicitly approved publication without waiting for further
  source-matching research. The resource index covers **207 unmodified upstream
  files / 203,918,053 bytes**, including exports, authoring sources and notices.
  Keep unresolved source correspondence visible; do not turn this approval into
  a statement of legal clearance or completed runtime integration.
- Owner-facing docs Arabic; engineering names/comments English.
- No credentials, activation files, account logs, keystores or APKs in Git.
- Do not copy GPL engine/QuakeC implementations into original MIT source.
- Original-fixture APK distribution and upstream-content distribution are separate
  gates. No assertion that an aggregate resource repository resolves Unity/GPL terms.
- Work on `feat/local-unity-android` (or a new isolated task branch), not directly on
  `main`. Fetch/reconcile before each push. Do not force-push shared work.
- This repo was empty at intake. `main` carries the bootstrap overview; implementation
  is on the development branch/PR. Do not mistake a main-only checkout for lost work.
- Update this handoff, test evidence and CHANGELOG with each tangible increment.
- One Unity instance at a time. Worker assignments from the initial task are finished;
  no lasting parallel file ownership is implied.

## Layout and entry points

- `Assets/MyXonotic/Runtime/Gameplay/`: original, approximate practice arena logic.
- `Runtime/Content/Bsp/`: pure managed IBSP v46 parser, coordinates, geometry.
- `Runtime/Content/`: typed `ImportedArena` and `BspSpawnPoint` markers.
- `Editor/Import/BspImportPipeline.Import(path)`: worldspawn + spawn marker import.
- `Editor/LocalBuild`: Configure / CreateDevelopmentScene / ImportExternalBsp /
  BuildAndroid / BuildLinux.
- `Editor/LocalTests.Run`: Editor assertions including repeat-import GUID stability.
- `Editor/LocalPlaytest.Run`: actual frame/physics smoke driver, runs WITHOUT `-quit`.
- `tools/local_unity.py`: local tasks, timeout, process-group stop, exclusive project lock.
- `tools/host_compile.py`: source/API check ONLY; no Editor, IL2CPP or shaders.
- `tools/content/`: bounded archive tools, optional fixed reference fetch, resource verifier.
- `ThirdParty/Xonotic/`: real upstream material, licences, source/provenance and limitations.

Unity 2022.3.62f3 is the current toolchain match, not a recommendation to ship an
unsupported Editor indefinitely. Review a supported LTS before production.
Android defaults are engineering choices: ARM64/IL2CPP, landscape, OpenGLES3,
minimum API 26, target API 36, development/debug signing, no custom keystore.

## Reproduce current checks

```bash
# Use explicit Mono/mcs paths as the first two args if not on PATH.
bash tests/run_all.sh mono mcs \
  ThirdParty/Xonotic/maps-pk3/maps/_hudsetup.bsp \
  ThirdParty/Xonotic/maps-pk3/maps/boil.bsp

python3 tools/asset_meta.py
python3 tools/content/verify_resources.py
python3 tools/host_compile.py --editor-data "$UNITY_EDITOR_DATA" \
  --ui-dll "$LOCAL_UNITY_UI_DLL"
```

The host check used an existing locally compiled Unity UGUI DLL as a reference.
It is NOT committed. `csc` wrappers can contain stale vendor build paths: invoke
the actual local Mono executable and `lib/mono/4.5/csc.exe`. Do not mix monolithic
`Managed/UnityEngine.dll` with modular `Managed/UnityEngine/*.dll`.

See `docs/TESTING.md` for sample-map counts and what these checks cannot prove.
The optional fetcher reproduces official BSP hashes using exact HTTP byte ranges;
full release SHA512 was not verified. It is never run implicitly by a build.

## Important implementation boundaries

- Original 40×40 practice arena; 3 test bots, 5 pickups, 3 prototype guns. Not the
  full Xonotic arsenal, character art, AI, match modes, campaign or networking.
- Base movement constants reference public `physicsX.cfg`; advanced air control,
  ramp/step behavior and timing are not matched. Fixed clamped movement steps are
  a development approximation, not deterministic network simulation.
- Coordinate convention: source `(x,y,z)` → Unity `(x,z,y)/32`. The scale is ours,
  not an upstream physical-unit definition. Hull origin is **not feet**:
  `ContentBridge` subtracts `24/32` on Y for a feet-based controller.
- BSP parses/indexes polygons/meshes and tessellates quadratic patches. Surface
  flags and content flags are independent. `SURF_SKIP=0x200`, not `0x10`.
  Invisible NODRAW caulk can still be solid.
- Collision uses eligible face triangles, NOT full original brush volumes/BSP
  collision. Do not call imported-map traversal verified.
- Only worldspawn becomes static geometry. Inline movers/triggers are not baked
  into the world. Unsupported entity classes warn, not silently pretend to work.
- Imported scenes use development rules. Original pickups, jump pads,
  teleporters, hazards and targets are not implemented. No fake pickup placement.
- No material/texture/lightmap/sky/IQM/MD3 runtime importer yet. Resource presence
  is not runtime support. Shader code here is debug vertex/tint rendering only.
- The four included upstream files named `*.md3` have `INTERQUAKEMODEL\0`
  headers (IQM). Dispatch any future model importer by magic, not extension.
- Typed marker references avoid reflection/IL2CPP stripping; runtime shaders live
  in Resources and are pinned by LocalBuild. Shader compilation remains untested.
- Stable `.meta` GUIDs are committed. `tools/asset_meta.py --write-missing` seeds
  new source assets only and never rewrites an existing GUID.

## Next actions, in order

1. Resolve legitimate local Unity activation with the owner privately. Do not
   retry guessed accounts, reuse unrelated mailbox access, or publish credentials.
2. On an activated machine run compile → Editor checks → real Play Mode smoke.
   Inspect errors and screenshots, then run Android build and validate its APK.
3. Run on a real ARM64 Android device: independent move/look/fire, safe area,
   pause/background/resume, shots/walls, score/respawn, performance and thermals.
4. Verify remaining source/attribution gaps in the upstream resource manifests
   before packaging; preserve all rights notices and corresponding source.
5. Connect actual texture/lightmap data to importer and implement one Boil gameplay
   feature at a time (trigger_push/teleport/hurt, pickups), with original-vs-port
   evidence. Do not broaden the prototype and relabel it Xonotic.
6. Record every outcome separately: source, import, play, packaging and device.
