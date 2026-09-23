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
        static readonly Color StickBaseColor = new Color32(255, 255, 255, 64); // rgba(255,255,255,0.25)
        static readonly Color StickKnobColor = new Color32(255, 230, 179, 128);// rgba(255,230,179,0.5)
        static readonly Color LabelColor = new Color32(255, 217, 153, 255);    // rgb(255,217,153)
        static readonly Color LabelShadowColor = new Color32(0, 0, 0, 230);    // rgba(0,0,0,0.9)
        static readonly Color PauseColor = new Color32(64, 46, 31, 235);       // rgba(64,46,31,0.92) "menu"

        Text _statusText;
        Text _bannerText;
        // dev.13 Xonotic-style bottom panel (luma icons + big numbers)
        Text _healthText, _armorText, _ammoCountText, _weaponNameText, _devText;
        Image _healthIcon, _armorIcon, _ammoIcon, _bottomPanel;
        Text _pauseDevText;
        Text _pauseText;
        Text _notifyText;
        Text _crosshairText;
        Image _damageFlash;
        GameObject _pausePanel;
        RectTransform _safeAreaRoot;

        Texture2D _discTexture;
        Texture2D _ringTexture;
        Texture2D _squareTexture;
        GUIStyle _buttonLabelStyle;
        GUIStyle _slotStyle;

        readonly System.Collections.Generic.List<string> _notifications = new System.Collections.Generic.List<string>();
        float _notifyTimer;
        float _damageFlashAlpha;
        float _hitMarkerTimer;
        int _lastHealth = -1;
        Actor _subscribedPlayer;

        static readonly Color HealthColor = new Color32(120, 230, 120, 255);
        static readonly Color ArmorColor = new Color32(120, 170, 255, 255);
        static readonly Color AmmoColor = new Color32(255, 210, 120, 255);
        static readonly Color SlotOwnedColor = new Color32(40, 40, 48, 190);
        static readonly Color SlotEmptyColor = new Color32(20, 20, 24, 90);
        static readonly Color SlotCurrentColor = new Color32(255, 200, 80, 230);

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

            // Top-left, small: the weapon bar owns the top centre, PAUSE the top right.
            _bannerText = CreateText("Banner", _safeAreaRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -8f), new Vector2(420f, 24f), 14, TextAnchor.UpperLeft, new Color(1f, 0.9f, 0.4f, 0.8f));
            _bannerText.text = "my-xonotic " + Application.version + " — development build";

            // dev.13: score/timer line moved under the banner (top-left) so the
            // bottom centre is free for the Xonotic-style health/armor/ammo panel.
            _statusText = CreateText("Status", _safeAreaRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -30f), new Vector2(560f, 66f), 18, TextAnchor.UpperLeft, Color.white);
            _statusText.supportRichText = true;
            _statusText.fontStyle = FontStyle.Bold;
            _statusText.verticalOverflow = VerticalWrapMode.Overflow;

            // DevCapture status (FPS, bots) under the score line — development builds only.
            _devText = CreateText("DevStatus", _safeAreaRoot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -98f), new Vector2(560f, 20f), 12, TextAnchor.UpperLeft, new Color(1f, 0.9f, 0.4f, 0.7f));

            BuildBottomPanel();


            _notifyText = CreateText("Notify", _safeAreaRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 110f), new Vector2(900f, 120f), 22, TextAnchor.LowerCenter, Color.white);
            _notifyText.supportRichText = true;

            _crosshairText = CreateText("Crosshair", _safeAreaRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(40, 40), 28, TextAnchor.MiddleCenter, Color.white);
            _crosshairText.text = "+";

            var flashGO = new GameObject("DamageFlash", typeof(RectTransform), typeof(Image));
            flashGO.transform.SetParent(_safeAreaRoot, false);
            _damageFlash = flashGO.GetComponent<Image>();
            _damageFlash.color = new Color(0.8f, 0f, 0f, 0f);
            _damageFlash.raycastTarget = false;
            var frt = flashGO.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;

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
                Vector2.zero, new Vector2(900f, 220f), 28, TextAnchor.MiddleCenter, Color.white);
            _pauseText.supportRichText = true;
            _pauseText.verticalOverflow = VerticalWrapMode.Overflow;
            _pauseText.text = "PAUSED\nTouch RESUME, RESTART or MAIN MENU\nDesktop: P / R / M";
            // dev.13 DevCapture panel: last log lines, bottom half of the pause screen.
            _pauseDevText = CreateText("PauseDevText", _pausePanel.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-440f, 12f), new Vector2(880f, 150f), 12, TextAnchor.LowerLeft, new Color(1f, 0.9f, 0.6f, 0.9f));
            _pauseDevText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _pauseDevText.verticalOverflow = VerticalWrapMode.Truncate;
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
            // dev.12: pivot follows the anchor so anchoredPosition is measured from
            // the screen edge the text hugs. With the default centre pivot the
            // bottom texts extended 40 px below the screen (FRAGS/DEATHS and the
            // weapon name were cut off on the first device video).
            rt.pivot = anchorMin;
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

        /// <summary>
        /// dev.13 Xonotic luma layout (hud_luma.cfg: healtharmor panel at
        /// 0.30..0.70 x 0.925, ammo panel just above it): a dark translucent
        /// panel bottom-centre with [health icon][number] [armor icon][number]
        /// and above it [ammo icon][number] + weapon name. Icons come from the
        /// original gfx/hud/luma art via HudArt; without the art the numbers
        /// stand alone. All rects are anchor-pivoted (dev.12 rule) so nothing
        /// can leave the screen; HudGeometry asserts it.
        /// </summary>
        void BuildBottomPanel()
        {
            const float panelW = 480f, panelH = 62f, ammoH = 44f, icon = 44f;
            var panelGO = new GameObject("BottomPanel", typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(_safeAreaRoot, false);
            _bottomPanel = panelGO.GetComponent<Image>();
            _bottomPanel.color = new Color(0f, 0.14f, 0.25f, 0.55f); // hud_panel_bg_color "0 0.14 0.25"
            _bottomPanel.raycastTarget = false;
            var prt = panelGO.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 10f);
            prt.sizeDelta = new Vector2(panelW, panelH);

            // Health: icon + number in the left half, right-aligned towards the centre.
            _healthIcon = CreateIcon("HealthIcon", prt, "health", new Vector2(14f, (panelH - icon) * 0.5f), icon);
            _healthText = CreatePanelText("Health", prt, new Vector2(14f + icon + 6f, 0f), new Vector2(panelW * 0.5f - icon - 30f, panelH), 40, TextAnchor.MiddleLeft, HealthColor);
            // Armor: mirrored on the right half.
            _armorIcon = CreateIcon("ArmorIcon", prt, "armor", new Vector2(panelW * 0.5f + 14f, (panelH - icon) * 0.5f), icon);
            _armorText = CreatePanelText("Armor", prt, new Vector2(panelW * 0.5f + 14f + icon + 6f, 0f), new Vector2(panelW * 0.5f - icon - 30f, panelH), 40, TextAnchor.MiddleLeft, ArmorColor);

            var ammoGO = new GameObject("AmmoPanel", typeof(RectTransform), typeof(Image));
            ammoGO.transform.SetParent(_safeAreaRoot, false);
            var ammoImg = ammoGO.GetComponent<Image>();
            ammoImg.color = new Color(0f, 0.14f, 0.25f, 0.45f);
            ammoImg.raycastTarget = false;
            var art = ammoGO.GetComponent<RectTransform>();
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0f);
            art.pivot = new Vector2(0.5f, 0f);
            art.anchoredPosition = new Vector2(0f, 10f + panelH + 4f);
            art.sizeDelta = new Vector2(panelW * 0.7f, ammoH);
            float aw = panelW * 0.7f;
            _ammoIcon = CreateIcon("AmmoIcon", art, null, new Vector2(12f, (ammoH - 32f) * 0.5f), 32f);
            _ammoCountText = CreatePanelText("AmmoCount", art, new Vector2(12f + 32f + 6f, 0f), new Vector2(110f, ammoH), 30, TextAnchor.MiddleLeft, AmmoColor);
            _weaponNameText = CreatePanelText("Ammo", art, new Vector2(12f + 32f + 6f + 110f, 0f), new Vector2(aw - (12f + 32f + 6f + 110f) - 10f, ammoH), 18, TextAnchor.MiddleRight, Color.white);
            _weaponNameText.supportRichText = true;
        }

        Image CreateIcon(string name, RectTransform parent, string art, Vector2 bottomLeft, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = bottomLeft;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            SetIcon(img, art);
            return img;
        }

        static void SetIcon(Image img, string art)
        {
            var sprite = HudArt.Sprite(art);
            img.sprite = sprite;
            // No art generated: hide the slot instead of drawing a white square.
            img.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        Text CreatePanelText(string name, RectTransform parent, Vector2 bottomLeft, Vector2 size, int fontSize, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = bottomLeft;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
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

        /// <summary>Queues a short centre-screen message (pickups, frags, weapon changes).</summary>
        public void Notify(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            _notifications.Add(message);
            if (_notifications.Count > 3) _notifications.RemoveAt(0);
            _notifyTimer = 2.6f;
            if (_notifyText != null) { var c = _notifyText.color; c.a = 1f; _notifyText.color = c; }
        }

        void OnEnable()
        {
            Pickup.AnyCollected += OnPickupCollected;
            GameState.AnyDeath += OnAnyDeath;
            Actor.AnyDamage += OnAnyDamage;
            Actor.PowerupStarted += OnPowerupStarted;
            Actor.PowerupEnded += OnPowerupEnded;
            CtfFlag.Taken += OnFlagTaken;
            CtfFlag.Dropped += OnFlagDropped;
            CtfFlag.Returned += OnFlagReturned;
            CtfFlag.Captured += OnFlagCaptured;
        }

        void OnDisable()
        {
            Pickup.AnyCollected -= OnPickupCollected;
            GameState.AnyDeath -= OnAnyDeath;
            Actor.AnyDamage -= OnAnyDamage;
            Actor.PowerupStarted -= OnPowerupStarted;
            Actor.PowerupEnded -= OnPowerupEnded;
            CtfFlag.Taken -= OnFlagTaken;
            CtfFlag.Dropped -= OnFlagDropped;
            CtfFlag.Returned -= OnFlagReturned;
            CtfFlag.Captured -= OnFlagCaptured;
            if (_subscribedPlayer != null && Weapons != null) Weapons.WeaponAcquired -= OnWeaponAcquired;
        }

        void OnPickupCollected(Pickup pickup, Actor collector)
        {
            if (collector != Player || pickup == null) return;
            Notify("<color=#ffd280>" + pickup.Label + "</color>");
            AudioClip clip = pickup.Type == PickupType.Weapon ? WeaponAudio.Common("weaponpickup")
                : pickup.Type == PickupType.Health && pickup.Amount >= 100 ? WeaponAudio.Misc("megahealth")
                : pickup.Type == PickupType.Health && pickup.Amount >= 25 ? WeaponAudio.Misc("mediumhealth")
                : WeaponAudio.Misc("itempickup");
            WeaponAudio.PlayAt(clip, Vector3.zero, 0.9f, spatial: false);
        }

        void OnPowerupStarted(Actor actor, bool strength)
        {
            if (actor != Player) return;
            WeaponAudio.PlayAt(WeaponAudio.Misc(strength ? "powerup" : "powerup_shield"), Vector3.zero, 0.9f, spatial: false);
        }

        void OnPowerupEnded(Actor actor, bool strength)
        {
            if (actor != Player) return;
            WeaponAudio.PlayAt(WeaponAudio.Misc("poweroff"), Vector3.zero, 0.8f, spatial: false);
        }

        static string TeamName(Team t) => t == Team.Red ? "RED" : t == Team.Blue ? "BLUE" : "";
        static string TeamHex(Team t) => t == Team.Red ? "#ff5a50" : "#5a8cff";

        void OnFlagTaken(CtfFlag flag, Actor actor)
        {
            string who = actor == Player ? "You" : actor != null ? actor.DisplayName : "?";
            Notify("<color=" + TeamHex(flag.Team) + ">" + who + " took the " + TeamName(flag.Team) + " flag</color>");
            WeaponAudio.PlayAt(WeaponAudio.Ctf(flag.Team == Team.Red ? "red_taken" : "blue_taken"), Vector3.zero, 0.9f, spatial: false);
        }

        void OnFlagDropped(CtfFlag flag, Actor actor)
        {
            Notify("<color=" + TeamHex(flag.Team) + ">" + TeamName(flag.Team) + " flag dropped</color>");
            WeaponAudio.PlayAt(WeaponAudio.Ctf(flag.Team == Team.Red ? "red_dropped" : "blue_dropped"), Vector3.zero, 0.9f, spatial: false);
        }

        void OnFlagReturned(CtfFlag flag, Actor actor)
        {
            Notify("<color=" + TeamHex(flag.Team) + ">" + TeamName(flag.Team) + " flag returned</color>");
            WeaponAudio.PlayAt(WeaponAudio.Ctf(flag.Team == Team.Red ? "red_returned" : "blue_returned"), Vector3.zero, 0.9f, spatial: false);
        }

        void OnFlagCaptured(CtfFlag flag, Actor actor)
        {
            Team scorer = actor != null ? actor.Team : Team.None;
            string who = actor == Player ? "You" : actor != null ? actor.DisplayName : "?";
            Notify("<color=" + TeamHex(scorer) + ">" + who + " captured the flag!</color>");
            WeaponAudio.PlayAt(WeaponAudio.Ctf(scorer == Team.Red ? "red_capture" : "blue_capture"), Vector3.zero, 1f, spatial: false);
        }

        void OnWeaponAcquired(WeaponType weapon)
        {
            // Pickup label already covers the name; nothing extra needed here.
        }

        void OnAnyDeath(Actor victim, Actor killer)
        {
            if (victim == null) return;
            if (killer == Player && victim != Player)
            {
                Notify("<color=#ff8c5a>You fragged " + victim.DisplayName + "</color>");
                WeaponAudio.PlayAt(WeaponAudio.Misc("kill"), Vector3.zero, 0.8f, spatial: false);
            }
            else if (victim == Player)
                Notify(killer != null && killer != Player ? "<color=#ff5a5a>" + killer.DisplayName + " fragged you</color>" : "<color=#ff5a5a>You died</color>");
            else if (killer != null && killer != victim)
                Notify(killer.DisplayName + " fragged " + victim.DisplayName);
        }

        void OnAnyDamage(Actor victim, Actor attacker, int damage)
        {
            if (victim == Player && damage > 0) _damageFlashAlpha = Mathf.Min(0.55f, _damageFlashAlpha + damage / 120f);
            if (attacker == Player && victim != Player && damage > 0)
            {
                _hitMarkerTimer = 0.12f;
                WeaponAudio.PlayAt(WeaponAudio.Misc("hit"), Vector3.zero, 0.5f, spatial: false);
            }
        }

        void Update()
        {
            ApplySafeArea();
            if (Player == null || _statusText == null) return;
            if (_subscribedPlayer != Player && Weapons != null)
            {
                _subscribedPlayer = Player;
                Weapons.WeaponAcquired += OnWeaponAcquired;
            }

            float dt = Time.unscaledDeltaTime;
            if (Weapons != null) TouchLayout.VisibleWeaponSlots = Weapons.VisibleSlotCount;
            var def = Weapons != null ? Weapons.CurrentDef : WeaponController.GetDef(WeaponType.Blaster);
            int ammo = Weapons != null ? Weapons.GetAmmo(Weapons.Current) : 0;
            // Bottom panel: Xonotic colours health green -> red when low, armor blue.
            _healthText.text = Player.Health.ToString();
            _healthText.color = Player.Health <= 25 ? new Color(1f, 0.3f, 0.25f) : Player.Health > 100 ? new Color(0.75f, 1f, 0.75f) : HealthColor;
            _armorText.text = Player.Armor.ToString();
            _armorText.color = Player.Armor > 100 ? new Color(0.8f, 0.9f, 1f) : ArmorColor;
            string ammoStr = ammo < 0 ? "∞" : ammo.ToString();
            bool lowAmmo = ammo >= 0 && ammo < Mathf.Max(1, def.Primary.AmmoCost) * 3;
            _ammoCountText.text = ammoStr;
            _ammoCountText.color = lowAmmo ? new Color(1f, 0.35f, 0.35f) : AmmoColor;
            SetIcon(_ammoIcon, HudArt.AmmoIconName(def.Ammo));
            string powerups = "";
            if (Player.HasStrength) powerups += " <color=#ff4aa0>STR " + Mathf.CeilToInt(Player.StrengthRemaining) + "</color>";
            if (Player.HasShield) powerups += " <color=#4ae0ff>SHLD " + Mathf.CeilToInt(Player.ShieldRemaining) + "</color>";
            // dev.15 weapon state readouts (Xonotic shows these on the weapon panel).
            string state = "";
            if (Weapons != null)
            {
                if (Weapons.Current == WeaponType.Vortex) state = $" <color=#8ee6ff>CHG {Mathf.RoundToInt(Weapons.VortexCharge * 100)}%</color>";
                else if (Weapons.Current == WeaponType.Arc) state = Weapons.ArcOverheated ? " <color=#ff5a3c>OVERHEAT</color>" : $" <color=#ffb44a>HEAT {Mathf.RoundToInt(Weapons.ArcHeat * 100)}%</color>";
                else if (Weapons.Current == WeaponType.Hagar && Weapons.HagarLoaded > 0) state = $" <color=#ffd27a>LOAD {Weapons.HagarLoaded}/4</color>";
            }
            _weaponNameText.text = def.Name.ToUpperInvariant() + powerups + state;

            _statusText.text = $"FRAGS {Player.Frags}   DEATHS {Player.Deaths}";
            if (_devText != null) _devText.text = DevCapture.StatusLine();

            if (_notifyTimer > 0f)
            {
                _notifyTimer -= dt;
                _notifyText.text = string.Join("\n", _notifications);
                var c = _notifyText.color; c.a = Mathf.Clamp01(_notifyTimer / 0.6f); _notifyText.color = c;
                if (_notifyTimer <= 0f) _notifications.Clear();
            }
            else _notifyText.text = string.Empty;

            _damageFlashAlpha = Mathf.MoveTowards(_damageFlashAlpha, 0f, dt * 1.4f);
            _damageFlash.color = new Color(0.8f, 0f, 0f, _damageFlashAlpha);

            _hitMarkerTimer -= dt;
            _crosshairText.color = _hitMarkerTimer > 0f ? new Color(1f, 0.3f, 0.2f) : Color.white;
            _crosshairText.text = Weapons != null && Weapons.IsZooming ? "◎" : "+";
            var arena = ArenaBootstrap.Instance;
            if (arena != null && arena.Match != null)
            {
                int seconds = Mathf.FloorToInt(arena.Match.ElapsedSeconds);
                _statusText.text = $"{MatchSettings.ModeShort(arena.Mode)}  {seconds / 60:00}:{seconds % 60:00}  LIMIT {arena.ScoreLimit}\n" + _statusText.text;
                if (arena.IsTeamMode)
                {
                    _statusText.text += $"\n<color=#ff5a50>RED {arena.TeamScore(Team.Red)}</color>   <color=#5a8cff>BLUE {arena.TeamScore(Team.Blue)}</color>";
                    if (arena.Mode == GameMode.CaptureTheFlag)
                    {
                        var mine = CtfFlag.CarriedBy(Player);
                        if (mine != null) _statusText.text += "   <color=#ffd280>YOU HAVE THE FLAG — RETURN TO BASE</color>";
                        else
                        {
                            var enemyFlag = CtfFlag.ForTeam(MatchSettings.Opponent(Player.Team));
                            var ownFlag = CtfFlag.ForTeam(Player.Team);
                            if (ownFlag != null && ownFlag.State == CtfFlag.FlagState.Carried) _statusText.text += "   <color=#ff8c5a>ENEMY HAS YOUR FLAG</color>";
                            else if (ownFlag != null && ownFlag.State == CtfFlag.FlagState.Dropped) _statusText.text += "   <color=#ff8c5a>YOUR FLAG IS DROPPED</color>";
                            if (enemyFlag == null) _statusText.text += "   (no flags on this map)";
                        }
                    }
                }
                if (arena.MatchFinished)
                {
                    var result = arena.Match.Result;
                    string winner = result.IsTie ? "TIE"
                        : arena.IsTeamMode ? TeamName(result.Winner.Team) + " TEAM WINS"
                        : result.Winner.DisplayName + " WINS";
                    _pauseText.text = "MATCH OVER — " + winner + "\n" + result.Reason +
                        "\nTouch RESTART / Desktop R";
                }
                else _pauseText.text = "PAUSED\nTouch RESUME, RESTART or MAIN MENU\nDesktop: P / R / M\n<size=14>" + RuntimeErrorLog.Summary() + "</size>";
            }
            if (_pausePanel != null)
            {
                bool paused = ArenaBootstrap.IsPaused;
                if (paused && !_pausePanel.activeSelf && _pauseDevText != null)
                {
                    // Refresh the DevCapture excerpt once per pause (not every frame).
                    var recent = RuntimeErrorLog.Recent;
                    var sb = new System.Text.StringBuilder();
                    sb.Append("DevCapture: ").Append(DevCapture.StatusLine()).Append('\n');
                    int from = Mathf.Max(0, recent.Count - 7);
                    for (int i = from; i < recent.Count; i++) sb.Append(recent[i].Length > 140 ? recent[i].Substring(0, 140) + "…" : recent[i]).Append('\n');
                    _pauseDevText.text = sb.ToString();
                }
                _pausePanel.SetActive(paused);
            }
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
            _ringTexture = MakeRingTexture(0.93f, 0.985f);
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

        Texture2D SquareTexture()
        {
            if (_squareTexture != null) return _squareTexture;
            _squareTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _squareTexture.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            _squareTexture.Apply();
            return _squareTexture;
        }

        /// Nine weapon slots along the top: number, short name and ammo; owned
        /// slots are solid, the current one is outlined in the accent colour,
        /// unowned ones are faint. Tap handling lives in Player.ReadTouch.
        void DrawWeaponBar()
        {
            if (Weapons == null) return;
            if (_slotStyle == null)
                _slotStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, wordWrap = false };
            float slotSize = TouchLayout.WeaponSlotSize;
            _slotStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(slotSize * 0.22f));
            var prev = GUI.color;
            TouchLayout.VisibleWeaponSlots = Weapons.VisibleSlotCount;
            for (int i = 0; i < TouchLayout.VisibleWeaponSlots; i++)
            {
                var rect = GuiRect(TouchLayout.WeaponSlot(i));
                WeaponType w;
                if (!Weapons.SlotToWeapon(i, out w)) continue;
                bool owned = Weapons.Has(w);
                bool current = Weapons.Current == w;
                var def = WeaponController.GetDef(w);
                if (current)
                {
                    GUI.color = SlotCurrentColor;
                    GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), SquareTexture());
                }
                GUI.color = owned ? SlotOwnedColor : SlotEmptyColor;
                GUI.DrawTexture(rect, SquareTexture());
                if (owned)
                {
                    GUI.color = def.Tint;
                    GUI.DrawTexture(new Rect(rect.x + 3f, rect.yMax - 5f, rect.width - 6f, 3f), SquareTexture());
                }
                int ammo = Weapons.GetAmmo(w);
                string ammoStr = ammo < 0 ? "∞" : ammo.ToString();
                string tint = owned ? (Weapons.CanFire(w) ? "#ffffff" : "#ff7a7a") : "#6a6a72";
                _slotStyle.normal.textColor = Color.white;
                string key = i < WeaponController.CoreWeaponCount ? (i + 1).ToString() : "0";
                // dev.13: original luma weapon icon fills the slot (dimmed when not
                // owned, reddish when out of ammo); text falls back when no art.
                var iconTex = HudArt.Texture(HudArt.WeaponIconName(w));
                if (iconTex != null)
                {
                    GUI.color = owned ? (Weapons.CanFire(w) ? Color.white : new Color(1f, 0.55f, 0.55f, 1f)) : new Color(1f, 1f, 1f, 0.28f);
                    float pad = rect.width * 0.08f;
                    GUI.DrawTexture(new Rect(rect.x + pad, rect.y + pad * 0.5f, rect.width - pad * 2f, rect.height - pad * 2.5f), iconTex, ScaleMode.ScaleToFit, true);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height * 0.4f), "<color=#c8c8d0><size=" + Mathf.Max(8, _slotStyle.fontSize - 3) + ">" + key + "</size></color>", _slotStyle);
                    GUI.Label(new Rect(rect.x, rect.yMax - rect.height * 0.42f, rect.width, rect.height * 0.4f), "<color=" + tint + "><b>" + (owned ? ammoStr : "") + "</b></color>", _slotStyle);
                    continue;
                }
                GUI.color = Color.white;
                GUI.Label(rect, "<color=#8a8a95><size=" + Mathf.Max(8, _slotStyle.fontSize - 3) + ">" + key + "</size></color>\n<color=" + tint + "><b>" + def.ShortName + "</b></color>\n<color=" + tint + ">" + (owned ? ammoStr : "-") + "</color>", _slotStyle);
            }
            GUI.color = prev;
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

        /// dev.14 button skin: disc + rim + glyph + small caption near the bottom.
        void DrawGlyphButton(Rect screenRect, TouchGlyphs.Glyph glyph, string caption, bool held)
        {
            DrawDisc(screenRect, held ? TouchGlyphs.DiscFillHeld : TouchGlyphs.DiscFill);
            DrawRing(screenRect, TouchGlyphs.Rim);
            Rect gui = GuiRect(screenRect);
            float iconSize = screenRect.height * 0.52f;
            var iconRect = new Rect(gui.center.x - iconSize * 0.5f, gui.center.y - iconSize * 0.58f, iconSize, iconSize);
            var tex = TouchGlyphs.Get(glyph);
            var prev = GUI.color;
            float sh = Mathf.Max(1f, screenRect.height * 0.012f);
            GUI.color = TouchGlyphs.IconShadow;
            GUI.DrawTexture(new Rect(iconRect.x + sh, iconRect.y + sh, iconRect.width, iconRect.height), tex, ScaleMode.ScaleToFit, true);
            GUI.color = TouchGlyphs.Icon;
            GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit, true);
            GUI.color = prev;
            if (!string.IsNullOrEmpty(caption))
            {
                if (_captionStyle == null)
                    _captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                _captionStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(screenRect.height * 0.13f));
                var capRect = new Rect(gui.x, gui.y + gui.height * 0.62f, gui.width, gui.height * 0.24f);
                _captionStyle.normal.textColor = LabelShadowColor;
                GUI.Label(new Rect(capRect.x + 1f, capRect.y + 1f, capRect.width, capRect.height), caption, _captionStyle);
                _captionStyle.normal.textColor = TouchGlyphs.Icon;
                GUI.Label(capRect, caption, _captionStyle);
            }
        }
        GUIStyle _captionStyle;

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
                // dev.13 DevCapture: hand the error report to the owner without a cable.
                var smallStyle = new GUIStyle(boxStyle) { fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.02f), 11, 28) };
                GUI.Box(GuiRect(TouchLayout.ShareLog), "SHARE LOG", smallStyle);
                GUI.Box(GuiRect(TouchLayout.CopyLog), "COPY LOG", smallStyle);
                return;
            }

            DrawWeaponBar();

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

            // dev.14: Warzone-Mobile-style skin — dark translucent discs, thin white
            // rim, white pictogram, lit while held. Positions/sizes unchanged.
            bool fireHeld = player != null && player.TouchFireHeld;
            bool altHeld = player != null && player.TouchAltHeld;
            bool jumpHeld = player != null && player.TouchJumpHeld;
            bool plusHeld = player != null && player.TouchWpnPlusHeld;
            bool minusHeld = player != null && player.TouchWpnMinusHeld;
            DrawGlyphButton(TouchLayout.Fire, TouchGlyphs.Glyph.Bullet, "FIRE", fireHeld);
            // Xonotic secondary-fire modes are real gameplay: crosshair glyph (aim/alt).
            DrawGlyphButton(TouchLayout.Alt, TouchGlyphs.Glyph.Crosshair, "ALT", altHeld);
            DrawGlyphButton(TouchLayout.Jump, TouchGlyphs.Glyph.ArrowUp, "JUMP", jumpHeld);
            DrawGlyphButton(TouchLayout.WpnMinus, TouchGlyphs.Glyph.ChevronDown, "WPN", minusHeld);
            DrawGlyphButton(TouchLayout.WpnPlus, TouchGlyphs.Glyph.ChevronUp, "WPN", plusHeld);

            // Right drag = look. Intentionally no visible disc/reticle under the
            // thumb here — only the dynamic joystick (left) and the buttons get a
            // drawn control surface.
        }
    }
}
