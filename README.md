# Plasma Verge — Unity Arena FPS Kit (Android)

A complete **Unity 2022.3 LTS** project that builds a fast, touch-controlled arena
shooter for Android from the free, open-source **Xonotic 0.8.6** game data.

- **29 arena maps** imported from Quake-3 BSP (IBSP 46) with lightmaps, curved patches,
  glow / scrolling / blended surfaces, skyboxes, and per-map music.
- **14 weapons** with Xonotic's real balance numbers and mechanics (charge, bursts,
  bounce, guided rockets, combo blasts, overheat), 8 projectile types with the
  original MD3 / IQM / MDL models, smoke trails and explosions, grappling hook.
- **Bots** on a baked NavMesh, skill 1–10, ported strategy / aim constants.
- **Modes**: Deathmatch, Team Deathmatch, Capture the Flag, frag / time limits.
- **Mobile-first**: multi-touch controls with resizable buttons and left-handed mode,
  tabbed settings (video / audio / controls / game), quarter-res bloom,
  adaptive resolution, ETC2 textures with mipmaps, IL2CPP ARM64.
- **Importers** for BSP maps, MD3 / IQM v1+v2 / DPM / MDL models, Quake shader scripts,
  skeletal animation (`.framegroups`) — bring your own Q3-format content too.
- **Tooling**: one-command content setup, headless Linux build runner, 154 Python
  tests, 1,074 Editor checks, Firebase Test Lab game-loop support.

Game data is **not** bundled: `python3 tools/setup_content.py` downloads the official
1.24 GB release, verifies its SHA-512 and prepares everything. See **[BUYER-GUIDE.md](BUYER-GUIDE.md)**.

## Quick start

```bash
pip install Pillow
python3 tools/setup_content.py        # data, maps, music, decoded textures
# export the XONOTIC_* variables it prints, then open the project in Unity 2022.3:
#   Plasma Verge ▸ 1 - Configure local project
#   Plasma Verge ▸ 5 - Prepare ALL maps + menu (full game)
#   Plasma Verge ▸ 4 - Build local Android development APK
```

## Documentation

- [BUYER-GUIDE.md](BUYER-GUIDE.md) — setup, customisation map, licences, limitations.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/DEVELOPMENT-GUIDE.md](docs/DEVELOPMENT-GUIDE.md),
  [docs/LOCAL_BUILD.md](docs/LOCAL_BUILD.md), [docs/TESTING.md](docs/TESTING.md).
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) — provenance and licences of upstream content.

## Licence

Original C# / Python code and documentation: **MIT** (`LICENSE`).
Xonotic game data: **GPL** © Team Xonotic and contributors — downloaded separately, never
relicensed by this project. Plasma Verge is independent and not endorsed by Team Xonotic.

Parts of this project were written with AI coding assistants under the author's direction and
verified by compilation, automated tests and real-device runs.
