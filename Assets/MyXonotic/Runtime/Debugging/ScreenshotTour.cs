using System;
using System.Collections;
using System.IO;
using MyXonotic.Content;
using MyXonotic.Gameplay;
using MyXonotic.Menu;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyXonotic.Debugging
{
    /// <summary>
    /// Desktop-only evidence capture: `my-xonotic.x86_64 -tour <dir>` walks
    /// MainMenu + every catalog map, saves PNG screenshots and a short frame
    /// sequence per map (for ffmpeg), writes <dir>/tour.json and quits.
    /// Never active without the flag; the Android player ignores it.
    /// </summary>
    public sealed class ScreenshotTour : MonoBehaviour
    {
        const int SettleFrames = 45;      // frames before the still shot
        const int ClipFrames = 90;        // frames captured for the clip
        const int ClipEvery = 3;          // capture every Nth frame -> 30 frames/clip

        static string outDir;
        static string mapFilter;
        static ScreenshotTour instance;

        int shots;
        readonly System.Text.StringBuilder log = new System.Text.StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isMobilePlatform || instance != null) return;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-tour") outDir = args[i + 1];
                if (args[i] == "-tourMaps") mapFilter = args[i + 1];
            }
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            var go = new GameObject("ScreenshotTour");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ScreenshotTour>();
        }

        void Start() => StartCoroutine(Run());

        IEnumerator Run()
        {
            Application.logMessageReceived += OnLog;
            var catalog = MapCatalog.Load();
            Note("start", SceneManager.GetActiveScene().name);

            if (SceneFlow.HasMainMenu())
            {
                SceneFlow.LoadMainMenu();
                yield return Settle();
                yield return Shoot("00_mainmenu");
            }

            if (catalog != null)
            {
                int index = 1;
                foreach (var entry in catalog.maps)
                {
                    if (!string.IsNullOrEmpty(mapFilter) &&
                        Array.IndexOf(mapFilter.Split(','), entry.mapName) < 0) continue;
                    string prefix = index.ToString("00") + "_" + entry.mapName;
                    index++;
                    SceneFlow.LoadMap(entry.sceneName);
                    yield return Settle();
                    var arena = ArenaBootstrap.Instance;
                    if (arena == null || arena.PlayerComponent == null)
                    {
                        Note("no-arena", entry.mapName);
                        continue;
                    }
                    yield return Shoot(prefix + "_spawn");

                    // Short walk + look for the clip; test input drives the
                    // same Player code the touch controls use.
                    var p = arena.PlayerComponent;
                    p.UseTestInput = true;
                    p.TestMove = Vector2.up;
                    p.TestLook = new Vector2(25f, 0f);
                    for (int f = 0; f < ClipFrames; f++)
                    {
                        if (f == ClipFrames / 2) p.TestFirePrimary = true;
                        if (f % ClipEvery == 0) yield return Shoot(prefix + "_clip_" + (f / ClipEvery).ToString("000"));
                        else yield return null;
                    }
                    p.TestFirePrimary = false;
                    p.TestMove = Vector2.zero;
                    p.TestLook = Vector2.zero;
                    yield return Shoot(prefix + "_after");
                }
            }

            Note("done", shots.ToString());
            File.WriteAllText(Path.Combine(outDir, "tour.json"),
                "{\"shots\":" + shots + ",\"log\":" + Quote(log.ToString()) + "}");
            Application.Quit();
        }

        IEnumerator Settle()
        {
            for (int i = 0; i < SettleFrames; i++) yield return null;
        }

        IEnumerator Shoot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            shots++;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                Note(type.ToString(), condition);
        }

        void Note(string kind, string text) => log.Append(kind).Append(": ").Append(text).Append('\n');

        static string Quote(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    }
}
