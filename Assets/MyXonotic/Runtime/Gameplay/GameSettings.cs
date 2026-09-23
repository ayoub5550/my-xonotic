using UnityEngine;

namespace MyXonotic
{
    /// <summary>Video quality preset chosen on the SETTINGS → VIDEO tab.</summary>
    public enum EffectsLevel { Low = 0, Medium = 1, High = 2 }

    /// <summary>
    /// dev.18: video and audio preferences behind the redesigned settings page
    /// (Xonotic-style tabs VIDEO / AUDIO / CONTROLS / GAME). Saved immediately on
    /// every change (PlayerPrefs + Save) and applied by <see cref="Apply"/> when a
    /// scene starts (ArenaBootstrap / MainMenu) or right away from the menu.
    /// Touch-specific values stay in <see cref="TouchSettings"/>, match options in
    /// <see cref="MatchSettings"/>. Tests use <see cref="OverrideForTest"/>.
    /// </summary>
    public static class GameSettings
    {
        const string EffectsKey = "mx_fx";
        const string BloomKey = "mx_bloom";
        const string FpsKey = "mx_fps";
        const string ShowFpsKey = "mx_showfps";
        const string MasterKey = "mx_vol_master";
        const string MusicKey = "mx_vol_music";
        const string SfxKey = "mx_vol_sfx";

        public static readonly int[] FpsChoices = { 30, 60 };
        public const float DefaultMusicVolume = 0.35f; // MapMusic.volume before dev.18

        static bool _loaded;
        static EffectsLevel _effects = EffectsLevel.Medium;
        static bool _bloom = true;
        static int _fps = 60;
        static bool _showFps = true;
        static float _master = 1f, _music = DefaultMusicVolume, _sfx = 1f;

        /// Low = no bloom, no smoke/fireball particles, adaptive resolution allowed to drop further.
        public static EffectsLevel Effects
        {
            get { Load(); return _effects; }
            set { Load(); _effects = value; PlayerPrefs.SetInt(EffectsKey, (int)value); Save(); }
        }

        public static bool Bloom
        {
            get { Load(); return _bloom; }
            set { Load(); _bloom = value; PlayerPrefs.SetInt(BloomKey, value ? 1 : 0); Save(); }
        }

        /// Effective bloom: the toggle AND not the Low preset.
        public static bool BloomActive => Bloom && Effects != EffectsLevel.Low;

        public static int TargetFps
        {
            get { Load(); return _fps; }
            set { Load(); _fps = value == 30 ? 30 : 60; PlayerPrefs.SetInt(FpsKey, _fps); Save(); }
        }

        public static bool ShowFps
        {
            get { Load(); return _showFps; }
            set { Load(); _showFps = value; PlayerPrefs.SetInt(ShowFpsKey, value ? 1 : 0); Save(); }
        }

        public static float MasterVolume
        {
            get { Load(); return _master; }
            set { Load(); _master = Mathf.Clamp01(value); PlayerPrefs.SetFloat(MasterKey, _master); Save(); ApplyAudio(); }
        }

        public static float MusicVolume
        {
            get { Load(); return _music; }
            set { Load(); _music = Mathf.Clamp01(value); PlayerPrefs.SetFloat(MusicKey, _music); Save(); ApplyAudio(); }
        }

        /// Weapon / pickup / impact volume multiplier (WeaponAudio.PlayAt).
        public static float SfxVolume
        {
            get { Load(); return _sfx; }
            set { Load(); _sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat(SfxKey, _sfx); Save(); }
        }

        public static void ResetToDefaults()
        {
            Effects = EffectsLevel.Medium; Bloom = true; TargetFps = 60; ShowFps = true;
            MasterVolume = 1f; MusicVolume = DefaultMusicVolume; SfxVolume = 1f;
        }

        /// Apply frame cap and audio; the camera bloom reads BloomActive itself.
        public static void Apply()
        {
            Application.targetFrameRate = TargetFps;
            ApplyAudio();
        }

        static void ApplyAudio()
        {
            AudioListener.volume = MasterVolume;
            foreach (var m in Object.FindObjectsOfType<MyXonotic.Gameplay.MapMusic>()) m.ApplyVolume();
        }

        static void Save() { if (Application.isPlaying) PlayerPrefs.Save(); }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            if (!Application.isPlaying && Application.isBatchMode) return;
            _effects = (EffectsLevel)Mathf.Clamp(PlayerPrefs.GetInt(EffectsKey, (int)EffectsLevel.Medium), 0, 2);
            _bloom = PlayerPrefs.GetInt(BloomKey, 1) != 0;
            _fps = PlayerPrefs.GetInt(FpsKey, 60) == 30 ? 30 : 60;
            _showFps = PlayerPrefs.GetInt(ShowFpsKey, 1) != 0;
            _master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
            _music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusicVolume));
            _sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
        }

        /// Test hook: forget persisted values (no PlayerPrefs write).
        public static void OverrideForTest(EffectsLevel effects, bool bloom, int fps, float master, float music, float sfx)
        {
            _loaded = true;
            _effects = effects; _bloom = bloom; _fps = fps == 30 ? 30 : 60;
            _master = Mathf.Clamp01(master); _music = Mathf.Clamp01(music); _sfx = Mathf.Clamp01(sfx);
        }

        public static string EffectsName(EffectsLevel level)
        {
            switch (level) { case EffectsLevel.Low: return "LOW"; case EffectsLevel.High: return "HIGH"; default: return "MEDIUM"; }
        }
    }
}
