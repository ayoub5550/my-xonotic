# Architecture and scope

## Direction

The target is Xonotic gameplay on Unity/Android. The first delivery establishes
local development, source for a small test environment and an evidence-based
content pipeline. It does not translate QuakeC automatically or embed DarkPlaces.

Unity 2022.3.62f3 is pinned to match the owner's existing LibreQuake project and
available toolchain. Unity's release API reports that this line is past its normal
support window; evaluate a supported LTS upgrade before a production store release.

## Layers

| Layer | Responsibility |
|---|---|
| `Runtime/Gameplay` | Independent offline development rules, movement, combat, bot actors, pickups and touch HUD |
| `Runtime/Content/Bsp` | Pure C# bounded IBSP v46 parser and geometry conversion |
| `Runtime/Content` | Scene markers describing imported geometry/spawn points |
| `Editor/Import` | Convert selected external map geometry into generated Unity assets; expose unsupported features |
| `Editor/LocalBuild` | Deterministic local settings, development scene creation, Android/Linux build reports |
| `tools/content` | Safe local archive inspection/extraction and provenance |
| `tools/local_unity.py` | Local Editor invocation, timeout, per-project lock, ignored logs |
| `tools/host_compile.py` | Direct C# API/type validation when Editor activation is unavailable |

No online account system, analytics, XonStat integration, matchmaking or network
protocol is implemented. "Local build" means compiling the program on a local
machine; it does not by itself prohibit future multiplayer.

## Coordinate convention

For this project, source `(x,y,z)` becomes Unity `(x,z,y) / 32`. This is a **chosen
scale convention**, reused for consistency with the owner's existing project,
not a claim that the original game defines one unit as a physical metre or inch.
Axis swapping changes handedness, so triangle winding must be adjusted. Spawn
coordinates are actor origins, not necessarily feet; the importer and spawning
code agree on a `-24/32` vertical origin-to-feet offset in `ContentBridge`.

## Why not copy LibreQuake?

Xonotic has different game rules, player movement, multiplayer semantics,
weapons, model formats, map entities and Q3-family compiled BSP geometry.
The older project is useful for local toolchain lessons, not evidence that a
substitution of names/assets would produce Xonotic.

## Compatibility ladder

1. **Parse:** a source file is structurally valid and data can be enumerated.
2. **Render:** meshes/materials are visible in Unity.
3. **Collide:** the actor can traverse them without falling through.
4. **Interact:** pickups, jump pads, teleporters, movers and hazards match rules.
5. **Play:** complete mode/scoring/respawn/AI behavior is verified.
6. **Network:** authoritative multiplayer, prediction and protocol decisions verified.
7. **Ship:** licensing, device performance, input, sound and store compliance verified.

Passing an earlier rung must never be described as passing the later ones.

## Known large gaps

- Real Xonotic textures, material shader scripts, lightmaps, skies, animated models
  (including IQM/MD3), weapon art and sound are not in the development fixture.
- Full Xonotic movement parity, weapon roster/secondary behavior and balance need
  evidence-driven implementation rather than extrapolation from the prototype.
- BSP brush collision volumes, inline movers and entity behavior need their own
  implementation/verification; triangle collision is an approximation.
- Bots are test opponents, not the original navigation/AI system.
- No CTF, team modes, campaign/challenges or original multiplayer compatibility.
- Device rendering/input/performance cannot be inferred from host C# tests.
