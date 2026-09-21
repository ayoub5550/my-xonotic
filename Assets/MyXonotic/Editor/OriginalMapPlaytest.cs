using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>Actual host Editor simulation of imported-map spawning/collision.
    /// No claim about Android multitouch, visual parity or complete map traversal.</summary>
    [InitializeOnLoad]
    public static class OriginalMapPlaytest
    {
        const string Key = "my-xonotic.original-playtest";
        static int stage, spawnIndex;
        static double started, deadline;
        static Vector3 origin;
        static float groundY;
        static string error;
        static readonly List<string> checks = new List<string>();
        static BspSpawnPoint[] spawns;

        static OriginalMapPlaytest()
        {
            if (SessionState.GetBool(Key, false)) Attach();
        }
        public static void Run()
        {
            Directory.CreateDirectory("Artifacts");
            File.Delete("Artifacts/original-playtest.json");
            LocalBuild.PrepareOriginalMap();
            SessionState.SetBool(Key, true);
            Attach();
            EditorApplication.isPlaying = true;
        }
        static void Attach()
        {
            stage = spawnIndex = 0;
            error = null;
            checks.Clear();
            ArenaBootstrap.TestMode = true;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
        }
        static void Log(string text, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                error = text;
        }
        static void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            checks.Add(message);
        }
        static void Place(Player player, BspSpawnPoint spawn)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = spawn.transform.position - Vector3.up * (24f/32f);
            cc.enabled = true;
            player.ResetForRespawn(spawn.yaw);
            Physics.SyncTransforms();
        }
        static void Tick()
        {
            try
            {
                if (error != null) throw new Exception(error);
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("original map timeout stage " + stage);
                if (!EditorApplication.isPlaying || !ArenaBootstrap.IsReady) return;
                ArenaBootstrap.TestMode = true;
                var arena = ArenaBootstrap.Instance;
                var player = arena.PlayerComponent;
                player.UseTestInput = true;
                if (stage == 0)
                {
                    Check(arena.UsedImportedArena && !arena.UsedFallbackSpawnMarker, "original arena used, no fallback");
                    Check(GameObject.Find("PracticeArena") == null, "no primitive practice arena");
                    spawns = UnityEngine.Object.FindObjectsOfType<BspSpawnPoint>();
                    Check(spawns.Length == 7, "seven original Boil spawn points");
                    foreach (var bot in arena.Bots) bot.gameObject.SetActive(false);
                    var imported = UnityEngine.Object.FindObjectOfType<ImportedArena>();
                    int textured = 0;
                    foreach (var renderer in imported.GetComponentsInChildren<MeshRenderer>())
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        Check(mat != null && mat.shader != null && mat.shader.isSupported, "world material shader supported");
                        if (mat.mainTexture != null) textured++;
                    }
                    Check(textured > 0, "original world textures referenced");
                    Place(player, spawns[spawnIndex]);
                    started = now;
                    stage = 1;
                }
                else if (stage == 1 && now-started > 1.2)
                {
                    Debug.Log("SPAWN PROBE " + spawnIndex + " source=" + spawns[spawnIndex].transform.position
                        + " actual=" + player.transform.position + " velocity=" + player.Velocity + " grounded=" + player.IsGrounded);
                    Check(player.IsGrounded, "spawn " + spawnIndex + " settles on real map collision");
                    if (++spawnIndex < spawns.Length)
                    {
                        Place(player, spawns[spawnIndex]);
                        started = now;
                        return;
                    }
                    origin = player.transform.position;
                    player.TestMove = Vector2.up;
                    started = now;
                    stage = 2;
                }
                else if (stage == 2 && now-started > 0.3)
                {
                    Check(Vector3.Distance(origin, player.transform.position) > 0.1f, "movement on original geometry");
                    player.TestMove = Vector2.zero;
                    groundY = player.transform.position.y;
                    player.TestJump = true;
                    started = now;
                    stage = 3;
                }
                else if (stage == 3 && now-started > 0.15)
                {
                    Check(player.transform.position.y > groundY+0.1f, "jump on original geometry");
                    player.TestJump = false;
                    arena.PlayerWeapons.ResetCooldownForTest();
                    Check(arena.PlayerWeapons.TryFire(player.ViewCamera.transform.position,
                        player.ViewCamera.transform.forward, false), "weapon fires on imported scene");
                    arena.SetPaused(true);
                    origin = player.transform.position;
                    player.TestMove = Vector2.up;
                    started = now;
                    stage = 4;
                }
                else if (stage == 4 && now-started > 0.2)
                {
                    Check(Vector3.Distance(origin,player.transform.position)<0.001f, "pause freezes player");
                    arena.SetPaused(false);
                    player.TestMove = Vector2.zero;
                    if (Environment.GetEnvironmentVariable("XONOTIC_CAPTURE_HOST") == "1" &&
                        SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        var camera = player.ViewCamera;
                        var rt = new RenderTexture(1280,720,24);
                        camera.targetTexture = rt;
                        camera.Render();
                        var previous = RenderTexture.active;
                        RenderTexture.active = rt;
                        var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
                        image.ReadPixels(new Rect(0,0,1280,720),0,0);
                        image.Apply();
                        File.WriteAllBytes("Artifacts/unity-boil-host-camera.png",image.EncodeToPNG());
                        camera.targetTexture=null;
                        RenderTexture.active=previous;
                        UnityEngine.Object.DestroyImmediate(image);
                        UnityEngine.Object.DestroyImmediate(rt);
                    }
                    Finish(null);
                }
            }
            catch(Exception ex) { Finish(ex.Message); }
        }
        static void Finish(string failure)
        {
            SessionState.SetBool(Key,false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            File.WriteAllText("Artifacts/original-playtest.json",JsonUtility.ToJson(new Result {
                passed=failure==null, checks=checks.ToArray(), error=failure??"",
                scope="Host Unity Editor imported Boil smoke; NOT Android runtime/touch or complete traversal validation"
            },true));
            Debug.Log("ORIGINAL MAP PLAYTEST " + (failure==null ? "PASS" : "FAIL: "+failure));
            EditorApplication.Exit(failure==null ? 0 : 1);
        }
        [Serializable] class Result { public bool passed; public string[] checks; public string error,scope; }
    }
}
