using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>Actual frame/physics checks on imported Boil, not a device test.</summary>
    [InitializeOnLoad]
    public static class GameplayPlaytest
    {
        const string Key = "myxonotic.gameplay-playtest";
        static readonly List<string> Checks = new List<string>();
        static double started, deadline;
        static int stage, previousHealth;
        static Pickup selected;
        static string failure;
        static Vector3 position;
        static float elapsed;
        static GameplayPlaytest() { if (SessionState.GetBool(Key, false)) Attach(); }
        public static void Run()
        {
            Directory.CreateDirectory("Artifacts");
            LocalBuild.PrepareOriginalMap();
            SessionState.SetBool(Key, true);
            Attach();
            EditorApplication.isPlaying = true;
        }
        static void Attach()
        {
            stage = 0; selected = null; failure = null; Checks.Clear();
            deadline = EditorApplication.timeSinceStartup + 90;
            ArenaBootstrap.TestMode = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
        }
        static void Log(string text, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = text;
        }
        static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception(label);
            Checks.Add(label);
        }
        static void Place(Transform who, Vector3 where)
        {
            var cc = who.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            who.position = where;
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
        }
        static void Tick()
        {
            try
            {
                if (failure != null) throw new Exception(failure);
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("timeout at stage " + stage);
                if (!EditorApplication.isPlaying || !ArenaBootstrap.IsReady) return;
                ArenaBootstrap.TestMode = true;
                var arena = ArenaBootstrap.Instance;
                var player = arena.PlayerComponent;
                player.UseTestInput = true;
                if (stage == 0)
                {
                    Check(arena.UsedImportedArena && arena.Pickups.Count == 25, "original pickups registered by runtime");
                    Check(arena.Match != null && arena.Match.IsRunning, "offline match starts");
                    foreach (var pickup in arena.Pickups)
                        if (pickup.Type == PickupType.Health) { selected = pickup; break; }
                    Check(selected != null, "real source health item present");
                    // Move bots away from this test pickup so collection ownership is deterministic.
                    for (int i = 0; i < arena.Bots.Count; i++)
                        Place(arena.Bots[i].transform, new Vector3(1000 + i * 3, 1, 1000));
                    arena.PlayerActor.ResetForSpawn();
                    previousHealth = arena.PlayerActor.Health;
                    selected.RespawnTime = 0.3f;
                    Place(player.transform, selected.transform.position - Vector3.up * 0.9f);
                    started = now; stage = 1;
                }
                else if (stage == 1 && now - started > 0.15)
                {
                    Check(!selected.IsAvailable, "actual physics trigger consumes original-map pickup");
                    Check(arena.PlayerActor.Health == previousHealth + selected.Amount, "physical collection grants measured health");
                    arena.PlaceAtSpawn(player.transform);
                    arena.SetPaused(true);
                    elapsed = arena.Match.ElapsedSeconds;
                    int health = arena.PlayerActor.Health;
                    arena.PlayerActor.TakeDamage(500, Vector3.zero, null);
                    Check(arena.PlayerActor.Health == health, "pause rejects damage");
                    started = now; stage = 2;
                }
                else if (stage == 2 && now - started > 0.5)
                {
                    Check(!selected.IsAvailable, "pause freezes pickup respawn");
                    Check(arena.Match.ElapsedSeconds == elapsed, "pause freezes match clock");
                    arena.SetPaused(false); started = now; stage = 3;
                }
                else if (stage == 3 && now - started > 0.5)
                {
                    Check(selected.IsAvailable, "pickup respawns through real frames after resume");
                    arena.FragLimit = 1; arena.TimeLimitSeconds = 600;
                    arena.Restart();
                    Check(arena.Pickups.Count == 25, "restart retains imported pickup registry");
                    arena.Bots[0].Actor.TakeDamage(10000, Vector3.zero, arena.PlayerActor);
                    Check(arena.MatchFinished && ArenaBootstrap.IsPaused, "frag limit ends match and freezes gameplay");
                    Check(arena.Match.Result.Winner == arena.PlayerActor && arena.PlayerActor.Frags == 1, "winner recorded without double scoring");
                    arena.SetPaused(false);
                    Check(ArenaBootstrap.IsPaused, "finished match cannot resume without restart");
                    arena.PlayerWeapons.ResetCooldownForTest();
                    Check(!arena.PlayerWeapons.TryFire(player.transform.position, Vector3.forward, false), "no fire after match end");
                    int frags = arena.PlayerActor.Frags;
                    arena.Bots[1].Actor.TakeDamage(10000, Vector3.zero, arena.PlayerActor);
                    Check(arena.PlayerActor.Frags == frags, "terminal result blocks further scoring");
                    position = player.transform.position;
                    player.TestMove = Vector2.up;
                    started = now; stage = 4;
                }
                else if (stage == 4 && now - started > 0.2)
                {
                    Check(Vector3.Distance(position, player.transform.position) < 0.001f, "match end freezes physical movement");
                    player.TestMove = Vector2.zero;
                    arena.FragLimit = 0; arena.TimeLimitSeconds = 0.3f;
                    arena.Restart();
                    Check(!ArenaBootstrap.IsPaused && arena.PlayerActor.Frags == 0 && arena.Match.Result == null,
                        "restart clears result, score and freeze");
                    started = now; stage = 5;
                }
                else if (stage == 5 && now - started > 0.6)
                {
                    Check(arena.MatchFinished && arena.Match.Result.Reason == MatchEndReason.TimeLimit,
                        "real frame clock reaches time limit");
                    Check(arena.Match.Result.IsTie, "equal scores at time limit produce tie");
                    arena.FragLimit = 20; arena.TimeLimitSeconds = 600;
                    arena.Restart();
                    foreach (var p in arena.Pickups) Check(p.IsAvailable, "restart reactivates imported pickup");
                    Finish(null);
                }
            }
            catch (Exception e) { Finish(e.Message); }
        }
        static void Finish(string error)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            File.WriteAllText("Artifacts/gameplay-playtest.json", JsonUtility.ToJson(new Result
            {
                passed = error == null, checks = Checks.ToArray(), error = error ?? "",
                scope = "Host Editor physics on Boil; independent offline rules; NOT complete game/device/visual parity"
            }, true));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
        [Serializable] sealed class Result { public bool passed; public string[] checks; public string error, scope; }
    }
}
