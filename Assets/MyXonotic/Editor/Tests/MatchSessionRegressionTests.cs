using System;
using MyXonotic.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Dedicated Editor entry point AND home of the actual self-test bodies for
    /// <see cref="MatchSession"/> (Assets/MyXonotic/Runtime/Gameplay/MatchSession.cs)
    /// and its underlying <see cref="MatchRules{TParticipant}"/>
    /// (Assets/MyXonotic/Runtime/Gameplay/MatchRules.cs). The test code lives
    /// here — Editor-only, never part of a player build — rather than inside
    /// the Runtime MatchSession.cs, so the shipped Runtime assembly stays lean
    /// and free of GameObject/DestroyImmediate/GameState.Reset test scaffolding.
    ///
    /// Entirely separate menu entry/file from the shared <see cref="LocalTests"/>
    /// driver and from any future gameplay-integration aggregator (not modified
    /// here): run this suite on its own with
    ///   Unity -batchmode -nographics -projectPath . -quit \
    ///     -executeMethod MyXonotic.EditorTools.MatchSessionRegressionTests.Run
    /// or from the Editor menu ("My Xonotic/Tests - Match session (dedicated)").
    /// An integrator's aggregator (e.g. GameplayIntegrationTests.Run) can call
    /// <see cref="RunSelfTests"/> directly.
    ///
    /// This file was never executed against a real Unity Editor in the
    /// environment that wrote it (no Unity launch there, by instruction — see
    /// the accompanying report for exactly what remains to be run). The logic
    /// under test DID pass a host API/type compile against real local Unity
    /// assemblies (tools/host_compile.py); do not treat this comment as
    /// evidence the suite has passed inside Unity.
    /// </summary>
    public static class MatchSessionRegressionTests
    {
        [MenuItem("My Xonotic/Tests - Match session (dedicated)")]
        public static void Run()
        {
            RunSelfTests();
            Debug.Log("[MatchSessionRegressionTests] All MatchSession self-tests passed.");
        }

        /// Focused, dependency-free self tests exercising the real Actor/
        /// GameState scoring path (frag limit win, suicide with no double
        /// score, environmental/no-killer death, clean restart, invalid
        /// StartMatch transition, instant-unpause death, non-finite dt/config
        /// sanitation). Throws on the first failed assertion. Does not require
        /// Play Mode: Actor's TakeDamage/Die are plain method calls, not
        /// coroutine/Update driven.
        public static void RunSelfTests()
        {
            RunSuicideNoDoubleScoreTest();
            RunFragLimitWinTest();
            RunEnvironmentalDeathTest();
            RunCleanRestartTest();
            RunInvalidStartMatchTransitionTest();
            RunInstantDeathAfterUnpauseTest();
            RunNonFiniteDtAndConfigAreSanitizedTest();
        }

        static GameObject NewActorObject(string name)
        {
            var go = new GameObject(name);
            go.AddComponent<Actor>();
            return go;
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("[MatchSessionRegressionTests] FAIL: " + message);
        }

        static void RunSuicideNoDoubleScoreTest()
        {
            var aGo = NewActorObject("MatchSessionTest_A");
            var bGo = NewActorObject("MatchSessionTest_B");
            var session = new MatchSession();
            try
            {
                var a = aGo.GetComponent<Actor>();
                var b = bGo.GetComponent<Actor>();
                GameState.Register(a);
                GameState.Register(b);

                session.StartMatch(new MatchConfig { FragLimit = 3, TimeLimitSeconds = 0f },
                    new[] { a, b });

                // Suicide: killer == victim. Actor.Die() decrements A's own
                // frags (Frags-- ), never touches B. MatchSession must forward
                // exactly that reading, not invent an extra +1/-1 anywhere.
                a.TakeDamage(a.Health, Vector3.zero, a);

                Assert(a.Frags == -1, "suicide must leave the victim at -1 frags (Actor's own rule)");
                Assert(session.Rules.GetFrags(a) == -1, "MatchSession must report the exact suicide total, not double it");
                Assert(session.Rules.GetFrags(b) == 0, "an unrelated participant must be untouched by another actor's suicide");
                Assert(session.IsRunning, "a single suicide at frag limit 3 must not end the match");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(aGo);
                UnityEngine.Object.DestroyImmediate(bGo);
                GameState.Reset();
            }
        }

        static void RunFragLimitWinTest()
        {
            var killerGo = NewActorObject("MatchSessionTest_Killer");
            var victimGo = NewActorObject("MatchSessionTest_Victim");
            var session = new MatchSession();
            try
            {
                var killer = killerGo.GetComponent<Actor>();
                var victim = victimGo.GetComponent<Actor>();
                GameState.Register(killer);
                GameState.Register(victim);

                int overFired = 0;
                MatchResult<Actor> capturedResult = null;
                session.MatchOver += result => { overFired++; capturedResult = result; };
                session.StartMatch(new MatchConfig { FragLimit = 2, TimeLimitSeconds = 0f },
                    new[] { killer, victim });

                // Two ordinary kills by the same killer (victim respawns to
                // full health between hits in this harness by direct reset,
                // mirroring what Actor.Respawn() would do without needing a
                // live scene/Update loop).
                killer.TakeDamage(0, Vector3.zero, null); // no-op guard sanity: zero damage must not kill/frag
                Assert(!killer.IsDead, "zero damage must never kill");

                victim.TakeDamage(victim.Health, Vector3.zero, killer);
                Assert(killer.Frags == 1, "first kill must award exactly one frag to the killer");
                Assert(session.IsRunning, "one frag under a limit of 2 must not end the match");

                victim.ResetForSpawn(); // victim "respawns" for a second round
                victim.TakeDamage(victim.Health, Vector3.zero, killer);
                Assert(killer.Frags == 2, "second kill must award a second frag");
                Assert(session.IsOver, "reaching the frag limit must end the match");
                Assert(overFired == 1, "MatchOver must fire exactly once");
                Assert(capturedResult.Winner == killer, "the participant who reached the frag limit must be the winner");
                Assert(capturedResult.Reason == MatchEndReason.FragLimit, "end reason must be FragLimit");

                // No repeated completion: further deaths/time after Over must
                // not change Result or fire MatchOver again.
                victim.ResetForSpawn();
                victim.TakeDamage(victim.Health, Vector3.zero, killer);
                session.AdvanceTime(999f, false);
                Assert(overFired == 1, "MatchOver must not fire again after the match is Over");
                Assert(session.Result == capturedResult, "Result must stay the same terminal snapshot");
                Assert(session.ElapsedSeconds == 0f, "ElapsedSeconds must stay frozen once Over");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(killerGo);
                UnityEngine.Object.DestroyImmediate(victimGo);
                GameState.Reset();
            }
        }

        static void RunEnvironmentalDeathTest()
        {
            var go = NewActorObject("MatchSessionTest_Env");
            var session = new MatchSession();
            try
            {
                var actor = go.GetComponent<Actor>();
                GameState.Register(actor);

                session.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f },
                    new[] { actor });

                // Environmental death: instigator is null, exactly like a
                // fall/hazard death with no killer Actor.
                actor.TakeDamage(actor.Health, Vector3.zero, null);
                Assert(actor.Frags == -1, "a killer-less death must decrement the victim's own frags");
                Assert(session.Rules.GetFrags(actor) == -1, "MatchSession must forward the decrement, not clamp/ignore it");
                Assert(session.IsRunning, "a negative frag total must never satisfy a positive frag limit");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(go);
                GameState.Reset();
            }
        }

        static void RunCleanRestartTest()
        {
            var aGo = NewActorObject("MatchSessionTest_RestartA");
            var bGo = NewActorObject("MatchSessionTest_RestartB");
            var session = new MatchSession();
            try
            {
                var a = aGo.GetComponent<Actor>();
                var b = bGo.GetComponent<Actor>();
                GameState.Register(a);
                GameState.Register(b);

                session.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a, b });
                b.TakeDamage(b.Health, Vector3.zero, a);
                Assert(session.IsOver, "setup: first match must finish on the frag limit");

                a.ResetScore();
                b.ResetForSpawn();
                session.Restart(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a, b });

                Assert(session.IsRunning, "Restart must bring the session back to Running");
                Assert(session.Result == null, "Restart must clear the previous terminal Result");
                Assert(session.ElapsedSeconds == 0f, "Restart must reset elapsed time");
                Assert(session.Rules.GetFrags(a) == 0, "Restart must reset recorded frag totals for a clean restart");

                b.TakeDamage(b.Health, Vector3.zero, a);
                Assert(session.IsOver, "the restarted match must still score/finish correctly");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(aGo);
                UnityEngine.Object.DestroyImmediate(bGo);
                GameState.Reset();
            }
        }

        static void RunInvalidStartMatchTransitionTest()
        {
            var aGo = NewActorObject("MatchSessionTest_InvalidTransitionA");
            var session = new MatchSession();
            try
            {
                var a = aGo.GetComponent<Actor>();
                GameState.Register(a);

                session.StartMatch(new MatchConfig { FragLimit = 5, TimeLimitSeconds = 0f }, new[] { a });
                Assert(session.IsRunning, "setup: first StartMatch must succeed from NotStarted");

                bool threw = false;
                try
                {
                    // Calling StartMatch again while Running must never
                    // silently discard the live match; it must throw and
                    // require an explicit Reset()/Restart() instead.
                    session.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a });
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                Assert(threw, "StartMatch while Running must throw InvalidOperationException, not bypass the live match");
                Assert(session.Rules.Config.FragLimit == 5,
                    "a rejected StartMatch must not have mutated the live match's config");

                a.TakeDamage(a.Health, Vector3.zero, a); // suicide, frags -1, does not end this still-live match
                Assert(session.IsRunning, "the original match must still be the one running after the rejected call");

                // After an explicit Reset(), StartMatch must be accepted again.
                session.Rules.Reset();
                session.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f }, new[] { a });
                Assert(session.IsRunning, "StartMatch after an explicit Reset() must succeed");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(aGo);
                GameState.Reset();
            }
        }

        static void RunInstantDeathAfterUnpauseTest()
        {
            var killerGo = NewActorObject("MatchSessionTest_UnpauseKiller");
            var victimGo = NewActorObject("MatchSessionTest_UnpauseVictim");
            var session = new MatchSession();
            try
            {
                var killer = killerGo.GetComponent<Actor>();
                var victim = victimGo.GetComponent<Actor>();
                GameState.Register(killer);
                GameState.Register(victim);

                session.StartMatch(new MatchConfig { FragLimit = 1, TimeLimitSeconds = 0f },
                    new[] { killer, victim });

                // Pause, then unpause via the dedicated SetPaused hook only
                // (no AdvanceTime call in between) and immediately report a
                // death: this must be scored, not dropped, because SetPaused
                // syncs Rules.Phase back to Running synchronously.
                session.SetPaused(true);
                Assert(session.IsPaused, "SetPaused(true) must move the session to Paused");
                session.SetPaused(false);
                Assert(session.IsRunning, "SetPaused(false) must move the session back to Running immediately");

                victim.TakeDamage(victim.Health, Vector3.zero, killer);
                Assert(killer.Frags == 1, "Actor scoring itself must be unaffected by MatchSession's pause tracking");
                Assert(session.IsOver, "a death reported instantly after SetPaused(false) must be scored and end the match");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(killerGo);
                UnityEngine.Object.DestroyImmediate(victimGo);
                GameState.Reset();
            }
        }

        static void RunNonFiniteDtAndConfigAreSanitizedTest()
        {
            var aGo = NewActorObject("MatchSessionTest_NonFinite");
            var session = new MatchSession();
            try
            {
                var a = aGo.GetComponent<Actor>();
                GameState.Register(a);

                session.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = float.NaN },
                    new[] { a });
                Assert(session.Rules.Config.TimeLimitSeconds == 0f,
                    "a NaN time limit must sanitize to disabled (0), not corrupt the config");

                bool endedOnNaN = session.AdvanceTime(float.NaN);
                Assert(!endedOnNaN, "a NaN dt must never end the match");
                Assert(session.ElapsedSeconds == 0f, "a NaN dt must never move the match clock");

                bool endedOnInfinity = session.AdvanceTime(float.PositiveInfinity);
                Assert(!endedOnInfinity, "an infinite dt must never end the match");
                Assert(session.ElapsedSeconds == 0f, "an infinite dt must never move the match clock");

                bool endedOnHugeFinite = session.AdvanceTime(float.MaxValue);
                Assert(!endedOnHugeFinite, "a huge finite dt must not end an unbounded (no time limit) match");
                Assert(!float.IsNaN(session.ElapsedSeconds) && !float.IsInfinity(session.ElapsedSeconds),
                    "elapsed time must never become NaN/Infinity even from a huge finite dt");

                session.Rules.Reset();
                session.StartMatch(new MatchConfig { FragLimit = 0, TimeLimitSeconds = 10f }, new[] { a });
                session.AdvanceTime(15f);
                Assert(session.IsOver, "setup: a genuinely large-but-finite dt must still correctly reach a real time limit");
                Assert(session.ElapsedSeconds == 10f,
                    "elapsed time at completion must be clamped exactly to the configured time limit, never overshoot it");
            }
            finally
            {
                session.StopListening();
                UnityEngine.Object.DestroyImmediate(aGo);
                GameState.Reset();
            }
        }
    }
}
