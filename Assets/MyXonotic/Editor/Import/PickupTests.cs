using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic;
using MyXonotic.Content.Bsp;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Focused, dependency-light Editor checks for <see cref="Pickup"/> and
    /// <see cref="BspPickupImporter"/> runtime behavior. Companion to
    /// <see cref="BspPickupImporter.RunSelfTests"/> (pure classname-mapping/
    /// coordinate logic only) and to tests/csharp/PickupEntityTests.cs (a
    /// plain-Mono, data-only inventory check of a real .bsp's entities lump,
    /// with no mapping assertions). This file is the one place that checks
    /// BspPickupImporter's ClassToPlan table against real placements, reading
    /// that table directly rather than duplicating it.
    ///
    /// Builds plain GameObjects with AddComponent and calls public methods
    /// directly (Pickup.TryCollect / Pickup.Tick / Pickup.ForceActivate,
    /// Actor.AddHealth/AddArmor/TakeDamage, WeaponController.AddAmmo/GetAmmo,
    /// ArenaBootstrap.SetPaused, BspPickupImporter.Import) -- the same style
    /// LocalTests.cs uses for BspImportPipeline.Import. Needs an open Unity
    /// Editor but NOT Play Mode.
    ///
    /// NOT wired into Editor/LocalTests.cs (LocalTests.cs is not owned by this
    /// change). Run manually via "My Xonotic/Tests - Pickup checks", or have
    /// LocalTests.Run() call PickupTests.RunSelfTests() the same way it already
    /// calls BspGameplayImporter.RunSelfTests() (one line + one Check(true, ...)).
    /// Throws on the first failed assertion, matching every other self-test in
    /// this project.
    /// </summary>
    public static class PickupTests
    {
        static readonly List<GameObject> _spawned = new List<GameObject>();

        [MenuItem("My Xonotic/Tests - Pickup checks")]
        public static void RunSelfTests()
        {
            _spawned.Clear();
            bool pausedBefore = ArenaBootstrap.IsPaused;
            try
            {
                Health_GrantsWhenNotFull_RejectsWhenFull();
                Armor_GrantsWhenNotFull_RejectsWhenFull();
                Ammo_RejectsWithoutWeaponController();
                Ammo_GrantsThenRejectsWhenCapped();
                DeadActor_IsRejected();
                NullActor_IsRejected();
                Paused_IsRejected_ThenUnpausedSucceeds();
                Tick_RespawnIsDeterministic();
                Tick_RejectsPausedAndInvalidDelta();
                TryCollect_RejectsNonPositiveAmount();
                ForceActivate_ImmediatelyReactivates();
                BspPickupImporter_MapsRealBoilEntities();

                Debug.Log("[PickupTests] RunSelfTests PASS");
            }
            finally
            {
                // Restore static ArenaBootstrap.IsPaused and clean up every
                // temporary GameObject this run created, pass or fail, so a
                // failed assertion never leaves stray objects/state for the
                // next test run (LocalTests.Run() creates its own scene via
                // LocalBuild.CreateDevelopmentScene(), but this file does not
                // assume it runs inside -- or before/after -- that call).
                var bootstrapForReset = new GameObject("PickupTests_ResetBootstrap").AddComponent<ArenaBootstrap>();
                bootstrapForReset.SetPaused(pausedBefore);
                UnityEngine.Object.DestroyImmediate(bootstrapForReset.gameObject);
                foreach (var go in _spawned)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }
                _spawned.Clear();
            }
        }

        // ---------------------------------------------------------------- helpers

        static Actor NewActor(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<Actor>();
        }

        static Pickup NewPickup(PickupType type, int amount)
        {
            var go = new GameObject("TestPickup_" + type);
            _spawned.Add(go);
            go.AddComponent<SphereCollider>();
            var pickup = go.AddComponent<Pickup>();
            pickup.Type = type;
            pickup.Amount = amount;
            return pickup;
        }

        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("[PickupTests] FAIL: " + name);
        }

        // ---------------------------------------------------------------- scenarios

        static void Health_GrantsWhenNotFull_RejectsWhenFull()
        {
            var actor = NewActor("HealthActor");
            Check(actor.Health == Actor.StartHealth, "fresh actor starts at StartHealth");

            var pickup = NewPickup(PickupType.Health, 25);
            bool collected = pickup.TryCollect(actor);
            Check(collected, "health pickup collected below max");
            Check(actor.Health == Actor.StartHealth + 25, "health pickup granted the exact amount");
            Check(!pickup.IsAvailable, "collected health pickup deactivates");

            var fullActor = NewActor("FullHealthActor");
            fullActor.AddHealth(Actor.MaxHealth);
            Check(fullActor.Health == Actor.MaxHealth, "actor pushed to max health");
            var pickup2 = NewPickup(PickupType.Health, 25);
            bool collectedAtFull = pickup2.TryCollect(fullActor);
            Check(!collectedAtFull, "health pickup rejected at max health");
            Check(pickup2.IsAvailable, "rejected health pickup remains available (not consumed)");
        }

        static void Armor_GrantsWhenNotFull_RejectsWhenFull()
        {
            var actor = NewActor("ArmorActor");
            Check(actor.Armor == Actor.StartArmor, "fresh actor starts at StartArmor");

            var pickup = NewPickup(PickupType.Armor, 25);
            Check(pickup.TryCollect(actor), "armor pickup collected below max");
            Check(actor.Armor == Actor.StartArmor + 25, "armor pickup granted the exact amount");

            var fullActor = NewActor("FullArmorActor");
            fullActor.AddArmor(Actor.MaxArmor);
            var pickup2 = NewPickup(PickupType.Armor, 25);
            Check(!pickup2.TryCollect(fullActor), "armor pickup rejected at max armor");
            Check(pickup2.IsAvailable, "rejected armor pickup remains available");
        }

        static void Ammo_RejectsWithoutWeaponController()
        {
            var actor = NewActor("NoWeaponsActor"); // deliberately no WeaponController component
            var pickup = NewPickup(PickupType.AmmoRifle, 20);
            Check(!pickup.TryCollect(actor), "ammo pickup rejected: actor has no WeaponController");
            Check(pickup.IsAvailable, "rejected ammo pickup (missing receiver) remains available, not silently consumed");
        }

        static void Ammo_GrantsThenRejectsWhenCapped()
        {
            var go = new GameObject("ArmedActor");
            _spawned.Add(go);
            var actor = go.AddComponent<Actor>();
            var wc = go.AddComponent<WeaponController>();
            wc.ResetLoadout();
            int startAmmo = wc.GetAmmo(WeaponType.Rifle);
            int maxAmmo = wc.GetDef(WeaponType.Rifle).MaxAmmo;
            Check(startAmmo < maxAmmo, "rifle StartAmmo must be below MaxAmmo for this test to mean anything");

            var pickup = NewPickup(PickupType.AmmoRifle, 20);
            Check(pickup.TryCollect(actor), "ammo pickup collected while below max");
            Check(wc.GetAmmo(WeaponType.Rifle) == Mathf.Min(startAmmo + 20, maxAmmo), "ammo pickup granted the exact (clamped) amount");

            wc.AddAmmo(WeaponType.Rifle, 10_000); // push to the hard cap via the existing public API
            Check(wc.GetAmmo(WeaponType.Rifle) == maxAmmo, "ammo pool is now at MaxAmmo");
            var pickup2 = NewPickup(PickupType.AmmoRifle, 20);
            Check(!pickup2.TryCollect(actor), "ammo pickup rejected once the pool is already at MaxAmmo");
            Check(pickup2.IsAvailable, "rejected ammo pickup (capped) remains available");
        }

        static void DeadActor_IsRejected()
        {
            var actor = NewActor("DeadActor");
            actor.TakeDamage(10_000, Vector3.zero, null); // no armor to absorb it -> Health hits 0 -> IsDead
            Check(actor.IsDead, "actor died from lethal damage");
            var pickup = NewPickup(PickupType.Health, 25);
            Check(!pickup.TryCollect(actor), "dead actor cannot collect a pickup");
            Check(pickup.IsAvailable, "pickup offered to a dead actor remains available");
        }

        static void NullActor_IsRejected()
        {
            var pickup = NewPickup(PickupType.Health, 25);
            Check(!pickup.TryCollect(null), "null actor is rejected without throwing");
            Check(pickup.IsAvailable, "pickup offered to a null actor remains available");
        }

        static void Paused_IsRejected_ThenUnpausedSucceeds()
        {
            var bootstrapGo = new GameObject("PauseTestBootstrap");
            _spawned.Add(bootstrapGo);
            var bootstrap = bootstrapGo.AddComponent<ArenaBootstrap>();
            bootstrap.SetPaused(true);
            Check(ArenaBootstrap.IsPaused, "arena reports paused");

            var actor = NewActor("PausedActor");
            var pickup = NewPickup(PickupType.Health, 25);
            Check(!pickup.TryCollect(actor), "pickup rejected while arena is paused");
            Check(pickup.IsAvailable, "pickup offered while paused remains available");

            bootstrap.SetPaused(false);
            Check(!ArenaBootstrap.IsPaused, "arena reports unpaused");
            Check(pickup.TryCollect(actor), "same pickup collected once unpaused");
        }

        static void Tick_RespawnIsDeterministic()
        {
            var actor = NewActor("TickActor");
            var pickup = NewPickup(PickupType.Health, 25);
            pickup.RespawnTime = 5f;
            Check(pickup.TryCollect(actor), "pickup collected before Tick test");
            Check(!pickup.IsAvailable, "pickup inactive right after collection");

            pickup.Tick(1f);
            Check(!pickup.IsAvailable, "pickup still respawning after a small Tick (1s < RespawnTime 5s)");

            pickup.Tick(1f);
            pickup.Tick(1f);
            Check(!pickup.IsAvailable, "pickup still respawning after 3s total (< 5s)");

            pickup.Tick(10f);
            Check(pickup.IsAvailable, "pickup respawns once accumulated Tick time exceeds RespawnTime");
        }

        static void Tick_RejectsPausedAndInvalidDelta()
        {
            var pickup = NewPickup(PickupType.Health, 25);
            pickup.RespawnTime = 1f; // must be set BEFORE TryCollect: TryCollect latches _respawnTimer = RespawnTime at collection time
            var actor = NewActor("TickGuardActor");
            Check(pickup.TryCollect(actor), "pickup collected before Tick guard test");

            var bootstrapGo = new GameObject("TickGuardBootstrap");
            _spawned.Add(bootstrapGo);
            var bootstrap = bootstrapGo.AddComponent<ArenaBootstrap>();
            bootstrap.SetPaused(true);
            pickup.Tick(10f); // would respawn if not rejected
            Check(!pickup.IsAvailable, "Tick while arena paused does not advance the respawn timer");
            bootstrap.SetPaused(false);

            pickup.Tick(float.NaN);
            pickup.Tick(float.PositiveInfinity);
            pickup.Tick(-5f);
            Check(!pickup.IsAvailable, "Tick rejects NaN/Infinity/negative delta without corrupting the respawn timer");

            pickup.Tick(2f);
            Check(pickup.IsAvailable, "a valid delta after the rejected ones still respawns normally");
        }

        static void TryCollect_RejectsNonPositiveAmount()
        {
            var actor = NewActor("ZeroAmountActor");
            int healthBefore = actor.Health;

            var zero = NewPickup(PickupType.Health, 0);
            Check(!zero.TryCollect(actor), "a zero-Amount pickup is rejected");
            Check(zero.IsAvailable, "rejected zero-Amount pickup remains available");
            Check(actor.Health == healthBefore, "zero-Amount pickup grants nothing");

            var negative = NewPickup(PickupType.Armor, -5);
            Check(!negative.TryCollect(actor), "a negative-Amount pickup is rejected");
            Check(negative.IsAvailable, "rejected negative-Amount pickup remains available");
        }

        static void ForceActivate_ImmediatelyReactivates()
        {
            var actor = NewActor("ForceActivateActor");
            var pickup = NewPickup(PickupType.Armor, 25);
            pickup.RespawnTime = 999f;
            Check(pickup.TryCollect(actor), "pickup collected before ForceActivate test");
            Check(!pickup.IsAvailable, "pickup inactive right after collection");
            pickup.ForceActivate();
            Check(pickup.IsAvailable, "ForceActivate() reactivates immediately regardless of RespawnTime (ArenaBootstrap.Restart() hook)");
        }

        static void BspPickupImporter_MapsRealBoilEntities()
        {
            string bspPath = Path.GetFullPath("ThirdParty/Xonotic/maps-pk3/maps/boil.bsp");
            if (!File.Exists(bspPath))
            {
                Debug.LogWarning("[PickupTests] Skipping BspPickupImporter_MapsRealBoilEntities: real boil.bsp not present at " + bspPath);
                return;
            }

            var doc = BspReader.Read(File.ReadAllBytes(bspPath));

            // Expected count/mapping is derived from the actual raw entities
            // lump against BspPickupImporter.ClassToPlan (the single
            // authoritative table), not a hand-maintained duplicate: this
            // test fails loudly if either the real map or the table changes,
            // instead of silently asserting a stale number.
            var expectedByType = new Dictionary<PickupType, int>();
            var expectedAmountByClass = new Dictionary<string, int>(StringComparer.Ordinal);
            int expectedTotal = 0;
            foreach (var entity in doc.Entities)
            {
                string classname = entity.Get("classname");
                if (string.IsNullOrEmpty(classname)) continue;
                if (!BspPickupImporter.ClassToPlan.TryGetValue(classname, out var plan)) continue;
                expectedByType.TryGetValue(plan.Type, out int n);
                expectedByType[plan.Type] = n + 1;
                expectedAmountByClass[classname] = plan.Amount;
                expectedTotal++;
            }
            Check(expectedTotal > 0, "the real map contains at least one supported item_* entity to exercise the importer against");

            var root = new GameObject("PickupImportRoot");
            _spawned.Add(root);
            var created = new List<Pickup>();
            var warnings = BspPickupImporter.Import(doc, root.transform, created);

            Check(created.Count == expectedTotal,
                "BspPickupImporter maps exactly the " + expectedTotal + " supported real Boil item_* entities (per ClassToPlan), got " + created.Count);

            var actualByType = new Dictionary<PickupType, int>();
            var positions = new HashSet<Vector3>();
            foreach (var p in created)
            {
                actualByType.TryGetValue(p.Type, out int n);
                actualByType[p.Type] = n + 1;

                // GameObject name is "<sourceClassname>_<index>" (see Import());
                // recover the classname and confirm the amount matches
                // ClassToPlan for that exact class, not just its PickupType.
                string goName = p.gameObject.name;
                int lastUnderscore = goName.LastIndexOf('_');
                string sourceClass = lastUnderscore > 0 ? goName.Substring(0, lastUnderscore) : goName;
                Check(expectedAmountByClass.TryGetValue(sourceClass, out int expectedAmount),
                    "created pickup '" + goName + "' has a recognizable supported source classname");
                Check(p.Amount == expectedAmount, "'" + sourceClass + "' pickup Amount matches ClassToPlan (" + expectedAmount + "), got " + p.Amount);

                positions.Add(p.transform.localPosition);
            }
            foreach (var kv in expectedByType)
            {
                actualByType.TryGetValue(kv.Key, out int actual);
                Check(actual == kv.Value, kv.Value + " real Boil entities of PickupType " + kv.Key + " expected, got " + actual);
            }
            Check(!actualByType.ContainsKey(PickupType.AmmoRifle),
                "no AmmoRifle pickups are created from the real map (item_bullets must stay unmapped)");
            Check(positions.Count == created.Count, "every mapped pickup got a distinct real-map position (no collapsed/fake placement)");

            bool warnedWeapon = false, warnedCells = false, warnedStrength = false, warnedBullets = false;
            foreach (var w in warnings)
            {
                if (w.Contains("weapon_") && w.Contains("not instantiated as a pickup")) warnedWeapon = true;
                if (w.Contains("item_cells") && w.Contains("not instantiated as a pickup")) warnedCells = true;
                if (w.Contains("item_strength") && w.Contains("not instantiated as a pickup")) warnedStrength = true;
                if (w.Contains("item_bullets") && w.Contains("not instantiated as a pickup")) warnedBullets = true;
            }
            Check(warnedWeapon, "importer explicitly reports at least one unsupported weapon_* class, never silently mapped");
            Check(warnedCells, "importer explicitly reports item_cells as unsupported");
            Check(warnedStrength, "importer explicitly reports item_strength as unsupported");
            Check(warnedBullets, "importer explicitly reports item_bullets as unsupported (no ammo/weapon correspondence claimed)");
        }
    }
}
