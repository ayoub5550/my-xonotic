# my-xonotic — developer and agent handoff

## dev.5 full-map checkpoint — 2026-09-21 (branch `feat/unity-dev5-full-game`)

Read `docs/UNITY-DEV5.md` first. First APK bundling all 29 importable official
maps + code-built main menu + per-map music: `my-xonotic-full.apk`,
322,882,281 bytes, versionCode 5, SHA256
`0e34d259539458ff091f100fb78648530f0ccdf72f693126bb73613db54c887f`,
receipt `docs/unity-dev5-build-2026-09-21.json`, map report
`docs/unity-dev5-maps-2026-09-21.json` (29/29 imported, 0 failed).
Build: `XONOTIC_ALL_MAPS=1 XONOTIC_VERSION_CODE=5 python3 tools/local_unity.py android`
(~12 min on 17 cores). Imported textures are ETC2 + mipmaps and shared across
maps (`Editor/Import/ImportedTexturePolicy.cs`); generated scenes/catalog live
under git-ignored `Assets/MyXonotic/Generated/`, so `prepare-maps` or the
FullGame build must run before opening the menu scene on a fresh clone.
`docs/UNITY-DEV5-WIP.md` describes code that was never pushed; do not assume
weapons/Erebus/doors exist. Still NOT complete Xonotic; no device test of this hash.

## Full-game continuation wave — dev.4 source checkpoint

Owner reiterated full game after dev.3; a Boil-only release is not the endpoint.
Do not manufacture a >1GB APK with unused files to simulate completeness.
Current wave: original-map pickups, offline Deathmatch lifecycle, and a complete
official-map/entity coverage inventory. Full weapons/animation/models/modes/network
and Android validation remain separate acceptance gates.

Workers finished; parent integrated and verified actual Unity Editor/Play Mode.
25 Boil pickups (health/armor/rocket ammo) survive scene serialization and collect
through actual physics. Pickup visuals are development spheres, not original art.
Offline match limit/winner/tie/freeze/restart is wired into the arena and HUD.
Current passes: 55 BSP, 36 MD3, 68 match rules, 172 Boil entity-data, 130 Python,
22 Editor, 59 sky, 131 integration, 44 gameplay Play Mode, 12 synthetic Play Mode,
100 original-Boil smoke assertions. No Android device/visual parity claim.
All-map inventory reads 31 BSP files incl internal stub; no all-map playability claim.
Read `docs/FULL-GAME-GATES.md`. Local dev.4 Android build succeeded from
`3322efb2a3260c28edd4b36f6523ee68297d7e48`, 72,377,724 bytes, versionCode 4.
SHA256 `9605df41cee8e68d7dd24d33953245b3de0df5979a74c6ebce06972a3b02e12a`.
Receipt: `docs/unity-dev4-build-2026-09-21.json` (0 errors, 1 build warning).
APK CRC/hash/manifest/ARM64 and v2 signature verified; same certificate as dev.3,
still different from dev.2. No install/update/device test performed.
Keep dev.3 evidence below historical when reporting this wave.

## Verified continuation checkpoint — 2026-09-21

Current working branch: `feat/unity-android-continuation`, based on `f59e34a`.
Read this section and `docs/LOCAL_BUILD.md` before historical notes below.
New bounded dev.3 APK built and verified; NOT complete Xonotic.
Read `docs/UNITY-CONTINUATION.md` and the dated build receipt for evidence.

- APK 72,332,393 bytes; SHA256
  `cca7f66e67f18c229f889cca1b70a3427943ee1217f3fc650c5aafa4c3bd33c0`.
- Build source `45e7e1c9c711142b8c4037023b6b0eaa04b6c4c8`; versionCode 3.
- 55 BSP checks, 36 synthetic MD3 / 40 with real Rocket, 90 Python, 22 Editor,
  59 sky-import, 12 synthetic Play Mode checks passed; Boil seven-spawn smoke passed.
- Actual Unity weapon manifest confirms static view-model IQM Blaster + MD3 Rocket,
  both resolved original diffuse skins. No animation or full-game claim.
- APK v2 debug signature verified. Certificate differs from released dev.2:
  in-place update will fail; never silently uninstall/delete app data.
- Graphical Camera.Render still hangs in llvmpipe; no visual/device validation.
- Complete official 0.8.6 archive SHA512 verified, selected extraction only.

Completed scope: sky editor-preview routing, static original MD3 weapon support,
selected DDS skins with provenance, and hardened local build verification.
Only the parent launches Unity; workers finished.

- `tools/local_unity.py` now requires a fresh invocation-bound build receipt,
  matching target/output, nonempty artifact, byte count and SHA256 for Android
  and Linux. Editor compile uses an executeMethod completion marker; Editor
  tests and playtests cannot reuse stale result files.
- `prepare_unity_textures.py` accepts repeatable `--texture` content paths,
  validates containment and actual DDS format, stages conversions, and retains
  earlier source/hash entries. It does not claim full texture/runtime support.
- Full original assets on disk and complete gameplay are separate gates.
  Do not rename a bounded development build as a complete Xonotic port.
- Source/binary resource provenance and licence compatibility remain independent
  review requirements; keep upstream art notices intact.
- A user-reported test of the earlier experimental APK is not a device test of
  any newly built version. Never transfer test claims between APK hashes.

## Current direction — 2026-09-21

Owner explicitly rejected the native/DarkPlaces approach and requested returning
to Unity. Resume the original C# implementation here; native/offline release
history is retained separately, not the current delivery path.

Active task branch: `feat/unity-original-map`, based on
`wip/textured-bsp-import` (`59b3d5c`). First milestone is original Boil geometry,
textures/lightmaps, integrated LibreQuake-style touch, original weapon visuals
where supported, and basic traversal. This is a staged reimplementation, NOT
full Xonotic; retain explicit missing-feature lists.

Parent owns LocalBuild/bootstrap/playtests/docs. Parallel workers currently own
importer/content BSP, touch Player/Hud/Layout, new IQM weapon importer/view, and
new BSP trigger importer/runtime. Only parent launches Unity.

All builds remain LOCAL. A single APK must contain its required content with
no resource download at runtime. Original-art distribution is owner-authorized,
but attribution/source correspondence and Unity/GPL compatibility are separate
review matters, not magically solved by that permission. Preserve notices and
source provenance. No DarkPlaces/QuakeC code is copied into original C#.

Use `XONOTIC_INCLUDE_EXTERNAL=1` and `XONOTIC_BSP=<boil.bsp>` for the imported-map
APK; the default remains a clearly named synthetic development fixture.
Original content roots can be provided through `XONOTIC_CONTENT_ROOTS`.
`LocalBuild.BuildAndroid` must NOT replace a requested imported scene with the
plain development arena (the previous method always did).

### Verified checkpoint: Unity 0.1.0-dev.2 (2026-09-21)
- Local ARM64/IL2CPP/GLES3 APK built, 51,114,182 bytes, versionCode 2,
  `com.ayoub.myxonotic` (distinct from native `com.ayoub.xonotic`).
- SHA256 `3f82420161a2b23388b46eb8a4d283e721ff16321681830cd11d30bf98f1d753`.
- v2 debug signature verified; SDK26 minimum / target36. APK includes
  Boil assets, 2 weapon prefabs, audio and upstream notices; no runtime download.
- 55 standalone C# checks (real-map winding regression), 68 Python tests,
  22 real Unity Editor checks; original resource index 207 files verified.
- Actual Unity Play Mode synthetic mechanics smoke: 12 checks passed.
- Actual imported Boil headless Play Mode: all 7 spawn points grounded, movement,
  jump, weapon firing and pause passed. World textures/materials referenced.
- Graphical host Camera.Render capture hung in llvmpipe and timed out, followed
  by shutdown crash. No usable visual capture; headless simulation passed.
- **Android installation, touch input, visuals, thermals and gameplay UNTESTED.**

Critical corrections:
- Shader parser must skip newline tokens between a material name and `{`;
  without this, real exomorphx scripts were ignored and textures fell back.
- Real BSP polygon meshverts already have native cross-product opposite vertex
  normals; Y/Z axis swap makes source a,b,c face correctly in Unity. Earlier
  blanket a,c,b reversal made players fall through floors. Patch grid triangles
  are separate and retain their existing winding. Synthetic fixture was corrected
  to represent actual compiled data; real-map cross/normal checks prevent regression.
- Weapon IQM triangles had the same inherited winding mistake; fixed separately.
  Blaster uses original static v_laser IQM + decoded laser DDS skin. Rocket uses
  a world-model fallback, currently without resolved full skin; real v_rl is MD3
  and unsupported. Rifle remains a prototype with no visible model. No animation
  parity or original full weapon mechanics claim.
- Trigger push/teleport/hurt imported as source model AABBs, not exact brush
  shapes; independent approximate logic, not full original rules. Actual trigger
  traversal has not been comprehensively tested. Pickups, other weapons, proper
  character art, sophisticated bots, menus/modes/networking remain incomplete.
- `tools/content/prepare_unity_textures.py <extracted-data>` decodes selected DDS
  to `ExternalContent/decoded`; put that root first in XONOTIC_CONTENT_ROOTS,
  followed by extracted maps/data and ThirdParty/Xonotic/maps-pk3.
- Only parent launches Unity. Worker tasks are now complete; no ongoing ownership.

## Historical checkpoint, 2026-09-20 — superseded where noted above

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
| Unity Editor import/compilation | PASS 2026-09-20: activated locally (Personal), project imported, 0 C# errors |
| Editor tests / Play Mode | Editor tests PASS (14) after generating fixtures; Play Mode smoke NOT RUN |
| Android build / installation / device play | APK BUILT 2026-09-20 (0.1.0-dev.1, 18.5 MB, ~5 min); device play NOT verified |

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

## Local build notes (2026-09-20)

- Never pin `Standard` in Always Included Shaders: it expands to 24,576 variants and
  the sandbox shader compiler (qemu-emulated) needs ~1 h for it. `LocalBuild.Configure`
  now unpins it; `Editor/ShaderVariantStripper.cs` keeps only minimal forward variants
  of built-in shaders; GraphicsSettings strips fog/lightmap/instancing variants.
- Run `python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated`
  before `tools/local_unity.py test`.
- Task order that works: compile → configure → scene → test → android.

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

## Historical implementation boundaries (dev.1, not current feature inventory)

The following describes the earlier dev.1 checkpoint. Texture/lightmap, static
IQM and approximate trigger support were subsequently added as documented in the
2026-09-21 checkpoint above. Do not use the older absence claims as current status.

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

## Historical next actions (dev.1)

1. DONE: local activation, compile, Editor checks, Android APK (see table).
2. Run the real Play Mode smoke (`playtest`, needs Xvfb + `-force-glcore`).
3. Run on a real ARM64 Android device: independent move/look/fire, safe area,
   pause/background/resume, shots/walls, score/respawn, performance and thermals.
4. Verify remaining source/attribution gaps in the upstream resource manifests
   before packaging; preserve all rights notices and corresponding source.
5. Connect actual texture/lightmap data to importer and implement one Boil gameplay
   feature at a time (trigger_push/teleport/hurt, pickups), with original-vs-port
   evidence. Do not broaden the prototype and relabel it Xonotic.
6. Record every outcome separately: source, import, play, packaging and device.
