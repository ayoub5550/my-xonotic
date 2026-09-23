using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// dev.13 touch-control preferences chosen on the menu SETTINGS page and read
    /// by <see cref="TouchLayout"/> (button size, left-handed mirror) and
    /// <see cref="Player"/> (look sensitivity, inverted Y). Persisted with
    /// PlayerPrefs like <see cref="MatchSettings"/>; tests set the properties
    /// directly (values are clamped, nothing else is read after that).
    /// </summary>
    public static class TouchSettings
    {
        const string ScaleKey = "mx_touch_scale";
        const string SensitivityKey = "mx_touch_sens";
        const string InvertKey = "mx_touch_inverty";
        const string LeftKey = "mx_touch_left";

        public const float MinButtonScale = 0.8f, MaxButtonScale = 1.4f, ButtonScaleStep = 0.1f;
        public const float MinSensitivity = 0.5f, MaxSensitivity = 2.0f, SensitivityStep = 0.1f;

        static bool _loaded;
        static float _scale = 1f;
        static float _sensitivity = 1f;
        static bool _invertY;
        static bool _leftHanded;

        /// Multiplier on every round button / joystick radius (0.8-1.4).
        public static float ButtonScale
        {
            get { Load(); return _scale; }
            set { Load(); _scale = Mathf.Clamp(Snap(value, ButtonScaleStep), MinButtonScale, MaxButtonScale); PlayerPrefs.SetFloat(ScaleKey, _scale); }
        }

        /// Multiplier on the touch look speed (0.5-2.0).
        public static float Sensitivity
        {
            get { Load(); return _sensitivity; }
            set { Load(); _sensitivity = Mathf.Clamp(Snap(value, SensitivityStep), MinSensitivity, MaxSensitivity); PlayerPrefs.SetFloat(SensitivityKey, _sensitivity); }
        }

        public static bool InvertY
        {
            get { Load(); return _invertY; }
            set { Load(); _invertY = value; PlayerPrefs.SetInt(InvertKey, value ? 1 : 0); }
        }

        /// Mirror the layout: joystick on the right half, FIRE/JUMP/WPN cluster on the left.
        public static bool LeftHanded
        {
            get { Load(); return _leftHanded; }
            set { Load(); _leftHanded = value; PlayerPrefs.SetInt(LeftKey, value ? 1 : 0); }
        }

        public static void ResetToDefaults()
        {
            ButtonScale = 1f; Sensitivity = 1f; InvertY = false; LeftHanded = false;
        }

        /// Test hook: forget persisted values and use the given ones (no PlayerPrefs write).
        public static void OverrideForTest(float scale, float sensitivity, bool invertY, bool leftHanded)
        {
            _loaded = true;
            _scale = Mathf.Clamp(scale, MinButtonScale, MaxButtonScale);
            _sensitivity = Mathf.Clamp(sensitivity, MinSensitivity, MaxSensitivity);
            _invertY = invertY;
            _leftHanded = leftHanded;
        }

        static float Snap(float v, float step) => Mathf.Round(v / step) * step;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            if (!Application.isPlaying && Application.isBatchMode) return;
            _scale = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, 1f), MinButtonScale, MaxButtonScale);
            _sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), MinSensitivity, MaxSensitivity);
            _invertY = PlayerPrefs.GetInt(InvertKey, 0) != 0;
            _leftHanded = PlayerPrefs.GetInt(LeftKey, 0) != 0;
        }
    }
}
