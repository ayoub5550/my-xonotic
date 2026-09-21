using UnityEngine;
using UnityEngine.UI;

namespace MyXonotic
{
    /// <summary>
    /// Runtime-built UGUI HUD: health/armor/ammo/frags readout, the mandatory
    /// "DEVELOPMENT SLICE" banner, safe-area aware layout, a pause overlay, and
    /// the OnGUI touch-control visuals (dynamic joystick, FIRE/JUMP/WPN-/WPN+/
    /// PAUSE). Touch visuals are purely cosmetic and share TouchLayout with
    /// Player.cs, which owns all real finger-id tracking/hit-testing — Hud only
    /// mirrors Player's read-only touch state to draw the joystick and does not
    /// itself receive input. Colors intentionally match the owner's LibreQuake
    /// touch reference (my-librequake Assets/LQ/Scripts/UI/TouchControls.cs):
    /// translucent red FIRE, blue JUMP, pale WPN-/WPN+, and a dynamic left
    /// joystick; PAUSE here is brown per this project's explicit style request.
    /// Everything is created in code — no prefabs/scene assets needed.
    /// Functional prototype styling only; original non-branded debug colors.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public Actor Player;
        public WeaponController Weapons;

        // Preserve the existing touch palette. The historical brand document
        // is not present in this checkout; no new brand provenance is asserted.
        static readonly Color FireColor = new Color32(204, 51, 26, 140);       // rgba(204,51,26,0.55)
        static readonly Color JumpColor = new Color32(51, 128, 230, 128);      // rgba(51,128,230,0.5)
        static readonly Color WeaponColor = new Color32(255, 255, 255, 89);    // rgba(255,255,255,0.35)
        static readonly Color AltColor = new Color32(255, 255, 255, 89);       // same "utility" tint, smaller circle
        static readonly Color StickBaseColor = new Color32(255, 255, 255, 64); // rgba(255,255,255,0.25)
        static readonly Color StickKnobColor = new Color32(255, 230, 179, 128);// rgba(255,230,179,0.5)
        static readonly Color LabelColor = new Color32(255, 217, 153, 255);    // rgb(255,217,153)
        static readonly Color LabelShadowColor = new Color32(0, 0, 0, 230);    // rgba(0,0,0,0.9)
        static readonly Color PauseColor = new Color32(64, 46, 31, 235);       // rgba(64,46,31,0.92) "menu"

        Text _statusText;
        Text _bannerText;
        Text _pauseText;
        GameObject _pausePanel;
        RectTransform _safeAreaRoot;

        Texture2D _discTexture;
        Texture2D _ringTexture;
        GUIStyle _buttonLabelStyle;

        public static Hud Build(Transform parent)
        {
            var canvasGO = new GameObject("HUD_Canvas");
            canvasGO.transform.SetParent(parent, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGO.AddComponent<GraphicRaycaster>();

            var hud = canvasGO.AddComponent<Hud>();
            hud.BuildLayout(canvasGO.transform);
            return hud;
        }

        void BuildLayout(Transform canvasTransform)
        {
            var safeGO = new GameObject("SafeArea", typeof(RectTransform));
            safeGO.transform.SetParent(canvasTransform, false);
            _safeAreaRoot = safeGO.GetComponent<RectTransform>();
            _safeAreaRoot.anchorMin = Vector2.zero;
            _safeAreaRoot.anchorMax = Vector2.one;
            _safeAreaRoot.offsetMin = Vector2.zero;
            _safeAreaRoot.offsetMax = Vector2.zero;
            ApplySafeArea();

            _bannerText = CreateText("Banner", _safeAreaRoot, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -20f), new Vector2(0f, 30f), 22, TextAnchor.UpperCenter, Color.yellow);
            _bannerText.text = "my-xonotic " + Application.version + " — development build";

            _statusText = CreateText("Status", _safeAreaRoot, new Vector2(0f, 0f), new Vector2(0.5f, 0f),
                new Vector2(20f, 20f), new Vector2(0f, 90f), 20, TextAnchor.LowerLeft, Color.white);
            CreateText("Crosshair", _safeAreaRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(40, 40), 28, TextAnchor.MiddleCenter, Color.white).text = "+";

            _pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            _pausePanel.transform.SetParent(_safeAreaRoot, false);
            var img = _pausePanel.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.65f);
            var rt = _pausePanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _pauseText = CreateText("PauseText", _pausePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(600f, 100f), 28, TextAnchor.MiddleCenter, Color.white);
            _pauseText.text = "PAUSED\nTouch RESUME, RESTART or MAIN MENU\nDesktop: P / R / M";
            _pausePanel.SetActive(false);
        }

        Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        void ApplySafeArea()
        {
            if (_safeAreaRoot == null) return;
            Rect safe = TouchLayout.Safe;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Vector2 anchorMin = safe.position;
            Vector2 anchorMax = safe.position + safe.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;
            _safeAreaRoot.anchorMin = anchorMin;
            _safeAreaRoot.anchorMax = anchorMax;
        }

        void Update()
        {
            ApplySafeArea();
            if (Player == null || _statusText == null) return;
            string weaponName = Weapons != null ? Weapons.GetDef(Weapons.Current).Name : "-";
            int ammo = Weapons != null ? Weapons.GetAmmo(Weapons.Current) : 0;
            _statusText.text =
                $"HP {Player.Health}  AR {Player.Armor}  AMMO {ammo}  [{weaponName}]\n" +
                $"FRAGS {Player.Frags}  DEATHS {Player.Deaths}";
            var arena = ArenaBootstrap.Instance;
            if (arena != null && arena.Match != null)
            {
                int seconds = Mathf.FloorToInt(arena.Match.ElapsedSeconds);
                _statusText.text += $"\nDM  {seconds / 60:00}:{seconds % 60:00}  LIMIT {arena.FragLimit}";
                if (arena.MatchFinished)
                {
                    var result = arena.Match.Result;
                    string winner = result.IsTie ? "TIE" : result.Winner.DisplayName + " WINS";
                    _pauseText.text = "MATCH OVER — " + winner + "\n" + result.Reason +
                        "\nTouch RESTART / Desktop R";
                }
                else _pauseText.text = "PAUSED\nTouch RESUME, RESTART or MAIN MENU\nDesktop: P / R / M";
            }
            if (_pausePanel != null) _pausePanel.SetActive(ArenaBootstrap.IsPaused);
        }

        static Rect GuiRect(Rect screenRect) => new Rect(screenRect.x,
            Screen.height - screenRect.yMax, screenRect.width, screenRect.height);

        // Filled soft-edge disc, tinted per-draw via GUI.color. Used for FIRE/JUMP/
        // WPN buttons and the joystick knob.
        Texture2D DiscTexture()
        {
            if (_discTexture != null) return _discTexture;
            _discTexture = MakeRingTexture(0f, 1f);
            return _discTexture;
        }

        // Hollow soft-edge ring, used for the joystick base so the dynamic stick
        // reads as a track rather than a second solid button.
        Texture2D RingTexture()
        {
            if (_ringTexture != null) return _ringTexture;
            _ringTexture = MakeRingTexture(0.82f, 0.98f);
            return _ringTexture;
        }

        static Texture2D MakeRingTexture(float innerFrac, float outerFrac)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float c = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c;
                    bool inside = d <= outerFrac && d >= innerFrac;
                    float edge = Mathf.Clamp01((outerFrac - d) * size * 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(inside ? 255 * edge : 0));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Rect CenterRect(Vector2 centerScreen, float diameter) => new Rect(
            centerScreen.x - diameter * 0.5f, centerScreen.y - diameter * 0.5f, diameter, diameter);

        void DrawDisc(Rect screenRect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(GuiRect(screenRect), DiscTexture());
            GUI.color = prev;
        }

        void DrawRing(Rect screenRect, Color color)
        {
            var prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(GuiRect(screenRect), RingTexture());
            GUI.color = prev;
        }

        // brand/DESIGN.md: "Labels are about 22% of button diameter, with a dark
        // drop shadow." Font size therefore scales per-button (not a single global
        // size) and every label is drawn twice: a 1-2px black shadow offset, then
        // the brand label color on top.
        void DrawButtonLabel(Rect buttonRect, string text)
        {
            if (_buttonLabelStyle == null)
                _buttonLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _buttonLabelStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(buttonRect.height * 0.22f));

            Rect guiRect = GuiRect(buttonRect);
            float shadowOffset = Mathf.Max(1f, buttonRect.height * 0.015f);

            _buttonLabelStyle.normal.textColor = LabelShadowColor;
            GUI.Label(new Rect(guiRect.x + shadowOffset, guiRect.y + shadowOffset, guiRect.width, guiRect.height),
                text, _buttonLabelStyle);

            _buttonLabelStyle.normal.textColor = LabelColor;
            GUI.Label(guiRect, text, _buttonLabelStyle);
        }

        void OnGUI()
        {
            // Visuals and input share TouchLayout for position/size; Player.cs owns
            // all real finger-id hit-testing. Hud only mirrors Player's read-only
            // touch state (joystick origin/knob, held flags) to draw the controls.
            var boxStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.025f), 14, 36)
            };

            bool isPaused = ArenaBootstrap.IsPaused;

            var prevColor = GUI.color;
            GUI.color = PauseColor;
            GUI.Box(GuiRect(TouchLayout.Pause), string.Empty, boxStyle);
            GUI.color = prevColor;
            bool finished = ArenaBootstrap.Instance != null && ArenaBootstrap.Instance.MatchFinished;
            GUI.Box(GuiRect(TouchLayout.Pause), finished ? "FINISHED" : isPaused ? "RESUME" : "PAUSE", boxStyle);

            if (isPaused)
            {
                GUI.Box(GuiRect(TouchLayout.Restart), "RESTART MATCH", boxStyle);
                if (MyXonotic.Menu.SceneFlow.HasMainMenu())
                {
                    GUI.color = PauseColor;
                    GUI.Box(GuiRect(TouchLayout.MainMenu), string.Empty, boxStyle);
                    GUI.color = Color.white;
                    GUI.Box(GuiRect(TouchLayout.MainMenu), "MAIN MENU", boxStyle);
                }
                return;
            }

            if (!Application.isMobilePlatform && !Application.isEditor) return;

            var player = ArenaBootstrap.Instance != null ? ArenaBootstrap.Instance.PlayerComponent : null;

            // Dynamic left joystick: only visible while a finger is actually
            // driving it, appearing exactly where that finger touched down.
            if (player != null && player.TouchJoystickActive)
            {
                DrawRing(CenterRect(player.TouchJoystickOrigin, TouchLayout.JoystickBaseRadius * 2f), StickBaseColor);
                DrawDisc(CenterRect(player.TouchJoystickOrigin + player.TouchJoystickKnobOffset,
                    TouchLayout.JoystickKnobRadius * 2f), StickKnobColor);
            }

            DrawDisc(TouchLayout.Fire, FireColor);
            DrawButtonLabel(TouchLayout.Fire, "FIRE");

            // Small utility circle, deliberately plainer than FIRE — Xonotic
            // secondary-fire modes are real gameplay, kept even though the
            // owner's LibreQuake reference screenshot has no equivalent button.
            DrawDisc(TouchLayout.Alt, AltColor);
            DrawButtonLabel(TouchLayout.Alt, "ALT");

            DrawDisc(TouchLayout.Jump, JumpColor);
            DrawButtonLabel(TouchLayout.Jump, "JUMP");

            DrawDisc(TouchLayout.WpnMinus, WeaponColor);
            DrawButtonLabel(TouchLayout.WpnMinus, "WPN -");

            DrawDisc(TouchLayout.WpnPlus, WeaponColor);
            DrawButtonLabel(TouchLayout.WpnPlus, "WPN +");

            // Right drag = look. Intentionally no visible disc/reticle under the
            // thumb here — only the dynamic joystick (left) and the buttons get a
            // drawn control surface.
        }
    }
}
