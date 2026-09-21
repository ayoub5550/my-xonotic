# MatchSession — offline Deathmatch state machine

Status: new, worker-owned files, **not wired into any scene / not integrated
with ArenaBootstrap/Hud by this change**. Compiles against real local Unity
2022.3.62f3 assemblies (`tools/host_compile.py`). `MatchRules<T>` has its own
standalone (non-Unity) test run with the bundled Mono/mcs — currently **68
checks passed**. `MatchSession`'s Actor/GameState integration self-tests were
written and are believed correct but were **not executed inside the Unity
Editor** by this change (no Unity launch, by instruction) — parent must run
them once, see "How to run" below.

This is **one Deathmatch mode only**: frag-limit and/or time-limit, no teams,
no weapons/pickups/network logic, not full Xonotic parity. It does not add,
change, or invent any mode, network behaviour, or paid/cloud dependency.

**2nd pass (review fixes applied):** non-finite (`NaN`/`Infinity`) `dt` and
`MatchConfig.TimeLimitSeconds` are now sanitized/rejected instead of being
able to corrupt the clock; `ElapsedSeconds` is clamped exactly to the time
limit on completion (never overshoots); `StartMatch` now throws
`InvalidOperationException` if called while `Running`/`Paused`/`Over` instead
of silently discarding a live/terminal match — callers must call `Reset()`
(`Restart` does this internally) first; pausing is now a dedicated
`SetPaused(bool)` hook decoupled from `AdvanceTime`, so a death reported the
instant after an unpause (before the next per-frame `AdvanceTime` call) is
still scored; and all self-test bodies were moved out of the shipped Runtime
`MatchSession.cs` into the Editor-only `MatchSessionRegressionTests.cs`
(with `GameState.AnyDeath` unsubscription now in every test's `finally`, so a
failed assertion can never leak a stale subscription into a later test).

## Files

- `Assets/MyXonotic/Runtime/Gameplay/MatchRules.cs` — pure, **UnityEngine-free**
  generic state machine `MatchRules<TParticipant>` plus `MatchConfig`,
  `MatchPhase`, `MatchEndReason`, `MatchResult<TParticipant>`. No dependency on
  Actor/GameState/Unity at all; `TParticipant` is any reference type the caller
  chooses to key participants by.
- `Assets/MyXonotic/Runtime/Gameplay/MatchSession.cs` — thin Unity-facing
  façade: `MatchRules<Actor>` wired to the **existing, unmodified**
  `Actor`/`GameState` scoring conventions. Not a MonoBehaviour: no scene/prefab
  requirement, parent can own the single instance however it likes. Kept lean
  and test-free (no `UnityEngine` GameObject test scaffolding shipped in the
  Runtime assembly / player build).
- `Assets/MyXonotic/Editor/Tests/MatchSessionRegressionTests.cs` — dedicated
  Editor entry point AND the actual self-test bodies (own menu item, mirrors
  the existing `SkyImportRegressionTests` pattern), exposing
  **`public static void RunSelfTests()`**. Not wired into `LocalTests.Run` or
  any future gameplay-integration aggregator; the parent's
  `GameplayIntegrationTests.cs` can call
  `MyXonotic.EditorTools.MatchSessionRegressionTests.RunSelfTests();` directly
  (the `gameplay-test` task slot already exists in `tools/local_unity.py`).
- `tests/csharp/MatchRulesTests.cs` — standalone (no Unity) test driver for
  `MatchRules<T>` only, using a tiny plain-C# `Fighter` stand-in participant
  type. Compiles/runs with the bundled Mono/mcs directly (see below). **68
  checks pass.**

No existing file was modified: `ArenaBootstrap.cs`, `Hud.cs`, `Actor.cs`,
`Player.cs`, `WeaponController.cs`, `GameState.cs` are all untouched by this
change (a pre-existing, already-uncommitted change to `Actor.cs`'s
`TakeDamage` pause guard was present in the worktree before this task started
and was left alone).

## Why the score is never doubled

`Actor.Die()` (unmodified) is the **only** place that mutates `Actor.Frags`:

```csharp
if (killer != null && killer != this) killer.Frags++;
else Frags--;   // suicide (killer == this) or environmental death (killer == null)
```

This happens *before* `Actor.Died` fires, and therefore before
`GameState.AnyDeath` fires. `MatchSession.ReportDeath(victim, killer)`
(auto-subscribed to `GameState.AnyDeath` by `StartMatch`) does no arithmetic:
it only **reads** the already-updated `victim.Frags` / `killer.Frags` and
forwards the current totals into `MatchRules<Actor>.ReportFrags`, which itself
also never adds — it just records the given total and checks the frag limit.
So a kill, a suicide, or an environmental death is scored by Actor exactly
once, and MatchSession/MatchRules only ever look at that single number.

## Public API (exact, current)

```csharp
namespace MyXonotic.Gameplay
{
    public enum MatchPhase { NotStarted, Running, Paused, Over }
    public enum MatchEndReason { None, FragLimit, TimeLimit }

    public struct MatchConfig
    {
        public int FragLimit;          // <= 0 disables the frag limit
        public float TimeLimitSeconds; // <= 0, NaN or +/-Infinity disables the time limit
        public static readonly MatchConfig Unbounded; // both disabled
    }

    public sealed class MatchResult<TParticipant> where TParticipant : class
    {
        public MatchEndReason Reason { get; }
        public TParticipant Winner { get; }     // null => tie
        public float ElapsedSeconds { get; }     // clamped to the time limit if that's how the match ended
        public bool IsTie { get; }               // Winner == null
    }

    public sealed class MatchRules<TParticipant> where TParticipant : class
    {
        public MatchPhase Phase { get; }
        public MatchConfig Config { get; }        // already-sanitized (never NaN/Infinity/negative)
        public float ElapsedSeconds { get; }
        public MatchResult<TParticipant> Result { get; } // null until Over
        public bool IsRunning { get; }
        public bool IsPaused { get; }
        public bool IsOver { get; }
        public event Action<MatchResult<TParticipant>> MatchOver; // fires exactly once per match

        // Throws InvalidOperationException if Phase != NotStarted (call Reset() first).
        public void StartMatch(MatchConfig config, IEnumerable<TParticipant> participants);
        public void SetPaused(bool paused);
        // dt <= 0, NaN or +/-Infinity is rejected (no-op, returns false); a
        // huge-but-finite dt cannot overflow the clock (rejected instead).
        // Returns true exactly when this call ended the match; on a TimeLimit
        // ending, ElapsedSeconds is clamped exactly to Config.TimeLimitSeconds.
        public bool AdvanceTime(float dt);
        public bool ReportFrags(TParticipant participant, int currentFragTotal); // true => this call ended the match
        public int GetFrags(TParticipant participant);            // 0 for unknown participants
        public void Reset();                                      // back to NotStarted, clears Result/frags
    }

    public sealed class MatchSession
    {
        public MatchRules<Actor> Rules { get; }
        public MatchPhase Phase { get; }
        public bool IsRunning { get; }
        public bool IsPaused { get; }
        public bool IsOver { get; }
        public float ElapsedSeconds { get; }
        public MatchResult<Actor> Result { get; }
        public event Action<MatchResult<Actor>> MatchOver;

        // Throws InvalidOperationException if Phase != NotStarted (call
        // Restart, or Rules.Reset() then this, first). Subscribes to
        // GameState.AnyDeath only after Rules.StartMatch succeeds.
        public void StartMatch(MatchConfig config, IEnumerable<Actor> participants);
        // Call the instant the parent's pause state changes, separately from
        // (and before) AdvanceTime, so an unpause takes effect immediately.
        public void SetPaused(bool paused);
        public bool AdvanceTime(float dt);                        // does not itself read/own pause state
        public bool AdvanceTime(float dt, bool isPaused);         // convenience: SetPaused(isPaused) then AdvanceTime(dt)
        public void ReportDeath(Actor victim, Actor killer);      // normally invoked automatically via GameState.AnyDeath
        public void Restart(MatchConfig config, IEnumerable<Actor> participants); // clean restart (StopListening+Reset+StartMatch)
        public void StopListening();                              // unsubscribe without restarting
    }
}

namespace MyXonotic.EditorTools // Editor-only, never shipped in a player build
{
    public static class MatchSessionRegressionTests
    {
        public static void RunSelfTests(); // throws on first failed assertion
        public static void Run();          // [MenuItem] wrapper: RunSelfTests() + a success log
    }
}
```

## Parent integration hooks (exactly 5, as requested)

1. **Starting a match** — `session.StartMatch(config, participants)`. Pass the
   live `Actor` instances that should be scored (e.g. `ArenaBootstrap.Instance.PlayerActor`
   plus every `Bot`'s `Actor`). Only valid while the session is `NotStarted`
   (a fresh instance, or right after `Rules.Reset()`); calling it again while
   `Running`/`Paused`/`Over` throws `InvalidOperationException` — use
   `Restart` (not `StartMatch` again) to begin a new match. This subscribes
   to `GameState.AnyDeath` only once `Rules.StartMatch` has accepted the
   transition, so a rejected call never leaves a dangling subscription.
2. **Advancing dt** — call `session.SetPaused(ArenaBootstrap.IsPaused)`
   **the instant** the pause state changes (this syncs `Rules.Phase`
   synchronously, so a death reported right after an unpause — before the
   next `AdvanceTime` — is still scored, not ignored), and call
   `session.AdvanceTime(Time.deltaTime)` once per frame. A combined
   `AdvanceTime(dt, isPaused)` convenience overload exists too, but calling
   `SetPaused` separately and immediately on every pause-state change is the
   correct pattern if a death could land between the pause flip and the next
   per-frame tick. `ArenaBootstrap.IsPaused` is only **read** here, never
   owned or written by this class.
3. **Scores after death** — automatic via the `GameState.AnyDeath` subscription
   set up by `StartMatch`; `ReportDeath` is public only for tests/manual
   feeding. No parent wiring needed for this hook by default.
4. **Match-over freeze / result view** — subscribe to `session.MatchOver`, or
   poll `session.IsOver` / `session.Result` each frame, to freeze
   input/HUD/camera and render `Result.Winner` (`null` => tie),
   `Result.Reason` (`FragLimit`/`TimeLimit`), and `Result.ElapsedSeconds`
   (clamped to the time limit when that's how the match ended).
   `MatchSession` does not freeze gameplay itself (no MonoBehaviour/Update, by
   design) — the parent decides how "freeze" actually manifests in
   ArenaBootstrap/Hud/Player/WeaponController, none of which this change touches.
5. **Clean restart** — `session.Restart(config, participants)`. Safe from any
   phase, including `Over` (the intended way to leave a terminal match) or
   mid-match. Internally: `StopListening()` → `Rules.Reset()` → `StartMatch(...)`
   (which is now guaranteed to be accepted, since `Reset()` returns to
   `NotStarted` first). Suggested wiring: call this from the same place
   `ArenaBootstrap.Restart()` is invoked (`R` key / touch), right after or
   before that call — again, without editing `ArenaBootstrap.cs` itself; e.g.
   a small owned method that calls both `ArenaBootstrap.Instance.Restart()`
   and `session.Restart(...)`.

`MatchSession` deliberately is **not** a `MonoBehaviour`. The parent can hold
the one instance as a field on whatever component already drives the frame
loop, or in a small new wrapper — this change does not prescribe or create
that wiring, per the "no changes to ArenaBootstrap/Hud/Actor/Player/WeaponController"
constraint.

## Limits and non-goals (explicit)

- One mode only: frag-limit and/or time-limit Deathmatch. No teams, no CTF/other
  modes, no bot-vs-bot AI changes, no network/multiplayer of any kind.
- `FragLimit <= 0` disables the frag limit; `TimeLimitSeconds <= 0`, `NaN`, or
  `+/-Infinity` all disable the time limit (sanitized in `MatchConfig`
  itself, never a crash or an instant match-over). If BOTH limits are
  disabled the match simply never ends on its own (verified by test).
- `AdvanceTime` rejects a non-finite (`NaN`/`Infinity`) or non-positive `dt`
  outright — the clock is never touched by a bad frame delta — and a
  huge-but-finite `dt` cannot push the clock itself into `NaN`/`Infinity`
  (also rejected as a step, defending against float overflow). On completion
  by time limit, `ElapsedSeconds`/`Result.ElapsedSeconds` are clamped exactly
  to `Config.TimeLimitSeconds`, never left overshooting it (e.g. a dt that
  jumps straight past the limit still reports the limit, not the raw sum).
- `StartMatch` only succeeds from `MatchPhase.NotStarted`; calling it while
  `Running`/`Paused`/`Over` throws `InvalidOperationException` instead of
  silently discarding the live/terminal match — an explicit `Reset()` (or
  `Restart`, which does this internally) is required first.
- Winner resolution: exact tie in recorded frags at the moment of ending
  (whichever cause) produces `Winner == null` (`IsTie == true`), never a
  fabricated tiebreaker.
- `ReportFrags`/`AdvanceTime` are no-ops once `Phase == Over`; `MatchOver`
  fires exactly once per match; `Result`/`ElapsedSeconds` are frozen after
  that (verified by test) — "no repeated completion" and "stable terminal
  state".
- Suicide/environmental death (`killer == null` or `killer == victim`) is
  read from Actor's own `Frags--` convention, never independently computed —
  a victim's frags can go negative and that is correctly forwarded, it just
  can never itself satisfy a positive frag limit.
- Unknown-at-start participants are tracked lazily the first time they report
  a frag total (useful if a bot/player is added mid-match by the parent); this
  is a convenience of the generic dictionary keyed by participant identity,
  not a claim of dynamic team/roster support.
- Pausing is a dedicated `SetPaused(bool)` call, decoupled from `AdvanceTime`,
  specifically so an unpause takes effect the instant it happens rather than
  being deferred until the next per-frame `AdvanceTime` call — a death
  reported in between is still scored.
- `MatchSession`/`MatchRules` do not persist anything, do not touch disk/network,
  and add no new third-party or paid/cloud dependency.

## How to run

Standalone `MatchRules<T>` tests, no Unity required (mirrors `tests/run_all.sh`'s
existing pattern for the BSP/MD3 parser tests):

```bash
MONO=/work/toolchains/unity/Editor/Editor/Data/MonoBleedingEdge/bin/mono
MCS=/work/temp/unity-mcs   # wraps: $MONO .../lib/mono/4.5/mcs.exe
BUILD=$(mktemp -d)
"$MCS" -target:exe -out:"$BUILD/MatchRulesTests.exe" \
  Assets/MyXonotic/Runtime/Gameplay/MatchRules.cs \
  tests/csharp/MatchRulesTests.cs
"$MONO" "$BUILD/MatchRulesTests.exe"
```

Result of the run performed for this change (2nd pass, after the review
fixes): **`MatchRulesTests: 68 checks passed.`**, exit code 0. `tests/run_all.sh`
(the existing BSP/MD3/Python suite, unrelated to but re-run to confirm no
regression) also still reports `All test suites passed.` (130 Python checks
+ existing BSP/MD3 checks; `MatchRulesTests.cs` is not part of that script and
must be run with the command above).

Host API/type compile (both Runtime and Editor assemblies, against real local
Unity 2022.3.62f3 managed DLLs — proves `MatchSession.cs`/`MatchRules.cs`/
`MatchSessionRegressionTests.cs` compile against the actual
`Actor`/`GameState`/`UnityEditor` types, still NOT an Editor import or Play
Mode run):

```bash
python3 tools/host_compile.py --editor-data "$UNITY_EDITOR_DATA" --ui-dll "$LOCAL_UNITY_UI_DLL"
```

Result of the run performed for this change:
`Host API compile passed. Unity Editor/import/runtime/build gates remain
separate.` (`MyXonotic.Runtime` — 33 files — and `MyXonotic.Editor` — 17
files, including the moved/expanded `MatchSessionRegressionTests.cs` — both
succeeded).

`MatchSessionRegressionTests.RunSelfTests()` — the real-`Actor` integration
coverage (frag limit win, suicide with no double score, environmental/
no-killer death, clean restart, invalid `StartMatch` transition,
instant-unpause death, non-finite `dt`/config sanitation) — requires an actual
Unity Editor process (creates real `GameObject`s with an `Actor` component)
and was **not run** by this change (no Unity launch, by instruction). Parent
can run it with:

```bash
Unity -batchmode -nographics -projectPath . -quit \
  -executeMethod MyXonotic.EditorTools.MatchSessionRegressionTests.Run
```

or from the Editor menu: **My Xonotic → Tests - Match session (dedicated)**.
To fold it into the existing `gameplay-test` task slot already present in
`tools/local_unity.py` (`MyXonotic.EditorTools.GameplayIntegrationTests.Run`),
the integrator's `GameplayIntegrationTests.Run` can simply call
`MyXonotic.EditorTools.MatchSessionRegressionTests.RunSelfTests();` alongside
the other workers' self-test entry points (this file does not create
`GameplayIntegrationTests.cs` itself, since `AGENTS.md` marks integration
tests/builds as parent-owned).

## What was NOT done (explicit)

- No scene/prefab wiring, no `ArenaBootstrap`/`Hud` changes, no HUD result
  screen, no input freeze implementation — those are explicitly parent
  integration work per the task.
- No execution inside a live Unity Editor/Play Mode (no Unity launch was
  performed by this change, by instruction). Only the standalone Mono/mcs test
  and the host API/type compile were actually run and are reported as such.
- No git commit/push was made; all new files are currently untracked/added-only
  in the working tree.
