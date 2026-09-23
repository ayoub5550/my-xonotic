using System.Collections.Generic;
using MyXonotic.Gameplay;
using UnityEngine;

namespace MyXonotic
{
    /// <summary>
    /// Development-slice arena entry point. The scene serializes only this component;
    /// everything else (geometry, lights, spawns, pickups, player, bots, HUD) is built
    /// procedurally in Start(). Original primitive arena, independent mechanics
    /// "inspired by" fast arena FPS games — this is explicitly a DEVELOPMENT FIXTURE,
    /// not a faithful reproduction of any existing title.
    ///
    /// Public API for test drivers / parent Editor tooling:
    ///  - ArenaBootstrap.Instance                static, set after Start() runs.
    ///  - ArenaBootstrap.TestMode                static bool; set true BEFORE Start()
    ///                                           runs to disable interactive bot AI.
    ///  - ArenaBootstrap.IsPaused                static bool; toggled by P key.
    ///  - ArenaBootstrap.IsReady                 static bool; true once bootstrap finished.
    ///  - Instance.PlayerObject / PlayerActor / PlayerComponent / PlayerWeapons
    ///  - Instance.Bots (read-only list), Instance.Pickups (read-only list)
    ///  - Instance.SpawnCount, Instance.UsedImportedArena, Instance.UsedFallbackSpawnMarker
    ///  - Instance.PlaceAtSpawn(Transform) -> yaw : repositions to a random spawn.
    ///  - Instance.Restart() : full in-place reset (frags/health/positions/pickups).
    /// </summary>
    public sealed class ArenaBootstrap : MonoBehaviour
    {
        public static ArenaBootstrap Instance { get; private set; }

        /// Set true (e.g. by a test driver) before Start() runs to keep bots
        /// grounded but non-interactive (no wandering/attacking) for deterministic tests.
        public static bool TestMode;

        public static bool IsPaused { get; private set; }
        public static bool IsReady { get; private set; }

        public GameObject PlayerObject { get; private set; }
        public Actor PlayerActor { get; private set; }
        public Player PlayerComponent { get; private set; }
        public WeaponController PlayerWeapons { get; private set; }
        public MatchSession Match { get; private set; }
        public int FragLimit = 20;
        public float TimeLimitSeconds = 600f;
        public bool MatchFinished => Match != null && Match.IsOver;

        /// Mode this arena was started with (from MatchSettings).
        public GameMode Mode { get; private set; }
        public bool IsTeamMode => Mode != GameMode.Deathmatch;
        /// Movers attached to imported func_* submodels (doors, rotators, bobbing platforms).
        public int MoverCount { get; private set; }
        /// CTF flags in play (2 in CTF mode on maps with flag stands, else 0).
        public IReadOnlyList<CtfFlag> Flags => _flags;
        readonly List<CtfFlag> _flags = new List<CtfFlag>();

        /// Sum of frags of every actor on <paramref name="team"/>.
        public int TeamFrags(Team team)
        {
            int n = 0;
            foreach (var a in GameState.Actors) if (a != null && a.Team == team) n += a.Frags;
            return n;
        }

        /// Team score for the current mode (TDM: frags, CTF: captures).
        public int TeamScore(Team team) => Mode == GameMode.CaptureTheFlag ? CtfFlag.Captures(team) : TeamFrags(team);

        /// Score limit for the current mode (CTF: capture limit).
        public int ScoreLimit => Mode == GameMode.CaptureTheFlag ? MatchSettings.CaptureLimit : FragLimit;

        public IReadOnlyList<Bot> Bots => _bots;
        public IReadOnlyList<Pickup> Pickups => _pickups;

        public int SpawnCount => _spawns.Count;
        public bool UsedImportedArena { get; private set; }
        public bool UsedFallbackSpawnMarker { get; private set; }
        /// dev.13: spawns moved down onto the floor found below them (no floor within SpawnGroundProbe).
        public int SnappedSpawns { get; private set; }
        /// dev.13: spawns removed because no floor exists below them at all (bot would fall into the void at once).
        public int DroppedSpawns { get; private set; }
        /// A spawn is "grounded" when a non-trigger collider lies within this distance below it.
        public const float SpawnGroundProbe = 2f;
        /// How far down to look for a floor to snap an ungrounded spawn onto.
        public const float SpawnSnapProbe = 40f;

        readonly List<Bot> _bots = new List<Bot>();
        readonly List<Pickup> _pickups = new List<Pickup>();
        readonly List<SpawnRuntime> _spawns = new List<SpawnRuntime>();
        Hud _hud;
        public Hud Hud => _hud;

        struct SpawnRuntime
        {
            public Vector3 Position;
            public float Yaw;
        }

        void Start()
        {
            Instance = this;
            IsReady = false;
            GameState.Reset();
            IsPaused = false;
            Application.targetFrameRate = 60;
            Input.simulateMouseWithTouches = false;

            Mode = TestMode ? GameMode.Deathmatch : MatchSettings.Mode;
            BuildLighting();
            BuildSpawns();
            ValidateSpawns();
            BuildPlayer();
            BuildBots();
            BuildPickups();
            BuildMoversAndFlags();
            _hud = Hud.Build(transform);
            _hud.Player = PlayerActor;
            _hud.Weapons = PlayerWeapons;
            Match = new MatchSession();
            if (Mode == GameMode.TeamDeathmatch) Match.ScoreOf = a => a != null ? TeamFrags(a.Team) : 0;
            else if (Mode == GameMode.CaptureTheFlag) Match.ScoreOf = a => a != null ? CtfFlag.Captures(a.Team) : 0;
            Match.MatchOver += OnMatchOver;
            Match.StartMatch(CurrentMatchConfig(), GameState.Actors);
            CtfFlag.Captured += OnFlagCaptured;

            ComputeVoidKillHeight();
            IsReady = true;
            Instance = this;
            if (Application.isPlaying)
            {
                DevCapture.Ensure();
                RuntimeErrorLog.Note("arena " + gameObject.scene.name + " | mode " + Mode + " | bots " + _bots.Count +
                    " (menu setting " + MatchSettings.BotCount + ") | spawns " + _spawns.Count + " snapped " + SnappedSpawns +
                    " dropped " + DroppedSpawns + " | voidY " + VoidKillY.ToString("0.0") + " | imported " + UsedImportedArena);
            }
        }

        /// <summary>
        /// dev.13: bots invisible on device, hypothesis 3 — a spawn inside a wall
        /// or over nothing drops the bot straight into the void, where VoidKillY
        /// kills and respawns it, so it never appears. Every imported spawn must
        /// have a floor within <see cref="SpawnGroundProbe"/>; otherwise it is
        /// snapped onto the first floor below (up to <see cref="SpawnSnapProbe"/>)
        /// or dropped. Counts are reported by DevCapture and the Editor test
        /// <c>BotSpawnGeometry</c> checks the same rule for every packaged map.
        /// </summary>
        void ValidateSpawns()
        {
            SnappedSpawns = 0;
            DroppedSpawns = 0;
            if (!UsedImportedArena || UsedFallbackSpawnMarker) return;
            Physics.SyncTransforms();
            for (int i = _spawns.Count - 1; i >= 0; i--)
            {
                var spawn = _spawns[i];
                if (SpawnHasFloor(spawn.Position, SpawnGroundProbe, out _)) continue;
                RaycastHit hit;
                if (SpawnHasFloor(spawn.Position, SpawnSnapProbe, out hit))
                {
                    spawn.Position = hit.point + Vector3.up * 0.1f;
                    _spawns[i] = spawn;
                    SnappedSpawns++;
                    continue;
                }
                _spawns.RemoveAt(i);
                DroppedSpawns++;
            }
            if (SnappedSpawns > 0 || DroppedSpawns > 0)
                Debug.LogWarning("MyXonotic.ArenaBootstrap: spawn validation snapped " + SnappedSpawns + " and dropped " + DroppedSpawns + " spawn(s).");
            if (_spawns.Count == 0)
            {
                UsedFallbackSpawnMarker = true;
                _spawns.Add(new SpawnRuntime { Position = Vector3.up, Yaw = 0f });
                CreateFallbackMarker(Vector3.zero);
            }
        }

        /// Shared with the Editor spawn test: is there a non-trigger collider within
        /// <paramref name="maxDistance"/> below <paramref name="position"/>?
        public static bool SpawnHasFloor(Vector3 position, float maxDistance, out RaycastHit hit)
        {
            return Physics.Raycast(position + Vector3.up * 0.3f, Vector3.down, out hit, maxDistance + 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Y below which an actor is dead (fell out of the map). dev.12: the first
        /// device video showed the player falling under the map for many seconds
        /// with the old fixed -200 m threshold. Now = lowest collider in the arena
        /// minus a margin, so every map kills within about a second of free fall.
        /// </summary>
        public static float VoidKillY { get; private set; } = -200f;
        public const float VoidKillMargin = 12f;

        void ComputeVoidKillHeight()
        {
            float minY = float.PositiveInfinity;
            foreach (var col in FindObjectsOfType<Collider>())
            {
                if (col.isTrigger) continue;
                float y = col.bounds.min.y;
                if (y < minY) minY = y;
            }
            VoidKillY = float.IsInfinity(minY) ? -200f : minY - VoidKillMargin;
        }

        void Update()
        {
            if (Match != null) Match.AdvanceTime(Time.deltaTime, IsPaused);
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape)) SetPaused(!IsPaused);
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            if (Input.GetKeyDown(KeyCode.M) && IsPaused) ReturnToMenu();
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began) continue;
                if (TouchLayout.Pause.Contains(touch.position)) SetPaused(!IsPaused);
                else if (IsPaused && TouchLayout.Restart.Contains(touch.position)) Restart();
                else if (IsPaused && TouchLayout.MainMenu.Contains(touch.position)) ReturnToMenu();
                else if (IsPaused && TouchLayout.ShareLog.Contains(touch.position)) DevCapture.Share();
                else if (IsPaused && TouchLayout.CopyLog.Contains(touch.position)) DevCapture.Copy();
            }
            if (Input.touchCount == 0 && Input.GetMouseButtonDown(0) &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                if (TouchLayout.Pause.Contains(Input.mousePosition)) SetPaused(!IsPaused);
                else if (IsPaused && TouchLayout.Restart.Contains(Input.mousePosition)) Restart();
                else if (IsPaused && TouchLayout.MainMenu.Contains(Input.mousePosition)) ReturnToMenu();
                else if (IsPaused && TouchLayout.ShareLog.Contains(Input.mousePosition)) DevCapture.Share();
                else if (IsPaused && TouchLayout.CopyLog.Contains(Input.mousePosition)) DevCapture.Copy();
            }
        }

        /// Leaves the match for the map menu when one is packaged; otherwise a no-op.
        public void ReturnToMenu()
        {
            if (!MyXonotic.Menu.SceneFlow.HasMainMenu()) return;
            MyXonotic.Menu.SceneFlow.LoadMainMenu();
        }

        public void SetPaused(bool paused)
        {
            if (!paused && MatchFinished) return;
            IsPaused = paused;
            if (Match != null) Match.Rules.SetPaused(paused);
            if (PlayerComponent != null) PlayerComponent.ResetInputState();
            Cursor.lockState = paused || TestMode || Application.isMobilePlatform
                ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = paused || TestMode || Application.isMobilePlatform;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (Match != null)
            {
                Match.MatchOver -= OnMatchOver;
                Match.StopListening();
            }
            CtfFlag.Captured -= OnFlagCaptured;
            CtfFlag.ResetScores();
            Instance = null;
            IsReady = false;
            IsPaused = false;
            GameState.Reset();
            ArenaMaterials.Release();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && !Application.isBatchMode && IsReady) SetPaused(true);
        }

        // ---------------------------------------------------------------- geometry

        void BuildLighting()
        {
            var lightGO = new GameObject("SunLight");
            lightGO.transform.SetParent(transform, false);
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.98f, 0.9f);
            RenderSettings.ambientLight = new Color(0.25f, 0.27f, 0.3f);
        }

        void BuildPracticeGeometry()
        {
            var arenaRoot = new GameObject("PracticeArena");
            arenaRoot.transform.SetParent(transform, false);

            const float half = 20f;
            const float wallHeight = 6f;

            CreateBox(arenaRoot.transform, "Floor", new Vector3(0f, -0.5f, 0f), new Vector3(half * 2f, 1f, half * 2f),
                new Color(0.35f, 0.35f, 0.38f));

            CreateBox(arenaRoot.transform, "Wall_North", new Vector3(0f, wallHeight * 0.5f, half), new Vector3(half * 2f, wallHeight, 1f),
                new Color(0.5f, 0.3f, 0.3f));
            CreateBox(arenaRoot.transform, "Wall_South", new Vector3(0f, wallHeight * 0.5f, -half), new Vector3(half * 2f, wallHeight, 1f),
                new Color(0.3f, 0.5f, 0.3f));
            CreateBox(arenaRoot.transform, "Wall_East", new Vector3(half, wallHeight * 0.5f, 0f), new Vector3(1f, wallHeight, half * 2f),
                new Color(0.3f, 0.3f, 0.5f));
            CreateBox(arenaRoot.transform, "Wall_West", new Vector3(-half, wallHeight * 0.5f, 0f), new Vector3(1f, wallHeight, half * 2f),
                new Color(0.5f, 0.5f, 0.3f));

            CreateBox(arenaRoot.transform, "Platform_A", new Vector3(8f, 1f, 8f), new Vector3(6f, 2f, 6f), new Color(0.55f, 0.4f, 0.2f));
            CreateBox(arenaRoot.transform, "Platform_B", new Vector3(-8f, 1f, -8f), new Vector3(6f, 2f, 6f), new Color(0.2f, 0.4f, 0.55f));
            CreateBox(arenaRoot.transform, "Pillar_Center", new Vector3(0f, 1.5f, 0f), new Vector3(2f, 3f, 2f), new Color(0.6f, 0.2f, 0.6f));
        }

        static void CreateBox(Transform parent, string name, Vector3 position, Vector3 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.CubeMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(color);
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
        }

        // ---------------------------------------------------------------- spawns

        void BuildSpawns()
        {
            UsedImportedArena = ContentBridge.HasImportedArena();

            if (UsedImportedArena)
            {
                var found = ContentBridge.FindBspSpawnPoints();
                if (found.Count > 0)
                {
                    foreach (var s in found) _spawns.Add(new SpawnRuntime { Position = s.Position, Yaw = s.Yaw });
                    return;
                }

                // Imported arena exists but produced no spawn points: fall back to a
                // single origin spawn and drop a visible marker so this is obvious
                // in-scene rather than silently spawning inside geometry.
                UsedFallbackSpawnMarker = true;
                _spawns.Add(new SpawnRuntime { Position = Vector3.up, Yaw = 0f });
                CreateFallbackMarker(Vector3.zero);
                return;
            }

            // No imported content at all: build our own original practice arena and a
            // ring of fallback spawn markers around it.
            BuildPracticeGeometry();

            var positions = new[]
            {
                new Vector3(15f, 0.1f, 0f),
                new Vector3(-15f, 0.1f, 0f),
                new Vector3(0f, 0.1f, 15f),
                new Vector3(0f, 0.1f, -15f),
                new Vector3(-10f, 0.1f, 10f),
                new Vector3(10f, 0.1f, -10f),
            };
            var arenaRoot = transform;
            for (int i = 0; i < positions.Length; i++)
            {
                float yaw = Quaternion.LookRotation(-new Vector3(positions[i].x, 0, positions[i].z)).eulerAngles.y;
                var markerGO = new GameObject($"SpawnPoint_{i}");
                markerGO.transform.SetParent(arenaRoot, false);
                markerGO.transform.position = positions[i];
                var marker = markerGO.AddComponent<SpawnMarker>();
                marker.Yaw = yaw;
                _spawns.Add(new SpawnRuntime { Position = positions[i], Yaw = yaw });
            }
        }

        void CreateFallbackMarker(Vector3 position)
        {
            var go = new GameObject("MissingSpawnMarker");
            go.transform.SetParent(transform, false);
            go.transform.position = position + Vector3.up * 1.5f;
            go.transform.localScale = new Vector3(0.4f, 3f, 0.4f);
            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.CylinderMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(new Color(1f, 0f, 1f)); // bright magenta: "spawn missing" debug marker
            Debug.LogWarning("MyXonotic.ArenaBootstrap: ImportedArena present but no BspSpawnPoint found; using fallback origin spawn.");
        }

        /// Repositions the given transform to a random known spawn point and returns
        /// the spawn's facing yaw (degrees). Uses FindObjectsOfType-free cached list
        /// built once in Start(), per the "only after Start" rule for content lookups.
        public float PlaceAtSpawn(Transform t)
        {
            if (_spawns.Count == 0)
            {
                t.position = Vector3.up;
                return 0f;
            }
            // Prefer a spawn far from live actors to reduce overlapping respawns.
            var spawn = _spawns[0];
            float best = -1f;
            int offset = TestMode ? 0 : Random.Range(0, _spawns.Count);
            for (int i = 0; i < _spawns.Count; i++)
            {
                var candidate = _spawns[(i + offset) % _spawns.Count];
                float nearest = 100000f;
                foreach (var actor in GameState.Actors)
                    if (actor != null && !actor.IsDead && actor.transform != t)
                        nearest = Mathf.Min(nearest, (actor.transform.position - candidate.Position).sqrMagnitude);
                if (nearest <= best) continue;
                best = nearest;
                spawn = candidate;
            }
            var controller = t.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            t.position = spawn.Position;
            t.rotation = Quaternion.Euler(0f, spawn.Yaw, 0f);
            if (controller != null) controller.enabled = true;
            var player = t.GetComponent<Player>();
            if (player != null) player.ResetForRespawn(spawn.Yaw);
            var bot = t.GetComponent<Bot>();
            if (bot != null) bot.ResetMotion();
            return spawn.Yaw;
        }

        // ---------------------------------------------------------------- actors

        void BuildPlayer()
        {
            var go = new GameObject("Player");
            go.transform.SetParent(transform, false);

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 31f / 32f;

            var body = go.AddComponent<MeshFilter>();
            body.mesh = ArenaPrimitives.CapsuleMesh;
            var bodyRenderer = go.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = ArenaMaterials.Get(new Color(0.2f, 0.8f, 0.9f));
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            var actor = go.AddComponent<Actor>();
            actor.DisplayName = "Player";
            actor.Arena = this;
            actor.Team = IsTeamMode ? Team.Red : Team.None;

            var weapons = go.AddComponent<WeaponController>();
            weapons.Owner = actor;

            var cameraGO = new GameObject("PlayerCamera");
            cameraGO.transform.SetParent(go.transform, false);
            cameraGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = cameraGO.AddComponent<Camera>();
            cameraGO.tag = "MainCamera";
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 85f;
            cameraGO.AddComponent<AudioListener>();

            var player = go.AddComponent<Player>();
            player.ViewCamera = cam;
            player.Weapons = weapons;
            var hook = go.AddComponent<GrapplingHook>();
            hook.Owner = player;
            weapons.Hook = hook;
            if (UsedImportedArena)
            {
                var viewObject = new GameObject("OriginalWeaponView");
                viewObject.transform.SetParent(cameraGO.transform, false);
                weapons.View = viewObject.AddComponent<WeaponView>();
                weapons.View.Controller = weapons;
            }

            float yaw = PlaceAtSpawn(go.transform);
            player.SetViewYaw(yaw);

            PlayerObject = go;
            PlayerActor = actor;
            PlayerComponent = player;
            PlayerWeapons = weapons;

            GameState.Register(actor);
        }

        void BuildBots()
        {
            int count = TestMode ? 3 : MatchSettings.BotCount;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Bot_{i}");
                go.transform.SetParent(transform, false);

                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.4f;
                cc.center = new Vector3(0f, 0.9f, 0f);

                var visual = new GameObject("Body");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = Vector3.up * 0.9f;
                visual.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                var body = visual.AddComponent<MeshFilter>();
                body.mesh = ArenaPrimitives.CapsuleMesh;
                var bodyRenderer = visual.AddComponent<MeshRenderer>();
                bodyRenderer.sharedMaterial = ArenaMaterials.Get(new Color(0.9f, 0.35f, 0.2f));
                // Original Xonotic character (static idle pose) when imported;
                // the capsule stays as the fallback and is hidden otherwise.
                // Teams: the player is Red; bots fill Blue first so the sides stay balanced.
                Team team = Team.None;
                if (IsTeamMode) team = (i % 2 == 0) ? Team.Blue : Team.Red;

                string characterName = CharacterModels.PickForIndex(i);
                GameObject characterBody;
                CharacterAnimator animator = null;
                if (characterName != null && CharacterModels.TryAttach(go.transform, characterName, out characterBody, out animator))
                {
                    // Actor.Respawn re-enables every child renderer, so remove
                    // the capsule outright rather than disabling it.
                    Destroy(visual);
                    if (team != Team.None) TintTeam(characterBody, team);
                }
                else if (team != Team.None)
                {
                    bodyRenderer.sharedMaterial = ArenaMaterials.Get(MatchSettings.TeamColor(team));
                }

                var actor = go.AddComponent<Actor>();
                actor.DisplayName = IsTeamMode ? (team == Team.Red ? "Red" : "Blue") + $"_Bot_{i}" : $"Bot_{i}";
                actor.Arena = this;
                actor.Team = team;

                var weapons = go.AddComponent<WeaponController>();
                weapons.Owner = actor;

                var bot = go.AddComponent<Bot>();
                bot.Weapons = weapons;
                bot.Target = PlayerObject != null ? PlayerObject.transform : null;
                bot.Animator = animator;
                bot.AimErrorDegrees = 2.5f + i * 1.5f;
                if (!TestMode)
                {
                    // One random extra weapon per bot at start; the rest comes from map pickups.
                    var extra = (WeaponType)Random.Range((int)WeaponType.MachineGun, WeaponController.WeaponCount);
                    weapons.GiveWeapon(extra);
                }

                PlaceAtSpawn(go.transform);

                _bots.Add(bot);
                GameState.Register(actor);
            }
        }

        /// Multiplies the character's materials by the team colour (material instances; assets untouched).
        static void TintTeam(GameObject body, Team team)
        {
            Color tint = Color.Lerp(Color.white, MatchSettings.TeamColor(team), 0.55f);
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials; // instances
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_Tint")) m.SetColor("_Tint", tint);
                    else if (m.HasProperty("_Color")) m.color = tint;
                }
                r.materials = mats;
            }
        }

        // ---------------------------------------------------------------- movers + flags

        void BuildMoversAndFlags()
        {
            if (!UsedImportedArena) return;
            foreach (var arena in FindObjectsOfType<MyXonotic.Content.ImportedArena>())
            {
                MoverCount += Mover.Attach(arena.transform);
                foreach (var stand in arena.GetComponentsInChildren<MyXonotic.Content.CtfFlagBase>(true))
                {
                    if (Mode != GameMode.CaptureTheFlag) { stand.gameObject.SetActive(false); continue; }
                    var team = stand.team == 1 ? Team.Red : Team.Blue;
                    var visual = stand.transform.childCount > 0 ? stand.transform.GetChild(0) : null;
                    if (visual != null) TintTeam(visual.gameObject, team);
                    var flag = stand.gameObject.AddComponent<CtfFlag>();
                    flag.Init(team, stand.transform.position, visual);
                    _flags.Add(flag);
                }
            }
            if (Mode == GameMode.CaptureTheFlag && _flags.Count < 2)
                Debug.LogWarning("MyXonotic.ArenaBootstrap: CTF selected but this map has " + _flags.Count + " flag stand(s); captures are impossible here.");
        }

        void OnFlagCaptured(CtfFlag flag, Actor scorer)
        {
            if (Match != null) Match.ReportScore(scorer);
        }

        // ---------------------------------------------------------------- pickups

        void BuildPickups()
        {
            if (UsedImportedArena)
            {
                // Imported by Editor from real entity positions; no Editor API in player.
                foreach (var arena in FindObjectsOfType<MyXonotic.Content.ImportedArena>())
                    _pickups.AddRange(arena.GetComponentsInChildren<Pickup>(true));
                return;
            }
            AddPickup(new Vector3(15f, 1f, 5f), PickupType.Health, 25, new Color(0.2f, 0.9f, 0.3f));
            AddPickup(new Vector3(-15f, 1f, -5f), PickupType.Armor, 25, new Color(0.3f, 0.5f, 0.9f));
            AddPickup(new Vector3(5f, 1f, -15f), PickupType.AmmoBullets, 40, new Color(0.9f, 0.9f, 0.2f));
            AddPickup(new Vector3(-5f, 1f, 15f), PickupType.AmmoRockets, 15, new Color(0.9f, 0.3f, 0.2f));
            AddPickup(new Vector3(0f, 1f, 5f), PickupType.Health, 50, new Color(0.1f, 1f, 0.5f));
            AddPickup(new Vector3(8f, 2.5f, 8f), PickupType.Weapon, 0, WeaponController.GetDef(WeaponType.MachineGun).Tint, WeaponType.MachineGun);
            AddPickup(new Vector3(-8f, 2.5f, -8f), PickupType.Weapon, 0, WeaponController.GetDef(WeaponType.Devastator).Tint, WeaponType.Devastator);
            AddPickup(new Vector3(12f, 1f, -12f), PickupType.Weapon, 0, WeaponController.GetDef(WeaponType.Vortex).Tint, WeaponType.Vortex);
            AddPickup(new Vector3(-12f, 1f, 12f), PickupType.AmmoCells, 25, new Color(0.3f, 0.6f, 1f));
        }

        void AddPickup(Vector3 position, PickupType type, int amount, Color color, WeaponType weapon = WeaponType.Shotgun)
        {
            var go = new GameObject($"Pickup_{type}");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.6f;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = ArenaPrimitives.SphereMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ArenaMaterials.Get(color);
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var pickup = go.AddComponent<Pickup>();
            pickup.Type = type;
            pickup.Amount = amount;
            pickup.Weapon = weapon;

            _pickups.Add(pickup);
        }

        // ---------------------------------------------------------------- restart

        /// Full in-place reset: respawns every actor, restores frags/health/armor to
        /// defaults, and reactivates every pickup. Does not reload the scene.
        public void Restart()
        {
            foreach (var actor in GameState.Actors)
            {
                actor.ResetScore();
                actor.ForceRespawn();
            }
            foreach (var pickup in _pickups)
            {
                pickup.ForceActivate();
            }
            foreach (var projectile in FindObjectsOfType<Projectile>()) Destroy(projectile.gameObject);
            CtfFlag.ResetScores();
            foreach (var flag in _flags) if (flag != null) flag.ResetToBase();
            if (Match != null) Match.Restart(CurrentMatchConfig(), GameState.Actors);
            SetPaused(false);
        }

        MatchConfig CurrentMatchConfig() => new MatchConfig
        {
            FragLimit = ScoreLimit, TimeLimitSeconds = TimeLimitSeconds
        };

        void OnMatchOver(MatchResult<Actor> result)
        {
            SetPaused(true);
        }
    }
}
