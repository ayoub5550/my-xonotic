using UnityEngine;
using UnityEngine.UI;

namespace MyXonotic
{
    /// <summary>
    /// Runtime-built UGUI HUD: health/armor/ammo/frags readout, the mandatory
    /// "DEVELOPMENT SLICE" banner, safe-area aware layout and a pause overlay.
    /// Everything is created in code — no prefabs/scene assets needed.
    /// Functional prototype styling only; original non-branded debug colors.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public Actor Player;
        public WeaponController Weapons;

        Text _statusText;
        Text _bannerText;
        GameObject _pausePanel;
        RectTransform _safeAreaRoot;

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
            _bannerText.text = "DEVELOPMENT SLICE — NOT FULL XONOTIC";

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
            Text pauseText = CreateText("PauseText", _pausePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(600f, 100f), 28, TextAnchor.MiddleCenter, Color.white);
            pauseText.text = "PAUSED\nTouch RESUME or RESTART\nDesktop: P / R";
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
            if (_pausePanel != null) _pausePanel.SetActive(ArenaBootstrap.IsPaused);
        }

        static Rect GuiRect(Rect screenRect) => new Rect(screenRect.x,
            Screen.height - screenRect.yMax, screenRect.width, screenRect.height);

        void OnGUI()
        {
            // Visuals and input share TouchLayout. These are intentionally non-clickable
            // boxes: raw finger IDs in Player/Bootstrap own all multi-touch state.
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.025f), 14, 36)
            };
            GUI.Box(GuiRect(TouchLayout.Pause), ArenaBootstrap.IsPaused ? "RESUME" : "PAUSE", style);
            if (ArenaBootstrap.IsPaused)
            {
                GUI.Box(GuiRect(TouchLayout.Restart), "RESTART MATCH", style);
                return;
            }
            if (Application.isMobilePlatform || Application.isEditor)
            {
                GUI.Box(GuiRect(TouchLayout.Fire), "FIRE", style);
                GUI.Box(GuiRect(TouchLayout.Alt), "ALT", style);
                GUI.Box(GuiRect(TouchLayout.Jump), "JUMP", style);
                GUI.Box(GuiRect(TouchLayout.Next), "NEXT", style);
                var safe = TouchLayout.Safe;
                GUI.Label(GuiRect(new Rect(safe.x + 20, safe.y + safe.height * 0.25f,
                    safe.width * 0.4f, safe.height * 0.06f)), "LEFT: MOVE  •  RIGHT: LOOK", style);
            }
        }
    }
}
