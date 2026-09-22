using System;
using System.Collections.Generic;
using System.Globalization;
using MyXonotic.Content.Bsp;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Additive gameplay pass over an already-parsed BspDocument, parallel to
    /// <see cref="BspGameplayImporter"/>'s trigger_push/teleport/hurt pass:
    /// builds one runtime <see cref="Pickup"/> per SUPPORTED item_*/weapon_*
    /// point entity (health/armor/ammo/weapons) at its real map position.
    ///
    /// Verified against Boil's actual entities lump (data only, key/value
    /// text; no QuakeC/engine source read):
    ///   item_health_small x7, item_health_medium x3, item_health_mega x1
    ///   item_armor_small x10, item_armor_big x1, item_armor_mega x1
    ///   item_rockets x2, item_bullets x2, item_cells x2, item_strength x1
    ///   weapon_machinegun/vortex/mortar/crylink/devastator/electro/hagar x1 each
    /// All are plain point entities: "classname" + "origin" only.
    ///
    /// SUPPORTED / UNSUPPORTED (see <see cref="ClassToPlan"/>, the single
    /// authoritative table -- tests read it directly, they do not duplicate it):
    ///   item_health_small/medium/big/mega -> Health   (5 / 25 / 50 / 100)
    ///   item_armor_small/medium/big/mega  -> Armor    (5 / 25 / 50 / 100)
    ///   item_shells/bullets/rockets/cells -> shared ammo pools (15 / 80 / 25 / 25)
    ///   weapon_* for the nine core weapons -> Weapon pickup (grants the weapon
    ///                       plus WeaponDef.PickupAmmo of its ammo type).
    ///   item_strength/invincible/jetpack/fuel, exotic weapon_* (arc, minelayer,
    ///                       rifle, seeker, ...) -> UNSUPPORTED, visual-only decoration.
    ///
    /// Amounts are PUBLIC NUMERIC CONFIG FACTS, not copied GPL/QuakeC code:
    /// read from the "set g_pickup_<name> <n>" cvar-default lines in
    /// balance-xonotic.cfg (sha256
    /// e5544d2dbd3873693d28ba21473f3bed13e50dcd6b68006829da462055e5f839).
    /// Respawn timers follow the same file's tiers: weapons/ammo 15 s,
    /// health 20 s, armor 30 s (approximate mapping of short/medium/long).
    ///
    /// Visual: an explicit DEVELOPMENT PLACEHOLDER sphere (ArenaPrimitives +
    /// an ArenaMaterials debug tint), matching ArenaBootstrap.AddPickup's
    /// synthetic pickups -- never an original-art claim. ArenaMaterials.Get
    /// returns a transient runtime Material, not an AssetDatabase-persisted
    /// asset; generated-asset persistence across a scene reload (matching how
    /// BspImportPipeline persists its meshes/materials) is left to whoever
    /// integrates this importer. The official data pack also contains real
    /// item meshes (e.g. models/items/g_h25.md3, item_armor_small.md3,
    /// a_rockets.md3) usable with the existing Md3WeaponModelBuilder API for
    /// a future original-model import; not wired up here.
    ///
    /// Wiring into BspImportPipeline.Import(path) / ArenaBootstrap is left to
    /// whoever owns those files (not touched here); the expected call shape
    /// mirrors BspGameplayImporter:
    ///
    ///   var arenaRoot = BspImportPipeline.Import(path);                 // existing
    ///   var pickups = new List&lt;Pickup&gt;();
    ///   var pickupWarnings = BspPickupImporter.Import(doc, arenaRoot.transform, pickups);
    ///   // then: _pickups.AddRange(pickups) so ArenaBootstrap.Restart() resets them too.
    /// </summary>
    public static class BspPickupImporter
    {
        private const float MaxSaneEntityCoordinate = 1_000_000f;

        // Defensive ceiling against a pathological entities lump, mirroring
        // BspGameplayImporter.MaxTriggerEntities.
        private const int MaxPickupEntities = 4096;

        internal readonly struct PickupPlan
        {
            public readonly PickupType Type;
            public readonly int Amount;
            public readonly WeaponType Weapon;
            public PickupPlan(PickupType type, int amount) { Type = type; Amount = amount; Weapon = WeaponType.Blaster; }
            public PickupPlan(WeaponType weapon) { Type = PickupType.Weapon; Amount = 0; Weapon = weapon; }
        }

        /// <summary>Authoritative supported classname -> (PickupType, Amount) table. See class doc.</summary>
        internal static readonly Dictionary<string, PickupPlan> ClassToPlan = new Dictionary<string, PickupPlan>(StringComparer.Ordinal)
        {
            ["item_health_small"] = new PickupPlan(PickupType.Health, 5),
            ["item_health_medium"] = new PickupPlan(PickupType.Health, 25),
            ["item_health_big"] = new PickupPlan(PickupType.Health, 50),
            ["item_health_mega"] = new PickupPlan(PickupType.Health, 100),
            ["item_armor_small"] = new PickupPlan(PickupType.Armor, 5),
            ["item_armor_medium"] = new PickupPlan(PickupType.Armor, 25),
            ["item_armor_big"] = new PickupPlan(PickupType.Armor, 50),
            ["item_armor_mega"] = new PickupPlan(PickupType.Armor, 100),
            // Ammo amounts: g_pickup_shells/nails/rockets/cells defaults (15/80/25/25).
            ["item_strength"] = new PickupPlan(PickupType.Strength, 30),
            ["item_invincible"] = new PickupPlan(PickupType.Shield, 30),
            ["item_shield"] = new PickupPlan(PickupType.Shield, 30),
            ["item_shells"] = new PickupPlan(PickupType.AmmoShells, 15),
            ["item_bullets"] = new PickupPlan(PickupType.AmmoBullets, 80),
            ["item_rockets"] = new PickupPlan(PickupType.AmmoRockets, 25),
            ["item_cells"] = new PickupPlan(PickupType.AmmoCells, 25),
            // The nine core weapons (current and legacy entity class names).
            ["weapon_blaster"] = new PickupPlan(WeaponType.Blaster),
            ["weapon_laser"] = new PickupPlan(WeaponType.Blaster),
            ["weapon_shotgun"] = new PickupPlan(WeaponType.Shotgun),
            ["weapon_machinegun"] = new PickupPlan(WeaponType.MachineGun),
            ["weapon_uzi"] = new PickupPlan(WeaponType.MachineGun),
            ["weapon_mortar"] = new PickupPlan(WeaponType.Mortar),
            ["weapon_grenadelauncher"] = new PickupPlan(WeaponType.Mortar),
            ["weapon_electro"] = new PickupPlan(WeaponType.Electro),
            ["weapon_crylink"] = new PickupPlan(WeaponType.Crylink),
            ["weapon_vortex"] = new PickupPlan(WeaponType.Vortex),
            ["weapon_nex"] = new PickupPlan(WeaponType.Vortex),
            ["weapon_hagar"] = new PickupPlan(WeaponType.Hagar),
            ["weapon_devastator"] = new PickupPlan(WeaponType.Devastator),
            ["weapon_rocketlauncher"] = new PickupPlan(WeaponType.Devastator),
            ["weapon_rifle"] = new PickupPlan(WeaponType.Rifle),
            ["weapon_campingrifle"] = new PickupPlan(WeaponType.Rifle),
            ["weapon_sniperrifle"] = new PickupPlan(WeaponType.Rifle),
            ["weapon_minelayer"] = new PickupPlan(WeaponType.Minelayer),
            ["weapon_arc"] = new PickupPlan(WeaponType.Arc),
            ["weapon_fireball"] = new PickupPlan(WeaponType.Fireball),
            ["weapon_hook"] = new PickupPlan(WeaponType.Hook),
        };

        /// <summary>
        /// Builds Pickup GameObjects for every supported item_* entity in
        /// <paramref name="doc"/> under <paramref name="root"/>, appending each
        /// created Pickup component to <paramref name="createdPickups"/>. Never
        /// throws on malformed per-entity data; such entities are skipped and
        /// recorded in the returned warnings list. Only null call arguments raise.
        /// </summary>
        public static List<string> Import(BspDocument doc, Transform root, List<Pickup> createdPickups)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (createdPickups == null) throw new ArgumentNullException(nameof(createdPickups));

            var warnings = new List<string>();
            var unsupported = new SortedSet<string>(StringComparer.Ordinal);
            var resolver = new XonoticContentResolver();
            var decorations = new GameObject("PickupDecorations");
            decorations.transform.SetParent(root, false);

            int considered = 0;
            int importedCount = 0;
            foreach (var entity in doc.Entities)
            {
                if (considered >= MaxPickupEntities)
                {
                    warnings.Add(string.Format(
                        "Entities lump has more than {0} entries; stopped scanning for item_*/weapon_* pickup entities to bound import work.",
                        MaxPickupEntities));
                    break;
                }

                string classname = entity.Get("classname");
                if (string.IsNullOrEmpty(classname)) continue;
                if (!classname.StartsWith("item_", StringComparison.Ordinal) &&
                    !classname.StartsWith("weapon_", StringComparison.Ordinal))
                {
                    continue;
                }
                considered++;

                if (classname == "item_flag_team1" || classname == "item_flag_team2")
                {
                    PlaceFlagBase(entity, classname == "item_flag_team1" ? 1 : 2, root, resolver, warnings);
                    continue;
                }
                if (!ClassToPlan.TryGetValue(classname, out PickupPlan plan))
                {
                    unsupported.Add(classname);
                    PlaceDecorationOnly(entity, classname, resolver, decorations, warnings);
                    continue;
                }

                string originStr = entity.Get("origin");
                if (originStr == null || !TryParseVec3(originStr, out BspVec3 quakeOrigin))
                {
                    warnings.Add(string.Format(
                        "Entity '{0}' has no parsable 'origin' key; pickup skipped.", classname));
                    continue;
                }
                if (!IsFiniteAndBounded(quakeOrigin))
                {
                    warnings.Add(string.Format(
                        "Entity '{0}' has a non-finite or out-of-range origin; pickup skipped.", classname));
                    continue;
                }

                var unityOrigin = BspCoordinateSpace.QuakeToUnity(quakeOrigin);
                var position = new Vector3(unityOrigin.X, unityOrigin.Y, unityOrigin.Z);

                var go = new GameObject(classname + "_" + importedCount);
                go.transform.SetParent(root, false);
                go.transform.localPosition = position;
                go.transform.localScale = Vector3.one * 0.6f;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                bool originalArt = ApplyOriginalModel(go, mf, mr, classname, resolver, warnings);
                if (!originalArt)
                {
                    mf.mesh = ArenaPrimitives.SphereMesh;
                    mr.sharedMaterial = ArenaMaterials.Get(DevelopmentColorFor(plan.Type));
                }
                var col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                if (originalArt) col.radius = 0.9f;
                // Kinematic Rigidbody for reliable OnTrigger callbacks, same
                // reasoning as BspGameplayImporter's trigger volumes.
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;

                var pickup = go.AddComponent<Pickup>();
                pickup.Type = plan.Type;
                pickup.Amount = plan.Amount;
                pickup.Weapon = plan.Weapon;
                // Xonotic defaults: weapons/ammo respawn after 15 s, health/armor after 20/30 s.
                pickup.RespawnTime = plan.Type == PickupType.Weapon ? 15f
                    : plan.Type == PickupType.Health ? 20f
                    : plan.Type == PickupType.Armor ? 30f
                    : plan.Type == PickupType.Strength || plan.Type == PickupType.Shield ? 120f : 15f;

                createdPickups.Add(pickup);
                importedCount++;

                warnings.Add(string.Format(
                    "Entity '{0}': mapped to a {1} pickup (amount {2}, provisional) at its real map position; visual is {3}.",
                    classname, plan.Type, plan.Amount,
                    originalArt ? "the original MD3 item model (static frame 0)" : "a development placeholder sphere, not original item art"));
            }

            foreach (var c in unsupported)
            {
                warnings.Add(string.Format(
                    "Entity class '{0}' present in map but not instantiated as a pickup: {1}",
                    c, UnsupportedReason(c)));
            }

            if (importedCount == 0)
            {
                warnings.Add("BspPickupImporter: no supported item_*/weapon_* entities were found.");
            }

            return warnings;
        }

        /// <summary>
        /// CTF flag stand: a <see cref="MyXonotic.Content.CtfFlagBase"/> marker with the
        /// original flags.md3 (static frame 0) as its visual. Runtime attaches the
        /// CtfFlag behaviour only in CTF mode. Not a Pickup, not a decoration.
        /// </summary>
        static void PlaceFlagBase(BspEntity entity, int team, Transform root, XonoticContentResolver resolver, List<string> warnings)
        {
            string originStr = entity.Get("origin");
            if (originStr == null || !TryParseVec3(originStr, out BspVec3 quakeOrigin) || !IsFiniteAndBounded(quakeOrigin))
            {
                warnings.Add("item_flag_team" + team + ": no parsable origin; flag stand skipped.");
                return;
            }
            var u = BspCoordinateSpace.QuakeToUnity(quakeOrigin);
            var go = new GameObject("item_flag_team" + team);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(u.X, u.Y, u.Z);
            var marker = go.AddComponent<MyXonotic.Content.CtfFlagBase>();
            marker.team = team;
            float yaw;
            if (float.TryParse(entity.Get("angle", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out yaw)) marker.yaw = yaw;

            var visual = new GameObject("FlagVisual");
            visual.transform.SetParent(go.transform, false);
            Mesh mesh; Material[] mats; string error;
            if (BspMapModelImporter.TryGetModel("models/ctf/flags.md3", resolver, out mesh, out mats, out error))
            {
                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterials = mats;
                warnings.Add("item_flag_team" + team + ": flag stand placed with original models/ctf/flags.md3 (static frame 0).");
            }
            else
            {
                visual.transform.localScale = new Vector3(0.12f, 2.2f, 0.12f);
                visual.transform.localPosition = Vector3.up * 1.1f;
                visual.AddComponent<MeshFilter>().sharedMesh = ArenaPrimitives.CylinderMesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = ArenaMaterials.Get(team == 1 ? new Color(1f, 0.25f, 0.2f) : new Color(0.25f, 0.5f, 1f));
                warnings.Add("item_flag_team" + team + ": flags.md3 unavailable (" + error + "); placeholder pole used.");
            }
        }

        static string UnsupportedReason(string classname)
        {
            if (classname.StartsWith("weapon_", StringComparison.Ordinal))
            {
                return "weapon outside the fourteen weapons implemented by WeaponController (visual-only decoration).";
            }
            if (classname == "item_fuel" || classname == "item_fuel_regen" || classname == "item_jetpack")
            {
                return "jetpack/fuel items are out of scope (no jetpack mechanic).";
            }
            return "unrecognized item_* class outside the mapped health/armor/ammo/weapon subset.";
        }

        /// <summary>
        /// Original Xonotic item/weapon world models by entity class. Names
        /// come from the official data pack's models/items and models/weapons
        /// folders; no model is substituted when the mapped file is absent.
        /// </summary>
        public static readonly Dictionary<string, string> OriginalModelByClass =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["item_health_small"] = "models/items/g_h1.md3",
                ["item_health_medium"] = "models/items/g_h25.md3",
                ["item_health_big"] = "models/items/g_h50.md3",
                ["item_health_large"] = "models/items/g_h50.md3",
                ["item_health_mega"] = "models/items/g_h100.md3",
                ["item_armor_small"] = "models/items/item_armor_small.md3",
                ["item_armor_medium"] = "models/items/item_armor_medium.md3",
                ["item_armor_big"] = "models/items/item_armor_big.md3",
                ["item_armor_large"] = "models/items/item_armor_large.md3",
                ["item_armor_mega"] = "models/items/item_armor_large.md3",
                ["item_rockets"] = "models/items/a_rockets.md3",
                ["item_bullets"] = "models/items/a_bullets.md3",
                ["item_cells"] = "models/items/a_cells.md3",
                ["item_plasma"] = "models/items/a_cells.md3",
                ["item_shells"] = "models/items/a_shells.md3",
                ["item_fuel"] = "models/items/g_fuel.md3",
                ["item_fuel_regen"] = "models/items/g_fuelregen.md3",
                ["item_strength"] = "models/items/g_strength.md3",
                ["item_invincible"] = "models/items/g_invincible.md3",
                ["item_shield"] = "models/items/g_invincible.md3",
                ["item_jetpack"] = "models/items/g_jetpack.md3",
                ["weapon_blaster"] = "models/weapons/g_laser.md3",
                ["weapon_laser"] = "models/weapons/g_laser.md3",
                ["weapon_shotgun"] = "models/weapons/g_shotgun.md3",
                ["weapon_machinegun"] = "models/weapons/g_uzi.md3",
                ["weapon_uzi"] = "models/weapons/g_uzi.md3",
                ["weapon_mortar"] = "models/weapons/g_gl.md3",
                ["weapon_grenadelauncher"] = "models/weapons/g_gl.md3",
                ["weapon_electro"] = "models/weapons/g_electro.md3",
                ["weapon_crylink"] = "models/weapons/g_crylink.md3",
                ["weapon_vortex"] = "models/weapons/g_nex.md3",
                ["weapon_nex"] = "models/weapons/g_nex.md3",
                ["weapon_hagar"] = "models/weapons/g_hagar.md3",
                ["weapon_devastator"] = "models/weapons/g_rl.md3",
                ["weapon_rocketlauncher"] = "models/weapons/g_rl.md3",
                ["weapon_arc"] = "models/weapons/g_arc.md3",
                ["weapon_minelayer"] = "models/weapons/g_minelayer.md3",
                ["weapon_rifle"] = "models/weapons/g_campingrifle.md3",
                ["weapon_campingrifle"] = "models/weapons/g_campingrifle.md3",
                ["weapon_seeker"] = "models/weapons/g_seeker.md3",
                ["weapon_fireball"] = "models/weapons/g_fireball.md3",
                ["weapon_hlac"] = "models/weapons/g_hlac.md3",
                ["weapon_hook"] = "models/weapons/g_hookgun.md3",
                ["weapon_porto"] = "models/weapons/g_porto.md3",
                ["weapon_tuba"] = "models/weapons/g_tuba.md3",
                ["weapon_vaporizer"] = "models/weapons/g_minstanex.md3",
                ["weapon_minstanex"] = "models/weapons/g_minstanex.md3",
            };

        static bool ApplyOriginalModel(GameObject go, MeshFilter mf, MeshRenderer mr, string classname,
            XonoticContentResolver resolver, List<string> warnings)
        {
            string modelPath;
            if (!OriginalModelByClass.TryGetValue(classname, out modelPath)) return false;
            Mesh mesh; Material[] mats; string error;
            if (!BspMapModelImporter.TryGetModel(modelPath, resolver, out mesh, out mats, out error))
            {
                warnings.Add(string.Format("Entity '{0}': original model '{1}' unavailable ({2}); placeholder used.", classname, modelPath, error));
                return false;
            }
            mf.sharedMesh = mesh;
            mr.sharedMaterials = mats;
            go.transform.localScale = Vector3.one;
            return true;
        }

        /// <summary>
        /// For item/weapon classes that have no gameplay yet: place the
        /// original model as a visual-only, non-collectible decoration so the
        /// map does not look emptier than the original. Explicitly reported.
        /// </summary>
        static void PlaceDecorationOnly(BspEntity entity, string classname,
            XonoticContentResolver resolver, GameObject decorations, List<string> warnings)
        {
            string modelPath;
            if (!OriginalModelByClass.TryGetValue(classname, out modelPath)) return;
            string originStr = entity.Get("origin");
            BspVec3 quakeOrigin;
            if (originStr == null || !TryParseVec3(originStr, out quakeOrigin) || !IsFiniteAndBounded(quakeOrigin)) return;
            Mesh mesh; Material[] mats; string error;
            if (!BspMapModelImporter.TryGetModel(modelPath, resolver, out mesh, out mats, out error))
            {
                warnings.Add(string.Format("Entity '{0}': decoration model '{1}' unavailable ({2}).", classname, modelPath, error));
                return;
            }
            var u = BspCoordinateSpace.QuakeToUnity(quakeOrigin);
            var go = new GameObject(classname + "_visualonly");
            go.transform.SetParent(decorations.transform, false);
            go.transform.localPosition = new Vector3(u.X, u.Y, u.Z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.AddComponent<PickupDecoration>();
        }

        static Color DevelopmentColorFor(PickupType type)
        {
            switch (type)
            {
                case PickupType.Health: return new Color(0.2f, 0.9f, 0.3f);
                case PickupType.Armor: return new Color(0.3f, 0.5f, 0.9f);
                case PickupType.AmmoBullets: return new Color(0.9f, 0.9f, 0.2f);
                case PickupType.AmmoRockets: return new Color(0.9f, 0.3f, 0.2f);
                case PickupType.AmmoCells: return new Color(0.3f, 0.6f, 1f);
                case PickupType.AmmoShells: return new Color(0.85f, 0.8f, 0.6f);
                case PickupType.Weapon: return new Color(1f, 0.8f, 0.3f);
                default: return Color.white;
            }
        }

        internal static bool TryParseVec3(string s, out BspVec3 v)
        {
            v = default;
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            if (!IsFiniteAndBounded(x) || !IsFiniteAndBounded(y) || !IsFiniteAndBounded(z)) return false;
            v = new BspVec3(x, y, z);
            return true;
        }

        static bool IsFiniteAndBounded(BspVec3 v) =>
            IsFiniteAndBounded(v.X) && IsFiniteAndBounded(v.Y) && IsFiniteAndBounded(v.Z);

        static bool IsFiniteAndBounded(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            return v >= -MaxSaneEntityCoordinate && v <= MaxSaneEntityCoordinate;
        }

        /// <summary>
        /// Dependency-free self-checks for this importer's pure/static logic
        /// (classname mapping, coordinate parsing). Not wired into
        /// Editor/LocalTests.cs (not owned by this change) -- run manually, or
        /// have LocalTests.Run() call it like BspGameplayImporter.RunSelfTests().
        /// Throws on the first failed assertion.
        /// </summary>
        public static void RunSelfTests()
        {
            // Amounts here are the public g_pickup_* cvar defaults from
            // balance-xonotic.cfg (see class doc for the file's sha256) --
            // numeric config facts, not copied code.
            Expect(ClassToPlan.TryGetValue("item_health_small", out PickupPlan hs) && hs.Type == PickupType.Health && hs.Amount == 5,
                "item_health_small maps to Health/5 (g_pickup_healthsmall)");
            Expect(ClassToPlan.TryGetValue("item_health_medium", out PickupPlan hm) && hm.Type == PickupType.Health && hm.Amount == 25,
                "item_health_medium maps to Health/25 (g_pickup_healthmedium)");
            Expect(ClassToPlan.TryGetValue("item_health_big", out PickupPlan hb) && hb.Type == PickupType.Health && hb.Amount == 50,
                "item_health_big maps to Health/50 (g_pickup_healthbig)");
            Expect(ClassToPlan.TryGetValue("item_health_mega", out PickupPlan hg) && hg.Type == PickupType.Health && hg.Amount == 100,
                "item_health_mega maps to Health/100 (g_pickup_healthmega)");
            Expect(ClassToPlan.TryGetValue("item_armor_small", out PickupPlan aS) && aS.Type == PickupType.Armor && aS.Amount == 5,
                "item_armor_small maps to Armor/5 (g_pickup_armorsmall)");
            Expect(ClassToPlan.TryGetValue("item_armor_medium", out PickupPlan aM) && aM.Type == PickupType.Armor && aM.Amount == 25,
                "item_armor_medium maps to Armor/25 (g_pickup_armormedium)");
            Expect(ClassToPlan.TryGetValue("item_armor_big", out PickupPlan ab) && ab.Type == PickupType.Armor && ab.Amount == 50,
                "item_armor_big maps to Armor/50 (g_pickup_armorbig)");
            Expect(ClassToPlan.TryGetValue("item_armor_mega", out PickupPlan am) && am.Type == PickupType.Armor && am.Amount == 100,
                "item_armor_mega maps to Armor/100 (g_pickup_armormega)");
            Expect(ClassToPlan.TryGetValue("item_rockets", out PickupPlan ro) && ro.Type == PickupType.AmmoRockets && ro.Amount == 25,
                "item_rockets maps to AmmoRockets/25 (g_pickup_rockets)");
            Expect(ClassToPlan.TryGetValue("item_bullets", out PickupPlan bu) && bu.Type == PickupType.AmmoBullets && bu.Amount == 80,
                "item_bullets maps to AmmoBullets/80 (g_pickup_nails)");
            Expect(ClassToPlan.TryGetValue("item_cells", out PickupPlan ce) && ce.Type == PickupType.AmmoCells && ce.Amount == 25,
                "item_cells maps to AmmoCells/25 (g_pickup_cells)");
            Expect(ClassToPlan.TryGetValue("item_shells", out PickupPlan sh) && sh.Type == PickupType.AmmoShells && sh.Amount == 15,
                "item_shells maps to AmmoShells/15 (g_pickup_shells)");
            Expect(ClassToPlan.TryGetValue("weapon_vortex", out PickupPlan wv) && wv.Type == PickupType.Weapon && wv.Weapon == WeaponType.Vortex,
                "weapon_vortex maps to the Vortex weapon pickup");
            Expect(ClassToPlan.TryGetValue("weapon_devastator", out PickupPlan wd) && wd.Type == PickupType.Weapon && wd.Weapon == WeaponType.Devastator,
                "weapon_devastator maps to the Devastator weapon pickup");
            int weaponClasses = 0;
            foreach (var kv in ClassToPlan) if (kv.Value.Type == PickupType.Weapon) weaponClasses++;
            Expect(weaponClasses >= WeaponController.WeaponCount, "every one of the fourteen weapons has at least one entity classname");

            // dev.9: power-ups and the five extra weapons are live pickups now.
            Expect(ClassToPlan.TryGetValue("item_strength", out PickupPlan st) && st.Type == PickupType.Strength && st.Amount == 30,
                "item_strength maps to a 30 s Strength pickup");
            Expect(ClassToPlan.TryGetValue("item_invincible", out PickupPlan sd) && sd.Type == PickupType.Shield,
                "item_invincible maps to the Shield pickup");
            Expect(ClassToPlan.TryGetValue("weapon_minelayer", out PickupPlan wm) && wm.Weapon == WeaponType.Minelayer &&
                   ClassToPlan.TryGetValue("weapon_arc", out PickupPlan wa) && wa.Weapon == WeaponType.Arc &&
                   ClassToPlan.TryGetValue("weapon_rifle", out PickupPlan wr) && wr.Weapon == WeaponType.Rifle &&
                   ClassToPlan.TryGetValue("weapon_fireball", out PickupPlan wf) && wf.Weapon == WeaponType.Fireball &&
                   ClassToPlan.TryGetValue("weapon_hook", out PickupPlan wh) && wh.Weapon == WeaponType.Hook,
                "the five extra weapons have entity classnames");
            // Jetpack/fuel and the remaining exotic weapons stay visual-only.
            string[] mustStayUnsupported =
            {
                "item_jetpack", "item_fuel",
                "weapon_seeker", "weapon_hlac", "weapon_porto", "weapon_tuba", "weapon_vaporizer",
            };
            foreach (var c in mustStayUnsupported)
            {
                Expect(!ClassToPlan.ContainsKey(c), "unsupported class '" + c + "' is not in ClassToPlan");
                Expect(!string.IsNullOrEmpty(UnsupportedReason(c)), "unsupported class '" + c + "' has a reported reason");
            }

            if (!TryParseVec3("1004.799988 0.000000 -300.800018", out BspVec3 parsed))
                throw new Exception("[BspPickupImporter.RunSelfTests] FAIL: TryParseVec3 real Boil origin string.");
            Expect(Math.Abs(parsed.X - 1004.799988f) < 0.01f, "TryParseVec3 parses X");
            Expect(Math.Abs(parsed.Z - (-300.800018f)) < 0.01f, "TryParseVec3 parses Z");
            Expect(!TryParseVec3("1 2", out _), "TryParseVec3 rejects wrong arity");
            Expect(!TryParseVec3("1 2 notanumber", out _), "TryParseVec3 rejects unparsable component");
            Expect(!IsFiniteAndBounded(new BspVec3(float.NaN, 0, 0)), "IsFiniteAndBounded rejects NaN");
            Expect(!IsFiniteAndBounded(new BspVec3(MaxSaneEntityCoordinate * 2f, 0, 0)), "IsFiniteAndBounded rejects out-of-range");

            Debug.Log("[BspPickupImporter] RunSelfTests PASS");
        }

        static void Expect(bool condition, string name)
        {
            if (!condition) throw new Exception("[BspPickupImporter.RunSelfTests] FAIL: " + name);
        }
    }
}
