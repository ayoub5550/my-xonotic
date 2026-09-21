using MyXonotic.Content;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MyXonotic.Menu
{
    /// <summary>
    /// Runtime-built main menu: a scrollable grid of every map in the
    /// MapCatalog (upstream preview image, title, author) — tap a card to load
    /// that map scene. Created entirely in code, like the HUD; no prefabs.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        static readonly Color Background = new Color32(14, 16, 22, 255);
        static readonly Color CardColor = new Color32(30, 34, 46, 255);
        static readonly Color CardPressed = new Color32(60, 70, 96, 255);
        static readonly Color Accent = new Color32(255, 140, 40, 255);
        static readonly Color TextColor = new Color32(235, 235, 240, 255);
        static readonly Color SubText = new Color32(160, 165, 180, 255);

        Font _font;
        MapCatalog _catalog;

        void Start()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _catalog = MapCatalog.Load();
            Build();
        }

        void Build()
        {
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
            var cam = new GameObject("MenuCamera", typeof(Camera));
            cam.transform.SetParent(transform, false);
            var camera = cam.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.cullingMask = 0;

            var canvasGO = new GameObject("MenuCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var bg = MakeImage("Background", canvasGO.transform, Background);
            Stretch(bg.rectTransform, Vector2.zero, Vector2.zero);

            var safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(canvasGO.transform, false);
            Stretch(safe, Vector2.zero, Vector2.zero);
            ApplySafeArea(safe);

            // Header
            var title = MakeText("Title", safe, "MY XONOTIC", 40, TextAnchor.MiddleLeft, Accent, FontStyle.Bold);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -12f), new Vector2(-30f, -62f));
            int count = _catalog != null ? _catalog.maps.Count : 0;
            string version = _catalog != null && !string.IsNullOrEmpty(_catalog.buildVersion) ? _catalog.buildVersion : Application.version;
            var sub = MakeText("Subtitle", safe,
                count + " maps · Unity reimplementation " + version + " · original Xonotic art (GPL) · development build",
                16, TextAnchor.MiddleLeft, SubText, FontStyle.Normal);
            SetRect(sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -64f), new Vector2(-30f, -90f));

            var quit = MakeButton("Quit", safe, "QUIT", () => Application.Quit());
            SetRect(quit.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-150f, -16f), new Vector2(-30f, -60f));

            // Scroll view with grid of cards
            var scrollGO = new GameObject("MapScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGO.transform.SetParent(safe, false);
            var scrollRT = scrollGO.GetComponent<RectTransform>();
            SetRect(scrollRT, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(20f, 20f), new Vector2(-20f, -100f));
            scrollGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            scrollGO.AddComponent<Mask>().showMaskGraphic = false;
            var scroll = scrollGO.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(scrollGO.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(290f, 210f);
            grid.spacing = new Vector2(16f, 16f);
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = scrollRT;

            if (_catalog == null || _catalog.maps.Count == 0)
            {
                var none = MakeText("NoMaps", safe, "No maps were packaged in this build.", 24, TextAnchor.MiddleCenter, TextColor, FontStyle.Normal);
                Stretch(none.rectTransform, Vector2.zero, Vector2.zero);
                return;
            }
            foreach (var entry in _catalog.maps) BuildCard(content, entry);
        }

        void BuildCard(Transform parent, MapCatalog.Entry entry)
        {
            var card = new GameObject("Card_" + entry.mapName, typeof(RectTransform), typeof(Image), typeof(Button));
            card.transform.SetParent(parent, false);
            var img = card.GetComponent<Image>();
            img.color = CardColor;
            var button = card.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = CardColor;
            colors.highlightedColor = CardPressed;
            colors.pressedColor = CardPressed;
            colors.selectedColor = CardColor;
            button.colors = colors;
            string scene = entry.sceneName;
            button.onClick.AddListener(() => SceneFlow.LoadMap(scene));

            var preview = MakeImage("Preview", card.transform, Color.white);
            SetRect(preview.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(6f, -6f), new Vector2(-6f, -150f));
            if (entry.preview != null)
            {
                preview.sprite = Sprite.Create(entry.preview,
                    new Rect(0, 0, entry.preview.width, entry.preview.height), new Vector2(0.5f, 0.5f));
                preview.preserveAspect = false;
            }
            else
            {
                preview.color = new Color32(50, 55, 70, 255);
            }

            var titleText = MakeText("Title", card.transform, string.IsNullOrEmpty(entry.title) ? entry.mapName : entry.title,
                20, TextAnchor.MiddleLeft, TextColor, FontStyle.Bold);
            SetRect(titleText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(10f, 30f), new Vector2(-10f, 58f));
            string meta = string.IsNullOrEmpty(entry.author) ? entry.mapName : entry.author;
            if (entry.gametypes != null && entry.gametypes.Length > 0)
                meta += "  ·  " + string.Join(" ", entry.gametypes).ToUpperInvariant();
            var metaText = MakeText("Meta", card.transform, meta, 12, TextAnchor.MiddleLeft, SubText, FontStyle.Normal);
            SetRect(metaText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(10f, 6f), new Vector2(-10f, 30f));
        }

        // ------------------------------------------------------------ helpers

        Image MakeImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text MakeText(string name, Transform parent, string value, int size, TextAnchor anchor, Color color, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.fontStyle = style;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        GameObject MakeButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = CardColor;
            go.GetComponent<Button>().onClick.AddListener(onClick);
            var text = MakeText("Label", go.transform, label, 18, TextAnchor.MiddleCenter, TextColor, FontStyle.Bold);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            return go;
        }

        static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = min;
            rt.offsetMax = max;
        }

        static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static void ApplySafeArea(RectTransform rt)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            Rect safe = Screen.safeArea;
            rt.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rt.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
