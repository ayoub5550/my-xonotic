using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyXonotic.Menu
{
    public enum SettingsTab { Video = 0, Audio = 1, Controls = 2, Game = 3 }

    /// <summary>
    /// dev.18: the redesigned SETTINGS screen (ROADMAP item 2 from the owner's Poco F3
    /// test: "صفحة الإعدادات سيئة"). Xonotic-menu style tabs VIDEO / AUDIO / CONTROLS /
    /// GAME, one panel per tab, big touch rows (≥ 56 px at 1280x720 reference): sliders
    /// with a fat handle, choice buttons, toggles. Every change is saved immediately by
    /// the settings classes (GameSettings / TouchSettings / MatchSettings) — there is no
    /// APPLY button. CONTROLS carries a live sensitivity preview pad. Built entirely in
    /// code like the rest of MainMenu (positive rects, RectMask2D-free, LocalTests
    /// MainMenuGeometry walks every tab).
    /// </summary>
    public sealed class SettingsPage
    {
        public const float RowH = 56f;
        public const float RowGap = 10f;
        public const float TabH = 60f;

        readonly MainMenu.Widgets _w;
        readonly Dictionary<SettingsTab, GameObject> _tabs = new Dictionary<SettingsTab, GameObject>();
        readonly Dictionary<SettingsTab, Image> _tabButtons = new Dictionary<SettingsTab, Image>();
        readonly List<System.Action> _refreshers = new List<System.Action>();
        public SettingsTab Current { get; private set; } = SettingsTab.Video;
        public RectTransform Root { get; private set; }

        public SettingsPage(MainMenu.Widgets widgets) { _w = widgets; }

        public GameObject Build(RectTransform screen)
        {
            Root = screen;
            float x = 20f, tabW = (1240f - 3f * 12f) / 4f;
            foreach (SettingsTab tab in System.Enum.GetValues(typeof(SettingsTab)))
            {
                var t = tab;
                var b = _w.Button("Tab" + tab, screen, tab.ToString().ToUpperInvariant(), () => Show(t));
                _w.Rect(b.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(x, -86f - TabH), new Vector2(x + tabW, -86f));
                b.GetComponentInChildren<Text>().fontSize = 20;
                _tabButtons[tab] = b.GetComponent<Image>();
                x += tabW + 12f;
            }
            var panel = _w.Image("SettingsPanel", screen, MainMenu.PanelColor);
            _w.Rect(panel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(20f, 16f), new Vector2(-20f, -158f));

            _tabs[SettingsTab.Video] = BuildVideo(panel.rectTransform);
            _tabs[SettingsTab.Audio] = BuildAudio(panel.rectTransform);
            _tabs[SettingsTab.Controls] = BuildControls(panel.rectTransform);
            _tabs[SettingsTab.Game] = BuildGame(panel.rectTransform);
            Show(SettingsTab.Video);
            return screen.gameObject;
        }

        public void Show(SettingsTab tab)
        {
            Current = tab;
            foreach (var kv in _tabs) kv.Value.SetActive(kv.Key == tab);
            foreach (var kv in _tabButtons) kv.Value.color = kv.Key == tab ? MainMenu.Accent : MainMenu.CardColor;
            Refresh();
        }

        public void Refresh() { foreach (var r in _refreshers) r(); }

        RectTransform NewTab(RectTransform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            _w.Rect(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 12f), new Vector2(-16f, -12f));
            return rt;
        }

        // ------------------------------------------------------------------ VIDEO

        GameObject BuildVideo(RectTransform parent)
        {
            var tab = NewTab(parent, "Video");
            float y = -8f;
            ChoiceRow(tab, "Effects", "EFFECTS", y, new[] { "LOW", "MEDIUM", "HIGH" },
                () => (int)GameSettings.Effects, i => GameSettings.Effects = (EffectsLevel)i);
            y -= RowH + RowGap;
            ToggleRow(tab, "Bloom", "BLOOM (GLOW)", y, () => GameSettings.Bloom, v => GameSettings.Bloom = v);
            y -= RowH + RowGap;
            ChoiceRow(tab, "Fps", "FRAME RATE CAP", y, new[] { "30 FPS", "60 FPS" },
                () => GameSettings.TargetFps == 30 ? 0 : 1, i => GameSettings.TargetFps = i == 0 ? 30 : 60);
            y -= RowH + RowGap;
            ToggleRow(tab, "ShowFps", "SHOW FPS / DEBUG LINE", y, () => GameSettings.ShowFps, v => GameSettings.ShowFps = v);
            y -= RowH + RowGap;
            var hint = _w.Text("VideoHint", tab, "LOW turns off bloom and particle smoke. Render resolution still adapts automatically to keep the frame rate.", 13, TextAnchor.UpperLeft, MainMenu.SubText, FontStyle.Normal);
            _w.Rect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - 60f), new Vector2(0f, y));
            return tab.gameObject;
        }

        // ------------------------------------------------------------------ AUDIO

        GameObject BuildAudio(RectTransform parent)
        {
            var tab = NewTab(parent, "Audio");
            float y = -8f;
            SliderRow(tab, "Master", "MASTER VOLUME", y, 0f, 1f, 0.05f, () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, Percent);
            y -= RowH + RowGap;
            SliderRow(tab, "Music", "MUSIC", y, 0f, 1f, 0.05f, () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, Percent);
            y -= RowH + RowGap;
            SliderRow(tab, "Sfx", "WEAPONS & EFFECTS", y, 0f, 1f, 0.05f, () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v, Percent);
            return tab.gameObject;
        }

        // --------------------------------------------------------------- CONTROLS

        GameObject BuildControls(RectTransform parent)
        {
            var tab = NewTab(parent, "Controls");
            float y = -8f;
            // Left column of rows (60 % width), preview pad on the right.
            var col = new GameObject("Rows", typeof(RectTransform)).GetComponent<RectTransform>();
            col.SetParent(tab, false);
            _w.Rect(col, new Vector2(0f, 0f), new Vector2(0.62f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            SliderRow(col, "ButtonScale", "BUTTON SIZE", y, TouchSettings.MinButtonScale, TouchSettings.MaxButtonScale, TouchSettings.ButtonScaleStep,
                () => TouchSettings.ButtonScale, v => TouchSettings.ButtonScale = v, v => v.ToString("0.0") + "×");
            y -= RowH + RowGap;
            SliderRow(col, "Sensitivity", "LOOK SENSITIVITY", y, TouchSettings.MinSensitivity, TouchSettings.MaxSensitivity, TouchSettings.SensitivityStep,
                () => TouchSettings.Sensitivity, v => TouchSettings.Sensitivity = v, v => v.ToString("0.0") + "×");
            y -= RowH + RowGap;
            ToggleRow(col, "InvertY", "INVERT LOOK (Y)", y, () => TouchSettings.InvertY, v => TouchSettings.InvertY = v);
            y -= RowH + RowGap;
            ToggleRow(col, "LeftHanded", "LEFT-HANDED LAYOUT", y, () => TouchSettings.LeftHanded, v => TouchSettings.LeftHanded = v);
            y -= RowH + RowGap;
            var reset = _w.Button("ResetTouch", col, "RESET DEFAULTS", () => { TouchSettings.ResetToDefaults(); Refresh(); });
            _w.Rect(reset.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - 48f), new Vector2(240f, y));
            reset.GetComponentInChildren<Text>().fontSize = 15;

            // Sensitivity preview: drag anywhere on the pad, the reticle moves as the view would.
            var pad = _w.Image("SensitivityPad", tab, new Color32(20, 24, 34, 255));
            _w.Rect(pad.rectTransform, new Vector2(0.65f, 0f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 40f), new Vector2(0f, -30f));
            pad.raycastTarget = true;
            var padLabel = _w.Text("PadLabel", tab, "SENSITIVITY PREVIEW — drag here", 13, TextAnchor.MiddleLeft, MainMenu.SubText, FontStyle.Normal);
            _w.Rect(padLabel.rectTransform, new Vector2(0.65f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, -28f), new Vector2(0f, -2f));
            var reticle = _w.Image("Reticle", pad.transform, MainMenu.Accent);
            reticle.rectTransform.anchorMin = reticle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            reticle.rectTransform.sizeDelta = new Vector2(18f, 18f);
            var cross = HudArt.Sprite("crosshair");
            if (cross != null) { reticle.sprite = cross; reticle.preserveAspect = true; reticle.rectTransform.sizeDelta = new Vector2(40f, 40f); }
            var preview = pad.gameObject.AddComponent<SensitivityPreview>();
            preview.Reticle = reticle.rectTransform;
            return tab.gameObject;
        }

        // ------------------------------------------------------------------- GAME

        GameObject BuildGame(RectTransform parent)
        {
            var tab = NewTab(parent, "Game");
            float y = -8f;
            SliderRow(tab, "BotSkill", "BOT DIFFICULTY", y, MatchSettings.MinBotSkill, MatchSettings.MaxBotSkill, 1f,
                () => MatchSettings.BotSkill, v => MatchSettings.BotSkill = Mathf.RoundToInt(v),
                v => MatchSettings.BotSkillName(Mathf.RoundToInt(v)) + "  " + MatchSettings.BotSkillFor(0) + " / " + MatchSettings.BotSkillFor(1) + " / " + MatchSettings.BotSkillFor(2));
            y -= RowH + RowGap;
            SliderRow(tab, "BotCount", "BOTS", y, MatchSettings.MinBots, MatchSettings.MaxBots, 1f,
                () => MatchSettings.BotCount, v => MatchSettings.BotCount = Mathf.RoundToInt(v), v => Mathf.RoundToInt(v).ToString());
            y -= RowH + RowGap;
            ChoiceRow(tab, "Mode", "MODE", y, new[] { "DM", "TDM", "CTF" }, () => (int)MatchSettings.Mode, i => MatchSettings.Mode = (GameMode)i);
            y -= RowH + RowGap;
            ToggleRow(tab, "AllWeapons", "ALL WEAPONS (WEAPON ARENA)", y, () => MatchSettings.AllWeapons, v => MatchSettings.AllWeapons = v);
            y -= RowH + RowGap;
            var hint = _w.Text("GameHint", tab,
                "Xonotic skill 1–10. Default 3 / 2 / 1 for touch (the original server default 8 was too hard on a phone). Bots aim, turn and fire like the original bot AI at that skill.",
                13, TextAnchor.UpperLeft, MainMenu.SubText, FontStyle.Normal);
            _w.Rect(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0.6f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - 60f), new Vector2(0f, y));

            // DevCapture report (moved here from the old page).
            var dev = _w.Image("DevPanel", tab, new Color32(0, 24, 44, 160));
            _w.Rect(dev.rectTransform, new Vector2(0.62f, 0f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, y - 4f));
            var devHeading = _w.Text("DevHeading", dev.transform, "DEVICE REPORT", 15, TextAnchor.MiddleLeft, MainMenu.Accent, FontStyle.Bold);
            _w.Rect(devHeading.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(12f, -34f), new Vector2(-12f, -6f));
            var summary = _w.Text("DevSummary", dev.transform, "", 12, TextAnchor.UpperLeft, MainMenu.TextColor, FontStyle.Normal);
            _w.Rect(summary.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(12f, 66f), new Vector2(-12f, -38f));
            var share = _w.Button("ShareLog", dev.transform, "SHARE LOG", () => DevCapture.Share());
            share.GetComponent<Image>().color = MainMenu.Accent;
            _w.Rect(share.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(12f, 10f), new Vector2(-6f, 58f));
            share.GetComponentInChildren<Text>().fontSize = 15;
            var copy = _w.Button("CopyLog", dev.transform, "COPY LOG", () => { DevCapture.Copy(); Refresh(); });
            _w.Rect(copy.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(6f, 10f), new Vector2(-12f, 58f));
            copy.GetComponentInChildren<Text>().fontSize = 15;
            _refreshers.Add(() =>
            {
                var dc = DevCapture.Instance;
                summary.text = "errors " + RuntimeErrorLog.ErrorCount + " · warnings " + RuntimeErrorLog.WarningCount +
                    (dc != null && dc.SampleCount > 0 ? "\nlast: " + dc.LastSample : "") +
                    (!string.IsNullOrEmpty(RuntimeErrorLog.LastError) ? "\nlast error: " + RuntimeErrorLog.LastError : "") +
                    "\n" + Application.version;
            });
            return tab.gameObject;
        }

        // ------------------------------------------------------------------ rows

        static string Percent(float v) => Mathf.RoundToInt(v * 100f) + " %";

        /// Caption | fat slider | value. Step-snapped; writes through immediately.
        public Slider SliderRow(RectTransform parent, string name, string caption, float y, float min, float max, float step,
            System.Func<float> get, System.Action<float> set, System.Func<float, string> format)
        {
            var label = _w.Text(name + "Caption", parent, caption, 16, TextAnchor.MiddleLeft, MainMenu.TextColor, FontStyle.Bold);
            _w.Rect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0.3f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - RowH), new Vector2(0f, y));

            var go = new GameObject(name + "Slider", typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            _w.Rect(rt, new Vector2(0.31f, 1f), new Vector2(0.78f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - RowH), new Vector2(0f, y));
            var bg = _w.Image("Background", go.transform, MainMenu.CardColor);
            _w.Rect(bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(0f, 8f));
            bg.raycastTarget = true;
            var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
            fillArea.SetParent(go.transform, false);
            _w.Rect(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14f, -8f), new Vector2(-14f, 8f));
            var fill = _w.Image("Fill", fillArea, MainMenu.Accent);
            _w.Rect(fill.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-14f, 0f), new Vector2(14f, 0f));
            var handleArea = new GameObject("HandleArea", typeof(RectTransform)).GetComponent<RectTransform>();
            handleArea.SetParent(go.transform, false);
            _w.Rect(handleArea, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(22f, 0f), new Vector2(-22f, 0f));
            var handle = _w.Image("Handle", handleArea, MainMenu.TextColor);
            _w.Rect(handle.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-22f, 4f), new Vector2(22f, -4f));
            handle.raycastTarget = true;

            var slider = go.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = Mathf.Approximately(step, 1f);

            var value = _w.Text(name + "Value", parent, "", 16, TextAnchor.MiddleLeft, MainMenu.Accent, FontStyle.Bold);
            _w.Rect(value.rectTransform, new Vector2(0.79f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - RowH), new Vector2(0f, y));

            bool syncing = false;
            slider.onValueChanged.AddListener(v =>
            {
                if (syncing) return;
                float snapped = min + Mathf.Round((v - min) / step) * step;
                set(Mathf.Clamp(snapped, min, max));
                float now = get();
                syncing = true; slider.SetValueWithoutNotify(now); syncing = false;
                value.text = format(now);
            });
            _refreshers.Add(() =>
            {
                float now = get();
                syncing = true; slider.SetValueWithoutNotify(now); syncing = false;
                value.text = format(now);
            });
            return slider;
        }

        /// Caption | N choice buttons (the selected one in accent colour).
        public void ChoiceRow(RectTransform parent, string name, string caption, float y, string[] options, System.Func<int> get, System.Action<int> set)
        {
            var label = _w.Text(name + "Caption", parent, caption, 16, TextAnchor.MiddleLeft, MainMenu.TextColor, FontStyle.Bold);
            _w.Rect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0.3f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - RowH), new Vector2(0f, y));
            var images = new Image[options.Length];
            float x0 = 0.31f, w = (0.69f) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = _w.Button(name + "Opt" + i, parent, options[i], () => { set(idx); Refresh(); });
                _w.Rect(b.GetComponent<RectTransform>(), new Vector2(x0 + w * i, 1f), new Vector2(x0 + w * (i + 1), 1f), new Vector2(0f, 1f), new Vector2(i == 0 ? 0f : 5f, y - RowH), new Vector2(i == options.Length - 1 ? 0f : -5f, y));
                b.GetComponentInChildren<Text>().fontSize = 16;
                images[i] = b.GetComponent<Image>();
            }
            _refreshers.Add(() => { int cur = get(); for (int i = 0; i < images.Length; i++) images[i].color = i == cur ? MainMenu.Accent : MainMenu.CardColor; });
        }

        /// Caption | ON/OFF button.
        public void ToggleRow(RectTransform parent, string name, string caption, float y, System.Func<bool> get, System.Action<bool> set)
        {
            var label = _w.Text(name + "Caption", parent, caption, 16, TextAnchor.MiddleLeft, MainMenu.TextColor, FontStyle.Bold);
            _w.Rect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0.55f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - RowH), new Vector2(0f, y));
            var b = _w.Button(name, parent, "", () => { set(!get()); Refresh(); });
            _w.Rect(b.GetComponent<RectTransform>(), new Vector2(0.31f, 1f), new Vector2(0.55f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - RowH), new Vector2(0f, y));
            var state = b.GetComponentInChildren<Text>();
            state.fontSize = 16;
            var img = b.GetComponent<Image>();
            _refreshers.Add(() => { bool on = get(); state.text = on ? "ON" : "OFF"; img.color = on ? MainMenu.Accent : MainMenu.CardColor; });
        }
    }

    /// <summary>Drag pad that moves a reticle by drag × look sensitivity, like Player's touch look.</summary>
    public sealed class SensitivityPreview : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        public RectTransform Reticle;
        /// Reference pixels of reticle travel per pixel of finger travel at sensitivity 1.0.
        public const float Gain = 1.2f;
        Vector2 _offset;

        public void OnPointerDown(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            float invert = TouchSettings.InvertY ? -1f : 1f;
            _offset += new Vector2(e.delta.x, e.delta.y * invert) * TouchSettings.Sensitivity * Gain;
            var half = rt.rect.size * 0.5f - Vector2.one * 20f;
            _offset.x = Mathf.Clamp(_offset.x, -half.x, half.x);
            _offset.y = Mathf.Clamp(_offset.y, -half.y, half.y);
            if (Reticle != null) Reticle.anchoredPosition = _offset;
        }
    }
}
