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
    /// builds one runtime <see cref="Pickup"/> per SUPPORTED item_* point
    /// entity (health/armor/rocket-ammo) at its real map position.
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
    ///   item_rockets                  -> AmmoRocket (5, provisional -- see below);
    ///                                     our Rocket weapon genuinely fires rockets.
    ///   item_bullets   -> UNSUPPORTED. Explicit instruction: do not map an
    ///                       ammo pickup onto a weapon it does not actually
    ///                       belong to. Our Rifle is an original prototype,
    ///                       not a stand-in for the machinegun that
    ///                       item_bullets refills in Xonotic.
    ///   item_cells     -> UNSUPPORTED. No supported weapon uses cell ammo.
    ///   item_strength  -> UNSUPPORTED. Power-up; Actor/WeaponController have
    ///                       no such stat.
    ///   weapon_*       -> UNSUPPORTED, all of them. WeaponController has no
    ///                       pickup/unlock mechanic (every actor always has
    ///                       Blaster+Rifle+Rocket), and none of our 3
    ///                       prototypes claims to BE any specific original
    ///                       weapon. Never remapped onto one.
    /// item_health_big and item_armor_medium are supported for completeness
    /// (a future map may use them) even though Boil's own entities lump has
    /// neither -- Boil's supported-entity count is therefore unchanged at 25.
    ///
    /// Health/Armor amounts are PUBLIC NUMERIC CONFIG FACTS, not copied
    /// GPL/QuakeC code: read directly from the "set g_pickup_&lt;name&gt; &lt;n&gt;"
    /// cvar-default lines in balance-xonotic.cfg, a plain-text data/config
    /// file (no compiled logic) inside the official
    /// xonotic-20230620-data.pk3 (sha256
    /// 7602be0d44a4f1ce4f0918c54ceb215c9bc963a103623d6c7874f081330c8505;
    /// balance-xonotic.cfg itself sha256
    /// e5544d2dbd3873693d28ba21473f3bed13e50dcd6b68006829da462055e5f839):
    ///   g_pickup_healthsmall 5, g_pickup_healthmedium 25,
    ///   g_pickup_healthbig 50, g_pickup_healthmega 100,
    ///   g_pickup_armorsmall 5, g_pickup_armormedium 25,
    ///   g_pickup_armorbig 50, g_pickup_armormega 100.
    /// (Reading these numeric defaults is fine; copying the QC code that
    /// applies/clamps them is not, and none of that code was read.)
    ///
    /// AmmoRocket stays PROVISIONAL at 5, NOT the config's
    /// g_pickup_rockets/g_pickup_rockets_weapon value (40, max 160): our
    /// WeaponController.Rocket().MaxAmmo is only 20 (an independent,
    /// unrelated tuning choice already in this repo), so reusing the
    /// official 40 would over-fill or exceed that cap on a single pickup.
    /// Revisiting this needs a WeaponController change, out of scope here.
    /// Respawn timers also have public cvars (g_pickup_respawntime_short 15,
    /// _medium 20, _long 30, _ammo 10) but which tier applies to which item
    /// class is not itself a plain 1:1 name match the way the amount cvars
    /// are, so Pickup.RespawnTime is left at its existing default here --
    /// still explicitly provisional, not wired to a tier.
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
            public PickupPlan(PickupType type, int amount) { Type = type; Amount = amount; }
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
            ["item_rockets"] = new PickupPlan(PickupType.AmmoRocket, 5),
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
                warnings.Add("BspPickupImporter: no supported item_health_*/item_armor_*/item_rockets entities were found.");
            }

            return warnings;
        }

        static string UnsupportedReason(string classname)
        {
            if (classname.StartsWith("weapon_", StringComparison.Ordinal))
            {
                return "weapon pickups are out of scope for this slice (WeaponController has no weapon-unlock mechanic; every actor always has Blaster+Rifle+Rocket) -- never remapped onto one of the 3 supported WeaponTypes.";
            }
            if (classname == "item_bullets")
            {
                return "our Rifle is an original prototype, not the machinegun item_bullets refills in Xonotic -- not mapped to avoid claiming an unsupported ammo/weapon correspondence.";
            }
            if (classname == "item_cells")
            {
                return "no supported weapon uses a cell-type ammo pool.";
            }
            if (classname == "item_strength")
            {
                return "power-up items are out of scope; Actor/WeaponController expose no such stat.";
            }
            return "unrecognized item_* class outside the mapped health/armor/rocket-ammo subset.";
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
                case PickupType.AmmoRifle: return new Color(0.9f, 0.9f, 0.2f);
                case PickupType.AmmoRocket: return new Color(0.9f, 0.3f, 0.2f);
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
            Expect(ClassToPlan.TryGetValue("item_rockets", out PickupPlan ro) && ro.Type == PickupType.AmmoRocket && ro.Amount == 5,
                "item_rockets maps to AmmoRocket/5 (provisional, NOT the config's 40 -- our Rocket MaxAmmo is only 20)");

            // item_bullets must stay unmapped (explicit parent instruction: no
            // unsupported ammo->weapon correspondence).
            string[] mustStayUnsupported =
            {
                "item_bullets", "item_cells", "item_strength",
                "weapon_vortex", "weapon_mortar", "weapon_machinegun",
                "weapon_hagar", "weapon_electro", "weapon_devastator", "weapon_crylink",
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
