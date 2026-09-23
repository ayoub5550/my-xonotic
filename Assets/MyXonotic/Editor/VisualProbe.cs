using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content;
using MyXonotic.Menu;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// dev.13 visual evidence without a GPU: Editor Play Mode driver that renders
    /// the real MainMenu screens (HOME / PLAY / SETTINGS) and one imported map
    /// with its HUD (playing + paused) into PNGs under <c>Artifacts/visual/</c>.
    /// Works under Xvfb + Mesa llvmpipe with <c>-force-glcore</c>
    /// (<c>python3 tools/local_unity.py visual-probe --graphics</c> inside
    /// <c>xvfb-run</c>, <c>LP_NUM_THREADS=16</c>). ScreenCapture cannot be used
    /// (no GameView in batchmode), so every ScreenSpaceOverlay canvas is switched
    /// to ScreenSpaceCamera for the shot and rendered by the scene camera into a
    /// RenderTexture. IMGUI (OnGUI touch discs, weapon bar, pause buttons) is not
    /// part of a camera render and therefore absent from these images — they
    /// show UGUI + world only. Scope: layout/readability sanity, not device parity.
    /// </summary>
    [InitializeOnLoad]
    public static class VisualProbe
    {
        const string ActiveKey = "my-xonotic.visualprobe.active";
        public const string OutDir = "Artifacts/visual";
        public const int Width = 1600, Height = 720; // 20:9 phone-like landscape

        static int stage, waitFrames;
        static double deadline;
        static string failure;
        static bool finishing;
        static readonly List<string> shots = new List<string>();
        static string mapScene;

        static VisualProbe()
        {
            if (SessionState.GetBool(ActiveKey, false)) Attach();
        }

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            File.Delete(OutDir + "/visual-probe.json");
            foreach (var old in Directory.GetFiles(OutDir, "*.png")) File.Delete(old);
            if (!SceneFlow.HasMainMenu() && AssetDatabase.LoadAssetAtPath<SceneAsset>(FullGameBuild.MenuScenePath) == null)
                throw new Exception("MainMenu scene missing: run prepare-maps first.");
            SessionState.SetBool(ActiveKey, true);
            Attach();
            EditorApplication.isPlaying = true;
        }

        static void Attach()
        {
            stage = 0;
            waitFrames = 0;
            failure = null;
            finishing = false;
            shots.Clear();
            deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
        }

        static void Log(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Assert) failure = condition;
        }

        static void Tick()
        {
            if (finishing) return;
            try
            {
                if (failure != null) throw new Exception(failure);
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("visual probe timed out at stage " + stage);
                if (!EditorApplication.isPlaying) return;
                if (waitFrames > 0) { waitFrames--; return; }

                switch (stage)
                {
                    case 0:
                        SceneManager.LoadScene(SceneFlow.MainMenuScene, LoadSceneMode.Single);
                        waitFrames = 20; stage = 1; break;
                    case 1:
                        Menu(MenuScreen.Home); Shoot("menu-home"); waitFrames = 5; stage = 2; break;
                    case 2:
                        Menu(MenuScreen.Play); Shoot("menu-play"); waitFrames = 5; stage = 3; break;
                    case 3:
                        Menu(MenuScreen.Settings); Shoot("menu-settings"); waitFrames = 5; stage = 30; break;
                    case 30: // dev.18: one shot per settings tab
                        ShowSettingsTab(SettingsTab.Audio); Shoot("menu-settings-audio"); waitFrames = 5; stage = 31; break;
                    case 31:
                        ShowSettingsTab(SettingsTab.Controls); Shoot("menu-settings-controls"); waitFrames = 5; stage = 32; break;
                    case 32:
                        ShowSettingsTab(SettingsTab.Game); Shoot("menu-settings-game"); waitFrames = 5; stage = 4; break;
                    case 4:
                        var catalog = MapCatalog.Load();
                        if (catalog == null || catalog.maps.Count == 0) throw new Exception("no MapCatalog");
                        mapScene = catalog.maps[0].sceneName;
                        // dev.18: XONOTIC_PROBE_MAP=<mapName> picks the map (default boil).
                        string wantMap = Environment.GetEnvironmentVariable("XONOTIC_PROBE_MAP");
                        if (string.IsNullOrEmpty(wantMap)) wantMap = "boil";
                        foreach (var e in catalog.maps) if (e.mapName == wantMap) mapScene = e.sceneName;
                        MatchSettings.OverrideForTest(GameMode.Deathmatch, true, 3);
                        SceneManager.LoadScene(mapScene, LoadSceneMode.Single);
                        waitFrames = 10; stage = 5; break;
                    case 5:
                        if (!ArenaBootstrap.IsReady) return;
                        waitFrames = 90; stage = 6; break; // let bots walk, sample once
                    case 6:
                        DevCapture.Instance?.TakeSample();
                        Shoot("hud-" + mapScene); stage = 7; waitFrames = 2; break;
                    case 7:
                        ArenaBootstrap.Instance.SetPaused(true);
                        waitFrames = 3; stage = 8; break;
                    case 8:
                        Shoot("pause-" + mapScene);
                        Finish(null); break;
                }
            }
            catch (Exception ex) { Finish(ex.Message); }
        }

        static void Menu(MenuScreen screen)
        {
            var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
            if (menu == null) throw new Exception("MainMenu not in scene");
            menu.ShowScreen(screen);
            Canvas.ForceUpdateCanvases();
        }

        static void ShowSettingsTab(SettingsTab tab)
        {
            var menu = UnityEngine.Object.FindObjectOfType<MainMenu>();
            if (menu == null || menu.Settings == null) throw new Exception("SettingsPage not built");
            menu.Settings.Show(tab);
            Canvas.ForceUpdateCanvases();
        }

        /// Render every canvas + the world through the scene's camera into a PNG.
        static void Shoot(string name)
        {
            var cam = Camera.main;
            if (cam == null) foreach (var c in Camera.allCameras) { cam = c; break; }
            if (cam == null) throw new Exception("no camera for " + name);
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            var restore = new List<(Canvas, RenderMode)>();
            int oldMask = cam.cullingMask;
            var oldTarget = cam.targetTexture;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            try
            {
                foreach (var cv in canvases)
                {
                    if (cv.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    restore.Add((cv, cv.renderMode));
                    cv.renderMode = RenderMode.ScreenSpaceCamera;
                    cv.worldCamera = cam;
                    cv.planeDistance = cam.nearClipPlane * 1.2f; // just past near clip so the view-model cannot occlude the HUD (Overlay semantics)
                }
                cam.cullingMask = ~0;
                cam.targetTexture = rt;
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string path = OutDir + "/" + name + ".png";
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                shots.Add(path);
                Debug.Log("[VisualProbe] wrote " + path);
            }
            finally
            {
                cam.targetTexture = oldTarget;
                cam.cullingMask = oldMask;
                foreach (var (cv, mode) in restore) { cv.renderMode = mode; cv.worldCamera = null; }
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        static void Finish(string error)
        {
            failure = error;
            finishing = true;
            SessionState.SetBool(ActiveKey, false);
            Application.logMessageReceived -= Log;
            EditorApplication.update -= Tick;
            var result = new Result { passed = error == null && shots.Count >= 5, shots = shots.ToArray(), error = error ?? "",
                renderer = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType,
                scope = "UGUI + world through the scene camera into a RenderTexture; IMGUI touch controls absent; llvmpipe, not a device" };
            File.WriteAllText(OutDir + "/visual-probe.json", JsonUtility.ToJson(result, true));
            Debug.Log("[my-xonotic] VISUAL PROBE " + (result.passed ? "PASS" : "FAIL") + " " + result.error);
            if (Application.isBatchMode) EditorApplication.Exit(result.passed ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }

        [Serializable]
        sealed class Result { public bool passed; public string[] shots; public string error, renderer, scope; }
    }
}
