using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyXonotic.Content;
using MyXonotic.Gameplay;
using MyXonotic.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Host Editor Play Mode smoke run over the full-game package: loads the
    /// MainMenu scene, then every generated map scene in turn, drives the
    /// player through spawn/move/jump/fire with live bots for a few seconds,
    /// and records every exception/error per map without aborting the run.
    /// Result: Artifacts/all-maps-playtest.json. NOT an Android/device test.
    /// </summary>
    [InitializeOnLoad]
    public static class AllMapsPlaytest
    {
        const string Key = "my-xonotic.all-maps-playtest";
        const string Report = "Artifacts/all-maps-playtest.json";
        const float MapSeconds = 4f;

        static int stage, mapIndex, spawnIndex;
        static double started, mapStarted, deadline;
        static Vector3 origin;
        static float groundY;
        static List<string> scenes = new List<string>();
        static readonly List<MapResult> results = new List<MapResult>();
        static MapResult current;
        static BspSpawnPoint[] spawns;
        static float maxY;
        static bool jumpFromGround;
        static int pickupsAtStart;

        static AllMapsPlaytest()
        {
            if (SessionState.GetBool(Key, false)) Attach();
        }

        public static void Run()
        {
            Directory.CreateDirectory("Artifacts");
            File.Delete(Report);
            FullGameBuild.PrepareFullGame();
            SessionState.SetBool(Key, true);
            Attach();
            EditorApplication.isPlaying = true;
        }

        static void Attach()
        {
            stage = mapIndex = spawnIndex = 0;
            results.Clear();
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            ArenaBootstrap.TestMode = true;
            deadline = EditorApplication.timeSinceStartup + 60 + scenes.Count * 60;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
        }

        static void Log(string text, string trace, LogType type)
        {
            if (current == null) return;
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            {
                string line = type + ": " + text;
                if (!string.IsNullOrEmpty(trace))
                {
                    var first = trace.Split('\n').FirstOrDefault(l => l.Contains("MyXonotic"));
                    if (first != null) line += " @ " + first.Trim();
                }
                if (current.errors.Count < 20 && !current.errors.Contains(line)) current.errors.Add(line);
                current.errorCount++;
            }
            else if (type == LogType.Warning)
            {
                current.warningCount++;
                if (current.warnings.Count < 5 && !current.warnings.Contains(text)) current.warnings.Add(text);
            }
        }

        static void Check(bool ok, string message)
        {
            if (ok) current.checks.Add(message);
            else current.failures.Add(message);
        }

        static void Place(Player player, BspSpawnPoint spawn)
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = spawn.transform.position - Vector3.up * (24f / 32f);
            cc.enabled = true;
            player.ResetForRespawn(spawn.yaw);
            Physics.SyncTransforms();
        }

        static void BeginScene(string path)
        {
            current = new MapResult { scene = Path.GetFileNameWithoutExtension(path) };
            results.Add(current);
            mapStarted = EditorApplication.timeSinceStartup;
            spawns = null;
            stage = 100;
            SceneManager.LoadScene(current.scene, LoadSceneMode.Single);
        }

        static void EndScene()
        {
            current.seconds = (float)(EditorApplication.timeSinceStartup - mapStarted);
            current.passed = current.failures.Count == 0 && current.errorCount == 0;
            Debug.Log("ALL-MAPS " + current.scene + (current.passed ? " PASS" : " FAIL ") +
                      string.Join("; ", current.failures) + (current.errorCount > 0 ? " errors=" + current.errorCount : ""));
            current = null;
            if (++mapIndex < scenes.Count) BeginScene(scenes[mapIndex]);
            else Finish(null);
        }

        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) { Finish("global timeout"); return; }
                if (!EditorApplication.isPlaying) return;

                if (stage == 0)
                {
                    if (scenes.Count == 0) { Finish("no scenes in build settings"); return; }
                    BeginScene(scenes[0]);
                    return;
                }
                if (current == null) return;
                // Base budget plus ~1.2 s of physics settling per spawn point
                // (courtfun has 45 spawns and legitimately needs > 45 s).
                double sceneBudget = 45 + (spawns != null ? spawns.Length * 1.2 : 0);
                if (now - mapStarted > sceneBudget)
                {
                    current.failures.Add("scene timeout at stage " + stage);
                    EndScene();
                    return;
                }
                if (stage == 100)
                {
                    if (SceneManager.GetActiveScene().name != current.scene) return;
                    if (current.scene == SceneFlow.MainMenuScene) { stage = 200; started = now; return; }
                    stage = 101;
                    return;
                }

                // ---------------------------------------------------- main menu
                if (stage == 200)
                {
                    if (now - started < 0.5) return;
                    var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                    Check(menu != null, "MainMenu component present");
                    var catalog = MapCatalog.Load();
                    Check(catalog != null && catalog.maps.Count > 0, "MapCatalog loaded with entries");
                    var cards = UnityEngine.Object.FindObjectsOfType<Button>()
                        .Where(b => b.name.StartsWith("Card_")).ToArray();
                    Check(catalog != null && cards.Length == catalog.maps.Count,
                        "one card per catalog map (" + cards.Length + "/" + (catalog?.maps.Count ?? 0) + ")");
                    int missingPreview = catalog == null ? 0 : catalog.maps.Count(m => m.preview == null);
                    Check(missingPreview == 0, "all cards have preview images (missing " + missingPreview + ")");
                    int badScenes = catalog == null ? 0 : catalog.maps.Count(m =>
                        !scenes.Any(s => Path.GetFileNameWithoutExtension(s) == m.sceneName));
                    Check(badScenes == 0, "all catalog scenes are in build settings (bad " + badScenes + ")");
                    Check(UnityEngine.Object.FindObjectsOfType<Button>().Any(b => b.name == "Quit"), "QUIT button present");
                    EndScene();
                    return;
                }

                // ---------------------------------------------------- map scenes
                if (stage == 101)
                {
                    if (!ArenaBootstrap.IsReady) return;
                    var arena = ArenaBootstrap.Instance;
                    var player = arena.PlayerComponent;
                    player.UseTestInput = true;
                    Check(arena.UsedImportedArena && !arena.UsedFallbackSpawnMarker, "imported arena used, no fallback spawn");
                    spawns = UnityEngine.Object.FindObjectsOfType<BspSpawnPoint>();
                    current.spawnPoints = spawns.Length;
                    Check(spawns.Length > 0, "spawn points present");
                    current.pickups = pickupsAtStart = arena.Pickups.Count(p => p != null && p.gameObject.activeInHierarchy);
                    current.bots = arena.Bots.Count;
                    var music = UnityEngine.Object.FindObjectOfType<MapMusic>();
                    current.music = music != null && music.clip != null ? music.clip.name : "";
                    var imported = UnityEngine.Object.FindObjectOfType<ImportedArena>();
                    int textured = 0, materials = 0, missingShader = 0;
                    if (imported != null)
                        foreach (var r in imported.GetComponentsInChildren<MeshRenderer>())
                        foreach (var mat in r.sharedMaterials)
                        {
                            materials++;
                            if (mat == null || mat.shader == null || !mat.shader.isSupported) { missingShader++; continue; }
                            if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") != null) textured++;
                        }
                    current.materials = materials;
                    current.texturedMaterials = textured;
                    Check(missingShader == 0, "all world materials have supported shaders (bad " + missingShader + ")");
                    Check(textured > 0, "world textures referenced");
                    if (spawns.Length == 0) { EndScene(); return; }
                    spawnIndex = 0;
                    Place(player, spawns[0]);
                    started = now;
                    stage = 102;
                    return;
                }
                var arenaNow = ArenaBootstrap.Instance;
                if (arenaNow == null || arenaNow.PlayerComponent == null)
                {
                    current.failures.Add("arena vanished at stage " + stage);
                    EndScene();
                    return;
                }
                var p = arenaNow.PlayerComponent;
                if (stage == 102 && now - started > 1.0)
                {
                    if (!p.IsGrounded) current.floatingSpawns.Add(spawnIndex + "@" + spawns[spawnIndex].transform.position);
                    if (++spawnIndex < spawns.Length)
                    {
                        Place(p, spawns[spawnIndex]);
                        started = now;
                        return;
                    }
                    Check(current.floatingSpawns.Count == 0,
                        "all spawns settle on map collision (floating " + current.floatingSpawns.Count + "/" + spawns.Length + ")");
                    Place(p, spawns[0]);
                    origin = p.transform.position;
                    p.TestMove = Vector2.up;
                    started = now;
                    stage = 103;
                }
                else if (stage == 103 && now - started > 0.4)
                {
                    Check(Vector3.Distance(origin, p.transform.position) > 0.1f, "player moves on map geometry");
                    p.TestMove = Vector2.zero;
                    groundY = maxY = p.transform.position.y;
                    jumpFromGround = p.IsGrounded;
                    p.TestJump = true;
                    started = now;
                    stage = 104;
                }
                else if (stage == 104 && now - started <= 0.5)
                {
                    // Track the apex over the whole window instead of sampling
                    // one frame: the player may still be airborne after the
                    // 0.4 s walk (stairs/ledges), which made this check flaky.
                    maxY = Mathf.Max(maxY, p.transform.position.y);
                }
                else if (stage == 104 && now - started > 0.5)
                {
                    if (jumpFromGround) Check(maxY > groundY + 0.05f, "player jumps");
                    else current.checks.Add("player jump skipped (airborne after walk)");
                    p.TestJump = false;
                    arenaNow.PlayerWeapons.ResetCooldownForTest();
                    Check(arenaNow.PlayerWeapons.TryFire(p.ViewCamera.transform.position,
                        p.ViewCamera.transform.forward, false), "weapon fires");
                    p.TestFirePrimary = true;
                    p.TestMove = Vector2.up;
                    p.TestLook = new Vector2(40f, 0f);
                    started = now;
                    stage = 105;
                }
                else if (stage == 105 && now - started > MapSeconds)
                {
                    p.TestFirePrimary = false;
                    p.TestMove = Vector2.zero;
                    p.TestLook = Vector2.zero;
                    int aliveBots = arenaNow.Bots.Count(b => b != null && b.gameObject.activeInHierarchy);
                    current.botsAliveAfter = aliveBots;
                    Check(p.transform.position.y > -2000f, "player did not fall out of the world");
                    arenaNow.SetPaused(true);
                    origin = p.transform.position;
                    p.TestMove = Vector2.up;
                    started = now;
                    stage = 106;
                }
                else if (stage == 106 && now - started > 0.2)
                {
                    Check(Vector3.Distance(origin, p.transform.position) < 0.001f, "pause freezes player");
                    arenaNow.SetPaused(false);
                    p.TestMove = Vector2.zero;
                    Check(SceneFlow.HasMainMenu(), "MAIN MENU return available");
                    EndScene();
                }
            }
            catch (Exception ex)
            {
                if (current != null)
                {
                    current.failures.Add("driver exception: " + ex.Message);
                    EndScene();
                }
                else Finish(ex.Message);
            }
        }

        static void Finish(string failure)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Log;
            if (current != null) { current.failures.Add("run aborted: " + failure); EndSceneNoAdvance(); }
            int passed = results.Count(r => r.passed);
            var report = new Result
            {
                passed = failure == null && passed == results.Count && results.Count == scenes.Count,
                scenes = scenes.Count, scenesPassed = passed, error = failure ?? "",
                scope = "Host Unity Editor Play Mode (no graphics) smoke over MainMenu + every packaged map; NOT Android runtime, touch, rendering or full traversal validation",
                maps = results.ToArray()
            };
            File.WriteAllText(Report, JsonUtility.ToJson(report, true));
            Debug.Log("ALL-MAPS PLAYTEST " + (report.passed ? "PASS" : "FAIL") + " " + passed + "/" + results.Count);
            EditorApplication.Exit(report.passed ? 0 : 1);
        }

        static void EndSceneNoAdvance()
        {
            current.seconds = (float)(EditorApplication.timeSinceStartup - mapStarted);
            current.passed = false;
            current = null;
        }

        [Serializable]
        public class MapResult
        {
            public string scene;
            public bool passed;
            public float seconds;
            public int spawnPoints, pickups, bots, botsAliveAfter, materials, texturedMaterials;
            public int errorCount, warningCount;
            public string music;
            public List<string> checks = new List<string>();
            public List<string> failures = new List<string>();
            public List<string> floatingSpawns = new List<string>();
            public List<string> errors = new List<string>();
            public List<string> warnings = new List<string>();
        }

        [Serializable]
        class Result
        {
            public bool passed;
            public int scenes, scenesPassed;
            public string error, scope;
            public MapResult[] maps;
        }
    }
}
