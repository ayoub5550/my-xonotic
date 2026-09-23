using System.Text;
using MyXonotic.Content;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.16: Firebase Test Lab "Game Loop" support (docs/DEVICE-TESTING.md).
    /// Test Lab launches the app with action com.google.intent.action.TEST_LOOP;
    /// the menu then starts a match on its own, a Bot drives the player's body
    /// (ArenaBootstrap.AutoPilot) for <see cref="DurationSeconds"/>, a result
    /// summary is written to the intent's data Uri and the activity finishes.
    /// Nothing here runs in a normal launch (Active == false).
    /// The manifest intent-filter is injected by Editor/GameLoopManifest.cs.
    /// </summary>
    public sealed class GameLoop : MonoBehaviour
    {
        public const string Action = "com.google.intent.action.TEST_LOOP";
        /// Scenario 1 = 120 s deathmatch on the first catalog map; 2 = 180 s on the 2nd map, etc.
        public const int BaseDurationSeconds = 120;

        static bool _probed;
        static bool _active;
        static int _scenario = 1;

        public static bool Active { get { Probe(); return _active; } }
        public static int Scenario { get { Probe(); return _scenario; } }
        public static int DurationSeconds => BaseDurationSeconds + (Scenario - 1) * 60;

        /// Test hook (Editor): pretend the loop intent was received.
        public static void OverrideForTest(bool active, int scenario)
        {
            _probed = true;
            _active = active;
            _scenario = Mathf.Max(1, scenario);
        }

        static void Probe()
        {
            if (_probed) return;
            _probed = true;
            _active = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    string action = intent.Call<string>("getAction");
                    _active = action == Action;
                    if (_active) _scenario = Mathf.Max(1, intent.Call<int>("getIntExtra", "scenario", 1));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("GameLoop: intent probe failed: " + e.Message);
            }
#endif
            if (_active) RuntimeErrorLog.Note("GameLoop active, scenario " + _scenario + ", " + DurationSeconds + " s");
        }

        /// Called by MainMenu.Start: choose the map for this scenario and start the match.
        public static bool LaunchFromMenu(MapCatalog catalog)
        {
            if (!Active || catalog == null || catalog.maps == null || catalog.maps.Count == 0) return false;
            var entry = catalog.maps[(Scenario - 1) % catalog.maps.Count];
            MatchSettings.OverrideForTest(GameMode.Deathmatch, false, 3);
            RuntimeErrorLog.Note("GameLoop launching " + entry.sceneName);
            MyXonotic.Menu.SceneFlow.LoadMap(entry.sceneName);
            return true;
        }

        float _elapsed;
        int _frames;
        float _worstFrame;
        bool _finished;

        void Update()
        {
            if (_finished) return;
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            _frames++;
            if (_elapsed > 3f && dt > _worstFrame) _worstFrame = dt;
            if (_elapsed < DurationSeconds) return;
            _finished = true;
            string report = BuildReport(_elapsed, _frames, _worstFrame);
            RuntimeErrorLog.Note("GameLoop done: " + report.Replace('\n', ' '));
            WriteResult(report);
            Finish();
        }

        /// Pure (tested): one-line-per-field summary consumed from the Test Lab results.
        public static string BuildReport(float elapsed, int frames, float worstFrame)
        {
            var sb = new StringBuilder();
            sb.Append("scenario=").Append(Scenario).Append('\n');
            sb.Append("elapsed_s=").Append(elapsed.ToString("0.0")).Append('\n');
            sb.Append("avg_fps=").Append(elapsed > 0f ? (frames / elapsed).ToString("0.0") : "0").Append('\n');
            sb.Append("worst_frame_ms=").Append((worstFrame * 1000f).ToString("0")).Append('\n');
            sb.Append("errors=").Append(RuntimeErrorLog.ErrorCount).Append('\n');
            sb.Append("warnings=").Append(RuntimeErrorLog.WarningCount).Append('\n');
            var arena = ArenaBootstrap.Instance;
            if (arena != null)
            {
                sb.Append("player_frags=").Append(arena.PlayerActor != null ? arena.PlayerActor.Frags : 0).Append('\n');
                sb.Append("player_deaths=").Append(arena.PlayerActor != null ? arena.PlayerActor.Deaths : 0).Append('\n');
                int botFrags = 0;
                foreach (var b in arena.Bots) if (b != null && b.Actor != null) botFrags += b.Actor.Frags;
                sb.Append("bot_frags=").Append(botFrags).Append('\n');
                sb.Append("navmesh=").Append(MapNavMesh.Available ? 1 : 0).Append('\n');
            }
            sb.Append("last_error=").Append(RuntimeErrorLog.LastError.Replace('\n', ' ')).Append('\n');
            return sb.ToString();
        }

        static void WriteResult(string report)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                using (var uri = intent.Call<AndroidJavaObject>("getData"))
                {
                    if (uri == null) return;
                    using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                    using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(report);
                        stream.Call("write", bytes);
                        stream.Call("flush");
                        stream.Call("close");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("GameLoop: result write failed: " + e.Message);
            }
#endif
        }

        static void Finish()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    activity.Call("finish");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("GameLoop: finish failed: " + e.Message);
            }
#else
            Debug.Log("GameLoop: finished (editor, no activity to close)");
#endif
        }
    }
}
