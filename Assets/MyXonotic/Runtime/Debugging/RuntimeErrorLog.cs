using System;
using System.IO;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.12: persistent in-game error log. Every Debug.LogError / exception /
    /// warning is appended to <c>{persistentDataPath}/my-xonotic-errors.log</c>
    /// so a tester can send the file instead of describing "thousands of errors".
    /// The pause screen shows the counter and the path (<see cref="Summary"/>).
    /// Android: /storage/emulated/0/Android/data/com.ayoub.myxonotic/files/.
    /// </summary>
    public static class RuntimeErrorLog
    {
        public const string FileName = "my-xonotic-errors.log";
        public static int ErrorCount { get; private set; }
        public static int WarningCount { get; private set; }
        public static string LastError { get; private set; } = "";
        public static string Path { get; private set; } = "";
        static StreamWriter _writer;
        static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                Path = System.IO.Path.Combine(Application.persistentDataPath, FileName);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > 2 * 1024 * 1024) info.Delete(); // keep the file small
                _writer = new StreamWriter(Path, true) { AutoFlush = true };
                _writer.WriteLine("=== my-xonotic " + Application.version + " start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " | " + SystemInfo.deviceModel + " | " + SystemInfo.operatingSystem + " | GPU " + SystemInfo.graphicsDeviceName +
                    " (" + SystemInfo.graphicsDeviceType + ") | " + Screen.width + "x" + Screen.height);
            }
            catch (Exception e)
            {
                _writer = null;
                Debug.LogWarning("[RuntimeErrorLog] cannot open log file: " + e.Message);
            }
            Application.logMessageReceived += OnLog;
            Application.quitting += () => { try { _writer?.Dispose(); } catch { } _writer = null; };
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;
            if (type == LogType.Warning) WarningCount++;
            else
            {
                ErrorCount++;
                LastError = condition.Length > 160 ? condition.Substring(0, 160) + "…" : condition;
            }
            if (_writer == null) return;
            try
            {
                _writer.WriteLine("[" + Time.realtimeSinceStartup.ToString("0.00") + "s] " + type + ": " + condition);
                if (type != LogType.Warning && !string.IsNullOrEmpty(stackTrace))
                {
                    var lines = stackTrace.Split('\n');
                    for (int i = 0; i < Mathf.Min(6, lines.Length); i++) if (lines[i].Length > 0) _writer.WriteLine("    " + lines[i]);
                }
            }
            catch { /* never let logging throw */ }
        }

        /// <summary>One or two lines for the pause screen.</summary>
        public static string Summary()
        {
            string s = "errors " + ErrorCount + " / warnings " + WarningCount;
            if (ErrorCount > 0) s += "\nlast: " + LastError;
            if (!string.IsNullOrEmpty(Path)) s += "\nlog: " + Path;
            return s;
        }
    }
}
