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

        public IReadOnlyList<Bot> Bots => _bots;
        public IReadOnlyList<Pickup> Pickups => _pickups;

        public int SpawnCount => _spawns.Count;
        public bool UsedImportedArena { get; private set; }
        public bool UsedFallbackSpawnMarker { get; private set; }

        readonly List<Bot> _bots = new List<Bot>();
        readonly List<Pickup> _pickups = new List<Pickup>();
        readonly List<SpawnRuntime> _spawns = new List<SpawnRuntime>();
        Hud _hud;

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

            BuildLighting();
            BuildSpawns();
            BuildPlayer();
            BuildBots();
            BuildPickups();
            _hud = Hud.Build(transform);
            _hud.Player = PlayerActor;
            _hud.Weapons = PlayerWeapons;
            Match = new MatchSession();
            Match.MatchOver += OnMatchOver;
            Match.StartMatch(CurrentMatchConfig(), GameState.Actors);

            IsReady = true;
            Instance = this;
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
            }
            if (Input.touchCount == 0 && Input.GetMouseButtonDown(0) &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                if (TouchLayout.Pause.Contains(Input.mousePosition)) SetPaused(!IsPaused);
                else if (IsPaused && TouchLayout.Restart.Contains(Input.mousePosition)) Restart();
                else if (IsPaused && TouchLayout.MainMenu.Contains(Input.mousePosition)) ReturnToMenu();
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
            for (int i = 0; i < 3; i++)
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
                string characterName = CharacterModels.PickForIndex(i);
                GameObject characterBody;
                if (characterName != null && CharacterModels.TryAttach(go.transform, characterName, out characterBody))
                {
                    // Actor.Respawn re-enables every child renderer, so remove
                    // the capsule outright rather than disabling it.
                    Destroy(visual);
                }

                var actor = go.AddComponent<Actor>();
                actor.DisplayName = $"Bot_{i}";
                actor.Arena = this;

                var weapons = go.AddComponent<WeaponController>();
                weapons.Owner = actor;

                var bot = go.AddComponent<Bot>();
                bot.Weapons = weapons;
                bot.Target = PlayerObject != null ? PlayerObject.transform : null;

                PlaceAtSpawn(go.transform);

                _bots.Add(bot);
                GameState.Register(actor);
            }
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
            AddPickup(new Vector3(5f, 1f, -15f), PickupType.AmmoRifle, 20, new Color(0.9f, 0.9f, 0.2f));
            AddPickup(new Vector3(-5f, 1f, 15f), PickupType.AmmoRocket, 5, new Color(0.9f, 0.3f, 0.2f));
            AddPickup(new Vector3(0f, 1f, 5f), PickupType.Health, 50, new Color(0.1f, 1f, 0.5f));
        }

        void AddPickup(Vector3 position, PickupType type, int amount, Color color)
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
            if (Match != null) Match.Restart(CurrentMatchConfig(), GameState.Actors);
            SetPaused(false);
        }

        MatchConfig CurrentMatchConfig() => new MatchConfig
        {
            FragLimit = FragLimit, TimeLimitSeconds = TimeLimitSeconds
        };

        void OnMatchOver(MatchResult<Actor> result)
        {
            SetPaused(true);
        }
    }
}
