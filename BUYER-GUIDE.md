# Plasma Verge — Unity Arena FPS Kit for Android — Buyer Guide

Thank you for buying Plasma Verge. This kit is a complete Unity 2022.3 (LTS) project
that turns the free, open-source **Xonotic 0.8.6** game data into a touch-controlled
arena shooter for Android: 29 maps, 14 weapons (9 core + 5 bonus, all 8 projectile
types using the original models), grappling hook,
NavMesh bots with 10 skill levels, Deathmatch / Team DM / CTF, settings page,
music, and a mobile-friendly rendering path (lightmaps, glow, quarter-res bloom,
adaptive resolution).

## 1. What is in the box

| Folder | What | Licence |
|---|---|---|
| `Assets/MyXonotic/Runtime` | Gameplay C#: player, Xonotic physics port, weapons, bots, HUD, menus, touch controls, settings | MIT (yours to use in any project) |
| `Assets/MyXonotic/Editor` | Import pipeline: Quake-3 BSP (IBSP 46) maps, MD3 / IQM / DPM / MDL models, shader-script parser, NavMesh bake, full-game build | MIT |
| `tools/` | `setup_content.py` (fetch game data), `make_sale_package.py`, `local_unity.py` (headless build runner), content helpers | MIT |
| `tests/` | 154 Python tests + Editor tests (1,074 checks) | MIT |
| `docs/` | Architecture, development guide, build guide, testing | MIT |
| **Game data** | Not included. Downloaded by `tools/setup_content.py` from dl.xonotic.org | GPL (Team Xonotic) |

The Unity project folder namespace is still `MyXonotic` (internal name); the shipped
product name, package id and menu title are **Plasma Verge**.

## 2. Requirements

- Unity **2022.3.62f3** (other 2022.3 LTS patches work) with **Android Build Support**
  (SDK, NDK, OpenJDK). ARM64 / IL2CPP.
- Python **3.10+** and `pip install Pillow` (DDS → PNG decoding).
- ~6 GB free disk: 1.24 GB download + extracted data + Unity import cache.
- Internet once, for the game data download.

## 3. Quick start (about 30 minutes + build time)

```bash
# 1. Fetch and prepare the game data (git-ignored ExternalContent/ folder)
python3 tools/setup_content.py
#    -> downloads xonotic-0.8.6.zip (SHA-512 verified), unpacks data/maps/music,
#       decodes textures, and prints the environment variables you need.

# 2. Export the variables it printed (or put them in your shell profile / Unity Hub env)
export XONOTIC_CONTENT_ROOTS=".../ExternalContent/decoded:.../worlddecoded:.../maps:.../data"
export XONOTIC_MAPS_ROOT=".../ExternalContent/maps"
export XONOTIC_MUSIC_ROOT=".../ExternalContent/music"
export XONOTIC_ALL_MAPS=1
```

3. Open the project folder in Unity Hub (launch Unity from a shell that has the
   variables above, or set them system-wide).
4. Menu **Plasma Verge ▸ 1 - Configure local project** (Android, ARM64, IL2CPP, shaders).
5. Menu **Plasma Verge ▸ 5 - Prepare ALL maps + menu (full game)** — imports the 29 maps,
   bakes NavMesh, imports characters/weapons/items, builds the menu catalog.
   First run: 20–40 minutes depending on CPU.
6. Menu **Plasma Verge ▸ 4 - Build local Android development APK** → `Builds/plasma-verge-full.apk`
   (≈ 400 MB with all maps). Install on a device and play.

Headless / CI alternative (Linux, no GPU needed):

```bash
export UNITY_EDITOR=/path/to/Unity
python3 tools/local_unity.py compile
python3 tools/local_unity.py weapons
python3 tools/local_unity.py prepare-maps
python3 tools/local_unity.py test           # 1,074 editor checks
XONOTIC_ALL_MAPS=1 python3 tools/local_unity.py android
```

Only a subset of maps? `XONOTIC_MAP_FILTER="afterslime,atelier,boil"` before step 5.

## 4. Where to change things

| Want to… | Look at |
|---|---|
| Weapon damage / speed / knockback | `Runtime/Gameplay/WeaponController.cs` → `WeaponDef` / `FireDef` (values mapped 1:1 from `bal-wep-xonotic.cfg`) |
| Movement feel | `Runtime/Gameplay/XonoticPhysics.cs` (constants from `physicsX.cfg`) |
| Bot difficulty | `Runtime/Gameplay/MatchSettings.cs`, `Bot.cs`, `BotAim.cs` |
| Touch layout / button size | `Runtime/Gameplay/TouchLayout.cs`, `TouchSettings.cs`, `TouchGlyphs.cs` |
| Menu / settings screens | `Runtime/Menu/MainMenu.cs`, `SettingsPage.cs`, `GameSettings.cs` |
| Graphics quality tiers | `Runtime/Gameplay/MobileBloom.cs`, `AdaptiveResolution.cs`, `Editor/LocalBuild.cs` |
| Game modes (DM / TDM / CTF) | `Runtime/Gameplay/MatchRules.cs`, `MatchSession.cs`, `CtfFlag.cs` |
| Import your own Q3-format maps | drop `.bsp` + textures in a content root; see `docs/ARCHITECTURE.md` |
| App name / id / icon | `Editor/LocalBuild.cs` → `ConfigureLocalProject()` |

## 5. Licences — read before publishing

- **Plasma Verge code (this kit): MIT.** Use it in commercial projects, no attribution required
  (a credit is appreciated).
- **Xonotic game data: GPL v3-or-later** (some textures GPL v2-or-later, a few CC-BY / MIT packs —
  see `THIRD_PARTY_NOTICES.md`). If you ship an APK containing Xonotic maps, models, sounds or
  music, that APK is a distribution of GPL content: keep the credits, provide the corresponding
  source on request, do not claim the art as your own, and do not use the name “Xonotic” as
  your product name (it belongs to Team Xonotic).
- **Google Play**: GPL assets are allowed on Google Play as long as you comply with the GPL.
  Many studios use this kit with their **own** maps and models instead — the importers accept
  any Quake-3 BSP / MD3 / IQM content.
- Plasma Verge is an independent project, not affiliated with or endorsed by Team Xonotic.

## 6. Known limitations (honest list)

- Offline only: bots, no network multiplayer.
- Doors/platforms from maps are static geometry; trigger→target chains are partial.
- Not every original item, announcer line and sound is ported yet.
- Rendering is a mobile approximation (lightmap × texture, glow, bloom), not DarkPlaces parity.
- DDS textures Pillow cannot decode fall back to the TGA/JPG copies in the packs; a handful
  of surfaces may look flat.
- Tested on Samsung A15 / S24 via Firebase Test Lab; older GPUs may need `Effects = Low`.

## 7. Support

Questions or bug reports: contact the seller through the marketplace where you bought the kit.
Please include the `SHARE LOG` report from the in-game PAUSE screen (it contains device,
FPS and error information) — it makes fixing things much faster.

## 8. AI disclosure

Large parts of this codebase and its documentation were written with the help of AI coding
assistants under the direction of the author, then compiled, tested (Editor + Python test
suites) and run on real devices through Firebase Test Lab.
