using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.13 "DevCapture": on-device diagnostics that survive without a cable.
    /// A single persistent object (created before the first scene) samples frame
    /// timing, memory, the arena state (bots alive / visible / nearest distance,
    /// player position, void height) every <see cref="SampleInterval"/> seconds and
    /// writes the sample as a NOTE line through <see cref="RuntimeErrorLog"/>.
    /// <see cref="BuildReport"/> assembles header + latest sample + repeated-error
    /// table + last log lines; <see cref="Share"/> hands that report (plus the
    /// file tail) to the Android share sheet so the owner can send it straight
    /// to Slack; <see cref="Copy"/> puts it on the clipboard. The PAUSE screen and
    /// the menu SETTINGS page expose both buttons. Nothing here needs a GPU.
    /// </summary>
    public sealed class DevCapture : MonoBehaviour
    {
        public const float SampleInterval = 5f;
        /// Bytes of the on-disk log appended to a shared report.
        public const int ShareTailBytes = 48 * 1024;

        public static DevCapture Instance { get; private set; }

        /// Smoothed frames per second over the last sample window.
        public float Fps { get; private set; }
        /// Worst single frame (ms) in the last sample window.
        public float WorstFrameMs { get; private set; }
        public int BotsTotal { get; private set; }
        public int BotsAlive { get; private set; }
        public int BotsVisible { get; private set; }
        public float NearestBotDistance { get; private set; } = -1f;
        public string LastSample { get; private set; } = "";
        public int SampleCount { get; private set; }

        float _accumTime;
        int _accumFrames;
        float _worst;
        float _sampleTimer;
        readonly List<Renderer> _rendererScratch = new List<Renderer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("DevCapture");
            if (Application.isPlaying) DontDestroyOnLoad(go); // edit-mode tests build it in the test scene
            Instance = go.AddComponent<DevCapture>();
            RuntimeErrorLog.Note("DevCapture armed (sample every " + SampleInterval + " s)");
        }

        /// Test/driver hook: make sure the singleton exists in the current scene.
        public static DevCapture Ensure()
        {
            if (Instance == null) Create();
            return Instance;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _accumTime += dt;
            _accumFrames++;
            if (dt > _worst) _worst = dt;
            _sampleTimer += dt;
            if (_sampleTimer >= SampleInterval) TakeSample();
        }

        /// Computes one sample now and appends it to the log. Called on the timer
        /// and by tests. Safe to call outside Play Mode (no arena -> "no arena").
        public string TakeSample()
        {
            Fps = _accumTime > 0f ? _accumFrames / _accumTime : 0f;
            WorstFrameMs = _worst * 1000f;
            _accumTime = 0f; _accumFrames = 0; _worst = 0f; _sampleTimer = 0f;
            SampleCount++;

            var sb = new StringBuilder();
            sb.Append("fps ").Append(Fps.ToString("0.0")).Append(" worst ").Append(WorstFrameMs.ToString("0")).Append(" ms");
            sb.Append(" | mem ").Append((UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)).ToString())
              .Append(" MB gfx ").Append((UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024)).ToString()).Append(" MB");

            var arena = ArenaBootstrap.Instance;
            if (arena == null || !ArenaBootstrap.IsReady)
            {
                sb.Append(" | no arena (menu)");
                BotsTotal = BotsAlive = BotsVisible = 0;
                NearestBotDistance = -1f;
            }
            else
            {
                Vector3 playerPos = arena.PlayerObject != null ? arena.PlayerObject.transform.position : Vector3.zero;
                int alive = 0, visible = 0;
                float nearest = float.MaxValue;
                Vector3 nearestPos = Vector3.zero;
                foreach (var bot in arena.Bots)
                {
                    if (bot == null) continue;
                    var actor = bot.Actor;
                    if (actor == null || actor.IsDead) continue;
                    alive++;
                    float d = Vector3.Distance(playerPos, bot.transform.position);
                    if (d < nearest) { nearest = d; nearestPos = bot.transform.position; }
                    _rendererScratch.Clear();
                    bot.GetComponentsInChildren(false, _rendererScratch);
                    foreach (var r in _rendererScratch) if (r.enabled && r.isVisible) { visible++; break; }
                }
                BotsTotal = arena.Bots.Count;
                BotsAlive = alive;
                BotsVisible = visible;
                NearestBotDistance = alive > 0 ? nearest : -1f;
                sb.Append(" | ").Append(MatchSettings.ModeShort(arena.Mode)).Append(" bots ").Append(alive).Append('/').Append(BotsTotal)
                  .Append(" visible ").Append(visible);
                if (alive > 0) sb.Append(" nearest ").Append(nearest.ToString("0.0")).Append(" m at ").Append(nearestPos.ToString("0"));
                sb.Append(" | player ").Append(playerPos.ToString("0"));
                if (arena.PlayerActor != null) sb.Append(" hp ").Append(arena.PlayerActor.Health).Append(" dead ").Append(arena.PlayerActor.IsDead);
                sb.Append(" | voidY ").Append(ArenaBootstrap.VoidKillY.ToString("0"));
                sb.Append(" spawns ").Append(arena.SpawnCount);
                if (arena.SnappedSpawns > 0 || arena.DroppedSpawns > 0)
                    sb.Append(" (snapped ").Append(arena.SnappedSpawns).Append(", dropped ").Append(arena.DroppedSpawns).Append(')');
                if (ArenaBootstrap.IsPaused) sb.Append(" | paused");
            }
            LastSample = sb.ToString();
            RuntimeErrorLog.Note(LastSample);
            return LastSample;
        }

        /// Short two-line status for HUD banners / the pause panel.
        public static string StatusLine()
        {
            var dc = Instance;
            if (dc == null) return "";
            string s = "FPS " + dc.Fps.ToString("0") + "  worst " + dc.WorstFrameMs.ToString("0") + " ms";
            if (dc.BotsTotal > 0)
                s += "  ·  BOTS " + dc.BotsAlive + "/" + dc.BotsTotal + " (visible " + dc.BotsVisible + (dc.NearestBotDistance >= 0f ? ", nearest " + dc.NearestBotDistance.ToString("0") + " m" : "") + ")";
            return s;
        }

        /// Full text report: header, counts, repeated-error table, last log lines.
        public static string BuildReport(bool includeFileTail)
        {
            var sb = new StringBuilder();
            sb.AppendLine(RuntimeErrorLog.Header());
            sb.AppendLine("uptime " + Time.realtimeSinceStartup.ToString("0") + " s | errors " + RuntimeErrorLog.ErrorCount +
                          " warnings " + RuntimeErrorLog.WarningCount + " | " + StatusLine());
            var dc = Instance;
            if (dc != null && !string.IsNullOrEmpty(dc.LastSample)) sb.AppendLine("last sample: " + dc.LastSample);
            if (RuntimeErrorLog.Repeats.Count > 0)
            {
                sb.AppendLine("--- repeated messages (count x message) ---");
                var rows = new List<KeyValuePair<string, int>>(RuntimeErrorLog.Repeats);
                rows.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < Mathf.Min(12, rows.Count); i++)
                    sb.AppendLine(rows[i].Value.ToString().PadLeft(6) + " x " + rows[i].Key);
            }
            sb.AppendLine("--- last " + RuntimeErrorLog.Recent.Count + " log lines ---");
            foreach (var line in RuntimeErrorLog.Recent) sb.AppendLine(line);
            if (includeFileTail)
            {
                string tail = RuntimeErrorLog.ReadTail(ShareTailBytes);
                if (tail.Length > 0) { sb.AppendLine("--- log file tail (" + RuntimeErrorLog.Path + ") ---"); sb.Append(tail); }
            }
            return sb.ToString();
        }

        /// Puts the report on the system clipboard (works on every platform).
        public static void Copy()
        {
            try { GUIUtility.systemCopyBuffer = BuildReport(false); RuntimeErrorLog.Note("report copied to clipboard"); }
            catch (Exception e) { Debug.LogWarning("[DevCapture] clipboard failed: " + e.Message); }
        }

        /// Android: opens the share sheet with the report as plain text (Slack,
        /// Telegram, mail...). Elsewhere: falls back to <see cref="Copy"/>.
        public static void Share()
        {
            string report = BuildReport(true);
            RuntimeErrorLog.Note("share requested (" + report.Length + " chars)");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                    intent.Call<AndroidJavaObject>("setType", "text/plain");
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"),
                        "my-xonotic " + Application.version + " device report");
                    intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), report);
                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Share my-xonotic report"))
                        activity.Call("startActivity", chooser);
                }
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DevCapture] share intent failed, copying instead: " + e.Message);
            }
#endif
            try { GUIUtility.systemCopyBuffer = report; } catch { }
        }
    }
}
