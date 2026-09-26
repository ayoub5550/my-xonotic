# Development Sheet — How to Develop Plasma Verge (dev.N Methodology)

This sheet explains the **method** used to build dev.4 → dev.11 releases, so that any developer can follow the same pattern. Read it together with `AGENTS.md` (checkpoints), `docs/LOCAL_BUILD.md` (environment setup), and `docs/RELEASING.md` (release policy).

## 1. Principle

- **The original source is the reference.** Everything is derived from the original Xonotic 0.8.6 content (`ExternalContent/`): BSP maps, IQM/MD3/DPM models, sounds, materials, `.framegroups` and `.shader` files. Do not draw substitute assets or guess values; when a file is missing, safely drop the feature (fallback) and document that fact.
- **No GPU in the build environment.** All verification is geometric or logical (headless Editor and Play Mode tests). Do not claim visual verification; request a screenshot from a real device.
- **Every release = branch + document + receipt + APK.** No code without all four.

## 2. dev.N Release Cycle (Start to Finish)

1. **Read** the latest `docs/UNITY-DEV{N-1}.md` and the checkpoint section in `AGENTS.md`; identify “what is still missing”—that is the scope of dev.N.
2. **Branch** from the previous release branch:
   `feat/unity-dev{N}-<topic>` on top of `feat/unity-dev{N-1}-…` (a stack of branches; the PR targets the previous branch, not `main`).
3. **Analyze the original content first** with a small Python script (header bytes, joint/frame counts, shader names) before writing C#. For example, dev.11 discovered this way that some `h_*.iqm` files were IQM v2, one was IQM v1, and five were DPM files with an `.iqm` name.
4. **Write the Editor importer** (`Assets/MyXonotic/Editor/Import/`) that generates assets into `Assets/MyXonotic/Resources/` or `Generated/` (ignored in git). The importer must be deterministic (same path → same GUID), write a manifest/notes, and delete the old asset on failure so runtime fallback works.
5. **Write runtime logic** in `Assets/MyXonotic/Runtime/Gameplay/` without depending on the Editor; it reads assets through `Resources.Load` and tolerates their absence.
6. **Add a test for every claim**:
   - `LocalTests.cs` (Editor, deterministic, no gameplay scene) for asset geometry: counts, names, and reasonable bounding boxes.
   - `LocalPlaytest.cs` / `GameplayPlaytest` (Play Mode) for gameplay behavior.
   - State the expected test count in the document (for example, “Editor checks 237 → …”).
7. **Run the gates in order**, with each one stopping the sequence on failure:
   `compile → weapons/prepare-maps (if needed) → test → playtest → gameplay-playtest → android`.
   Runner: `python3 tools/local_unity.py <task>` (or the `pipeline.sh` wrapper with `TASKS=… DO_ANDROID=1 VC=<versionCode>`). Logs are stored in `Artifacts/<task>.log`; search for `error CS` and `TEST FAIL`.
   **First-person-view geometry gate** (since dev.12): the `WeaponPlacement` test writes `Artifacts/weapons/<Type>.obj`, then `python3 tools/weapon_snapshot.py Artifacts/weapons out.png` renders a wireframe using the same FOV/near/device aspect ratio (2400×1080). This reproduces what the device sees geometrically without a GPU—compare it with the device screenshot before “fixing” any weapon position.
8. **Document**: `docs/UNITY-DEV{N}.md` (in Arabic: objective, reasons/discoveries, what changed file by file, verification table, APK table, lessons), one line in `CHANGELOG.md`, a new checkpoint section **at the top** of `AGENTS.md`, and `VERSION`.
9. **Release**: target commit (source and documentation only), push, PR to the previous branch, GitHub prerelease `unity-v0.1.0-dev.{N}` with the APK and a JSON receipt (`docs/unity-dev{N}-build-<date>.json` written by the build). Increment versionCode by one for each release (dev.10 = 11, dev.11 = 12).
10. **Report facts only**: size, SHA256, which gates passed, and what was not verified (visual/device). Request screenshots.

## 3. Code Rules

- **Coordinates:** Quake (x right, y forward, z up, unit = 1/32 m) → Unity: position `(x, z, y)/32`, rotation `(x,y,z,w) → (−x,−z,−y,w)` (fixed to 0 mm in dev.9). View models `h_/v_` need an additional Euler(0,−90,0) rotation in `WeaponView` because they face toward +X.
- **Rig structures:** `CharacterRig` (ScriptableObject: joint names, parents, local bind, clips, flattened Poses `frame*joints*10`) is animated by `CharacterAnimator`. Use the same mechanism for characters (dev.9) and weapons (dev.11)—do not write a second animation system.
- **Every UI built in code must pass a geometry test** (positive rectangles, no `Mask` stencil over a transparent image)—a dev.10 lesson.
- **Do not commit** `ProjectSettings/`, `Assets/**/Generated/`, `Resources/Weapons/`, `Builds/`, `ExternalContent/`, any APK, or any credential data. Commit `.meta` files for new source files.
- **Naming:** one file per concept (`DpmDocument`, `WeaponRigImporter`, `WeaponRigInfo`); a header comment explaining the original file format and offset values.

## 4. Known Original Content Formats

| Format | Where | Reader | Notes |
|---|---|---|---|
| BSP (Q3, IBSP 46) | `maps/*.bsp` | `Content/Bsp/*` | lightmaps, patches, entities |
| MD3 | `v_*.md3`, items | `Md3WeaponModelBuilder` | static, materials from `.shader` |
| IQM v2 | characters, most `h_*` | `IqmSkinnedDocument` | clips from `.framegroups` |
| IQM v1 | `h_fireball.iqm` | same (`v1` flag) | 3-component quaternion, 9 channels, joint 44 B, pose 80 B |
| DPM (“DARKPLACESMODEL”, big-endian) | `h_electro/crylink/gl/hagar/rl` | `DpmDocument` | under the `.iqm` extension! Check the magic, not the extension |

## 5. Environment (Summary; Details in LOCAL_BUILD.md)

- Unity 2022.3.62f3 headless: `UNITY_EDITOR=<wrapper>`, variables `XONOTIC_CONTENT_ROOTS` and `XONOTIC_MAPS_ROOT`.
- Only one Unity instance at a time; when it stops, delete `Temp/UnityLockfile` and `Artifacts/local-unity.lock`, then restart. The first compilation after a major change may take 6–12 minutes (ILPP).
- A full APK build (29 maps, ~445 MB) takes 40–60 minutes on 17 cores.

## 6. Still Missing After dev.12 (dev.13+ Candidates)

Networking/multiplayer, bot waypoints, trigger→target chains, device performance testing, a HUD that more closely matches the original, production signing for the store, a touch settings page (button size/sensitivity), and verification that bots appear on the device (they did not appear in the dev.11 videos).

## 7. Material Rule (Since dev.12)

Every generated material must pass through `ImportedTexturePolicy.Finalize` (mipmaps + ETC2) before `PersistAsset`. A 2048² RGBA32 texture without mips = 16 MB of VRAM; 14 weapons × several textures plus pickup models consumed the phone’s GPU memory, causing a white Vortex and dark shapes in dev.11. Never write a raw texture directly.
