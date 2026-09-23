using System;
using System.Collections.Generic;
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
    /// dev.13: also keeps the last <see cref="RecentCapacity"/> lines in memory
    /// (<see cref="Recent"/>) for the DevCapture overlay, accepts NOTE lines
    /// (<see cref="Note"/>: status samples that are not errors) and can read the
    /// file tail back for sharing (<see cref="ReadTail"/>).
    /// </summary>
    public static class RuntimeErrorLog
    {
        public const string FileName = "my-xonotic-errors.log";
        public const int RecentCapacity = 60;
        public static int ErrorCount { get; private set; }
        public static int WarningCount { get; private set; }
        public static string LastError { get; private set; } = "";
        public static string Path { get; private set; } = "";
        static StreamWriter _writer;
        static bool _hooked;
        static readonly List<string> _recent = new List<string>();
        static readonly Dictionary<string, int> _repeats = new Dictionary<string, int>();

        /// Last lines written (errors, warnings and notes), oldest first.
        public static IReadOnlyList<string> Recent => _recent;

        /// Distinct error/warning messages with their repeat counts (for "the same
        /// exception 4,000 times" reports). Key = first 120 chars of the message.
        public static IReadOnlyDictionary<string, int> Repeats => _repeats;

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
                _writer.WriteLine(Header());
            }
            catch (Exception e)
            {
                _writer = null;
                Debug.LogWarning("[RuntimeErrorLog] cannot open log file: " + e.Message);
            }
            Application.logMessageReceived += OnLog;
            Application.lowMemory += () => Note("LOW MEMORY warning from the OS");
            Application.quitting += () => { try { _writer?.Dispose(); } catch { } _writer = null; };
        }

        /// One line describing this run: version, device, OS, GPU, resolution.
        public static string Header()
        {
            return "=== my-xonotic " + Application.version + " start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                   " | " + SystemInfo.deviceModel + " | " + SystemInfo.operatingSystem + " | GPU " + SystemInfo.graphicsDeviceName +
                   " (" + SystemInfo.graphicsDeviceType + ", " + SystemInfo.graphicsMemorySize + " MB) | RAM " + SystemInfo.systemMemorySize +
                   " MB | " + Screen.width + "x" + Screen.height + " | ETC2 " + SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGBA8) +
                   " | compute skinning " + SystemInfo.supportsComputeShaders;
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
            string key = condition.Length > 120 ? condition.Substring(0, 120) : condition;
            int n;
            _repeats.TryGetValue(key, out n);
            if (n > 0 || _repeats.Count < 64) _repeats[key] = n + 1;
            Push("[" + Time.realtimeSinceStartup.ToString("0.00") + "s] " + type + ": " + condition);
            if (_writer == null) return;
            try
            {
                // A message repeating more than 20 times is only counted from then
                // on (the first 20 copies keep their stack traces in the file).
                if (n >= 20) return;
                _writer.WriteLine(_recent[_recent.Count - 1]);
                if (type != LogType.Warning && !string.IsNullOrEmpty(stackTrace))
                {
                    var lines = stackTrace.Split('\n');
                    for (int i = 0; i < Mathf.Min(6, lines.Length); i++) if (lines[i].Length > 0) _writer.WriteLine("    " + lines[i]);
                }
                if (n == 19) _writer.WriteLine("    (further repeats of this message are counted only; see DevCapture report)");
            }
            catch { /* never let logging throw */ }
        }

        /// Writes a status/diagnostic line that is not an error (DevCapture samples,
        /// arena start summary). Never counted in ErrorCount/WarningCount.
        public static void Note(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            string line = "[" + Time.realtimeSinceStartup.ToString("0.00") + "s] NOTE: " + message;
            Push(line);
            if (_writer == null) return;
            try { _writer.WriteLine(line); } catch { }
        }

        static void Push(string line)
        {
            _recent.Add(line);
            if (_recent.Count > RecentCapacity) _recent.RemoveAt(0);
        }

        /// <summary>One or two lines for the pause screen.</summary>
        public static string Summary()
        {
            string s = "errors " + ErrorCount + " / warnings " + WarningCount;
            if (ErrorCount > 0) s += "\nlast: " + LastError;
            if (!string.IsNullOrEmpty(Path)) s += "\nlog: " + Path;
            return s;
        }

        /// <summary>Last <paramref name="maxBytes"/> of the log file (whole file when smaller), or "" when unreadable.</summary>
        public static string ReadTail(int maxBytes)
        {
            try
            {
                if (string.IsNullOrEmpty(Path) || !File.Exists(Path)) return "";
                using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, fs.Length - maxBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[fs.Length - start];
                    int read = fs.Read(buffer, 0, buffer.Length);
                    string text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                    return start > 0 ? "…(truncated)…\n" + text : text;
                }
            }
            catch { return ""; }
        }
    }
}
