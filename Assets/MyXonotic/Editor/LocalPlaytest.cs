using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Real Editor Play Mode smoke driver. Start without -quit; exit/result is emitted
    /// only after physical movement, jump, combat, damage, respawn and reset assertions.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalPlaytest
    {
        const string ActiveKey = "my-xonotic.playtest.active";
        static readonly List<string> Passed = new List<string>();
        static int stage;
        static double deadline, started;
        static Vector3 startPosition;
        static int targetHealth;
        static int spawnFrame;
        static string failure;
        static bool finishing;

        static LocalPlaytest()
        {
            if (SessionState.GetBool(ActiveKey, false)) Attach();
        }

        public static void Run()
        {
            Directory.CreateDirectory("Artifacts");
            if (File.Exists("Artifacts/playtest-result.json")) File.Delete("Artifacts/playtest-result.json");
            LocalBuild.CreateDevelopmentScene();
            SessionState.SetBool(ActiveKey, true);
            Attach();
            EditorApplication.isPlaying = true;
        }

        static void Attach()
        {
            ArenaBootstrap.TestMode = true;
            stage = 0;
            Passed.Clear();
            failure = null;
            finishing = false;
            started = EditorApplication.timeSinceStartup;
            deadline = started + 120;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.playModeStateChanged += StateChanged;
        }

        static void Log(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                failure = condition;
        }

        static void StateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode) ArenaBootstrap.TestMode = true;
        }

        static void Tick()
        {
            if (finishing) return;
            try
            {
                if (failure != null) throw new Exception(failure);
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Playtest timed out at stage " + stage);
                if (!EditorApplication.isPlaying) return;
                ArenaBootstrap.TestMode = true;
                if (!ArenaBootstrap.IsReady) return;
                var arena = ArenaBootstrap.Instance;
                var player = arena.PlayerComponent;
                var target = arena.Bots[0].Actor;
                player.UseTestInput = true;
                if (stage == 0)
                {
                    Check(arena.Bots.Count == 3 && arena.Pickups.Count == 9, "arena population");
                    for (int i = 0; i < arena.Bots.Count; i++)
                        Place(arena.Bots[i].transform, new Vector3(-15 + i * 2, 0.1f, 15));
                    Place(player.transform, new Vector3(-10, 0.1f, 0));
                    player.ResetForRespawn(0);
                    startPosition = player.transform.position;
                    player.TestMove = Vector2.up;
                    started = now;
                    stage = 1;
                }
                else if (stage == 1 && now - started > 0.7)
                {
                    Check(Vector3.Distance(startPosition, player.transform.position) > 1f, "physical walk");
                    Check(player.transform.position.y > -0.1f, "floor collision");
                    player.TestMove = Vector2.zero;
                    player.TestJump = true;
                    startPosition = player.transform.position;
                    started = now;
                    stage = 2;
                }
                else if (stage == 2 && now - started > 0.15)
                {
                    Check(player.transform.position.y > startPosition.y + 0.2f, "physical jump");
                    player.TestJump = false;
                    Place(player.transform, new Vector3(-10, 0.1f, 0));
                    player.ResetForRespawn(0);
                    Place(target.transform, new Vector3(-10, 0.1f, 5));
                    Physics.SyncTransforms();
                    arena.PlayerWeapons.GiveWeapon(WeaponType.MachineGun);
                    arena.PlayerWeapons.SwitchTo(WeaponType.MachineGun);
                    arena.PlayerWeapons.ResetCooldownForTest();
                    targetHealth = target.Health;
                    Check(arena.PlayerWeapons.TryFire(player.transform.position + Vector3.up,
                        Vector3.forward, false), "hitscan fired");
                    Check(target.Health < targetHealth, "hitscan damages target");
                    // dev.11: firing plays the h_ rig's fire clip and spawns a muzzle flash.
                    var view = arena.PlayerWeapons.View;
                    if (view != null && view.IsRigged(WeaponType.MachineGun))
                    {
                        Check(view.AnimatorFor(WeaponType.MachineGun).CurrentClip == "fire", "fire animation plays on shot");
                        Check(view.ShotJointFor(WeaponType.MachineGun) != null, "muzzle joint resolved");
                        Check(ImpactEffects.LiveCount > 0, "muzzle flash spawned");
                    }
                    else Debug.LogWarning("[my-xonotic] MachineGun not rigged in playtest; animation checks skipped");
                    arena.PlayerWeapons.ResetCooldownForTest();
                    arena.PlayerWeapons.SwitchTo(WeaponType.Blaster);
                    // Re-seat the target: MachineGun knockback moves it during this frame, which made
                    // this check flaky. Aim at the target centre so the shot is deterministic.
                    Place(target.transform, new Vector3(-10, 0.1f, 5));
                    Physics.SyncTransforms();
                    targetHealth = target.Health;
                    Vector3 muzzle = player.transform.position + Vector3.up;
                    Vector3 aim = (target.transform.position + Vector3.up - muzzle).normalized;
                    Check(arena.PlayerWeapons.TryFire(muzzle, aim, false), "projectile fired");
                    spawnFrame = Time.frameCount;
                    started = now;
                    stage = 3;
                }
                else if (stage == 3 && ((Projectile.LiveCount == 0 && Time.frameCount > spawnFrame) || now - started > 3.0))
                {
                    // Frame-driven: headless Play Mode frames can be slower than editor wall-clock,
                    // so wait for the projectile to resolve (or a generous timeout) instead of 0.6 s.
                    Check(target.Health < targetHealth, "projectile damages target (target hp " + target.Health + "/" + targetHealth +
                        " pos " + target.transform.position + " player " + player.transform.position +
                        " live projectiles " + Projectile.LiveCount + " frames " + (Time.frameCount - spawnFrame) +
                        " dt " + Time.deltaTime + " paused " + ArenaBootstrap.IsPaused + " timeScale " + Time.timeScale + ")");
                    target.TakeDamage(1000, Vector3.zero, arena.PlayerActor);
                    Check(target.IsDead && arena.PlayerActor.Frags == 1, "kill scoring");
                    started = now;
                    stage = 4;
                }
                else if (stage == 4 && now - started > 3)
                {
                    Check(!target.IsDead && target.Health == Actor.StartHealth, "timed respawn");
                    arena.PlayerActor.AddArmor(50);
                    arena.PlayerActor.TakeDamage(20, Vector3.zero, null);
                    Check(arena.PlayerActor.Health == 92 && arena.PlayerActor.Armor == 38, "runtime armor");
                    arena.Restart();
                    Check(arena.PlayerActor.Frags == 0 && arena.PlayerActor.Deaths == 0 &&
                        arena.PlayerActor.Health == 100, "match reset");
                    arena.SetPaused(true);
                    startPosition = player.transform.position;
                    player.TestMove = Vector2.up;
                    started = now;
                    stage = 5;
                }
                else if (stage == 5 && now - started > 0.2)
                {
                    Check(Vector3.Distance(startPosition, player.transform.position) < 0.001f, "pause stops player");
                    arena.SetPaused(false);
                    player.TestMove = Vector2.zero;
                    if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                        ScreenCapture.CaptureScreenshot("Artifacts/development-arena.png");
                    Finish(null);
                }
            }
            catch (Exception ex) { Finish(ex.Message); }
        }

        static void Place(Transform actor, Vector3 position)
        {
            var cc = actor.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            actor.position = position;
            if (cc != null) cc.enabled = true;
        }

        static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAIL: " + label);
            Passed.Add(label);
        }

        static void Finish(string error)
        {
            failure = error;
            finishing = true;
            SessionState.SetBool(ActiveKey, false);
            Application.logMessageReceived -= Log;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= StateChanged;
            var result = new Result { passed = error == null, checks = Passed.ToArray(), error = error ?? "",
                scope = "Editor Play Mode smoke only; not full game, Android device, touch or visual parity validation" };
            File.WriteAllText("Artifacts/playtest-result.json", JsonUtility.ToJson(result, true));
            Debug.Log("[my-xonotic] PLAYTEST " + (result.passed ? "PASS" : "FAIL") + " " + result.error);
            // Do not rely on static state surviving the exit-Play-Mode domain reload.
            if (Application.isBatchMode) EditorApplication.Exit(result.passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        [Serializable]
        sealed class Result { public bool passed; public string[] checks; public string error, scope; }
    }
}
