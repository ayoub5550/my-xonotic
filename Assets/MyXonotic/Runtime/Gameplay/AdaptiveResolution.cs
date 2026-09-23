using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.16: Test Lab showed Galaxy A15 (Android 14, mid-range) at 30–43 FPS while
    /// the S24 held 60. This drops the render resolution in steps when the average
    /// frame time stays above budget, and restores it when there is headroom.
    /// Plain Screen.SetResolution (works on GLES and Vulkan; UI canvases and
    /// TouchLayout read Screen.width/height so they follow). Added by ArenaBootstrap
    /// on mobile only; the menu keeps native resolution.
    /// </summary>
    public sealed class AdaptiveResolution : MonoBehaviour
    {
        public static readonly float[] Scales = { 1f, 0.85f, 0.7f, 0.6f };
        /// Seconds of frames averaged per decision (skip the first WarmupSeconds after load).
        public const float WindowSeconds = 3f;
        public const float WarmupSeconds = 4f;
        /// Budget: 60 FPS target; step down above 22 ms (~45 FPS), step up below 14 ms (~70 FPS headroom).
        public const float DownMs = 22f;
        public const float UpMs = 14f;

        public static int Level { get; private set; }
        public static float CurrentScale => Scales[Level];

        int _nativeW, _nativeH;
        float _accum, _time;
        int _frames;

        void Awake()
        {
            _nativeW = Screen.width;
            _nativeH = Screen.height;
            if (Level != 0) Apply(); // keep the level chosen in the previous match
            _time = -WarmupSeconds;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            if (_time < 0f) return;
            _accum += dt;
            _frames++;
            if (_accum < WindowSeconds) return;
            float avgMs = _accum / _frames * 1000f;
            _accum = 0f;
            _frames = 0;
            int next = Decide(Level, avgMs);
            if (next == Level) return;
            Level = next;
            Apply();
        }

        /// Pure (tested): next level for the measured average frame time.
        public static int Decide(int level, float avgMs)
        {
            if (avgMs > DownMs && level < Scales.Length - 1) return level + 1;
            if (avgMs < UpMs && level > 0) return level - 1;
            return level;
        }

        void Apply()
        {
            float s = Scales[Level];
            int w = Mathf.Max(320, Mathf.RoundToInt(_nativeW * s));
            int h = Mathf.Max(240, Mathf.RoundToInt(_nativeH * s));
            Screen.SetResolution(w, h, Screen.fullScreenMode);
            RuntimeErrorLog.Note("AdaptiveResolution level " + Level + " -> " + w + "x" + h);
        }
    }
}
