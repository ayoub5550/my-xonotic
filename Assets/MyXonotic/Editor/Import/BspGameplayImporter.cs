using System;
using System.Collections.Generic;
using System.Globalization;
using MyXonotic.Content.Bsp;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Second, additive gameplay pass over an already-parsed BspDocument:
    /// builds one MapTrigger volume per trigger_push / trigger_teleport /
    /// trigger_hurt entity that references a brush submodel ("model" "*N").
    ///
    /// Deliberately separate from BspImportPipeline.Import(path), which only
    /// builds worldspawn geometry and spawn markers and explicitly warns that
    /// "trigger_*" classes are out of scope for that pass (see its
    /// BuildSpawnMarkers unsupported-class warnings). Wiring this method into
    /// that pipeline / the Editor menu / LocalTests is left to whoever owns
    /// BspImportPipeline.cs and LocalTests.cs (not touched here); the expected
    /// call shape is:
    ///
    ///   var arenaRoot = BspImportPipeline.Import(path);   // existing
    ///   var triggerWarnings = BspGameplayImporter.Import(doc, arenaRoot.transform);
    ///
    /// where `doc` is the same BspDocument BspImportPipeline.Import already
    /// parsed internally (currently a local variable there, not returned —
    /// exposing it, or re-reading the file here, is the integrator's call).
    ///
    /// BOUNDS CAVEAT: trigger volumes are built from the source brush model's
    /// axis-aligned Mins/Maxs (a BoxCollider), not its real (possibly
    /// non-box) brush shape. See MapTrigger's class doc for the full caveat.
    /// This is a clean, original approximation of Boil's trigger volumes; no
    /// GPL Xonotic/Quake QuakeC (trigger_push/trigger_teleport/trigger_hurt
    /// touch functions) was read or copied to write it.
    ///
    /// No pickups here: item_/weapon_ entities are explicitly out of scope
    /// for this pass (see AGENTS.md and BspImportPipeline's unsupported-class
    /// warnings, which continue to cover them).
    /// </summary>
    public static class BspGameplayImporter
    {
        private const float MaxSaneEntityCoordinate = 1_000_000f;

        // Defensive ceiling against a pathological/hostile entities lump with an
        // absurd number of trigger_* blocks; a real Q3-family map has at most a
        // few hundred entities total. Mirrors the spirit of
        // BspImportPipeline.MaxBspFileSizeBytes (bound work before it starts).
        private const int MaxTriggerEntities = 4096;

        /// <summary>
        /// Builds MapTrigger volumes for every trigger_push/trigger_teleport/
        /// trigger_hurt entity in <paramref name="doc"/> under <paramref name="root"/>.
        /// Never throws on malformed per-entity data (missing keys, out-of-range
        /// model index, unresolved target, non-finite coordinates); such entities
        /// are skipped and recorded in the returned warnings list, mirroring
        /// BspImportPipeline's warnings-not-exceptions style for per-entity issues.
        /// Only truly invalid call arguments (null doc/root) raise.
        /// </summary>
        public static List<string> Import(BspDocument doc, Transform root)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (root == null) throw new ArgumentNullException(nameof(root));

            var warnings = new List<string>();

            if (doc.Models == null || doc.Models.Length == 0)
            {
                warnings.Add("BspGameplayImporter: document has no models; no trigger volumes can be built (all trigger_* entities reference a brush submodel).");
                return warnings;
            }

            var byTargetName = BuildTargetNameLookup(doc);

            int consideredEntities = 0;
            int importedCount = 0;
            foreach (var entity in doc.Entities)
            {
                if (consideredEntities >= MaxTriggerEntities)
                {
                    warnings.Add(string.Format(
                        "Entities lump has more than {0} entries; stopped scanning for trigger_* entities to bound import work.",
                        MaxTriggerEntities));
                    break;
                }

                string classname = entity.Get("classname");
                if (!TryGetKind(classname, out MapTrigger.Kind kind))
                {
                    continue;
                }
                consideredEntities++;

                if (!TryResolveModel(doc, entity, classname, warnings, out int modelIndex, out BspModel model))
                {
                    continue;
                }

                if (!TryConvertBoundsToUnity(model.Mins, model.Maxs, out Vector3 center, out Vector3 size))
                {
                    warnings.Add(string.Format(
                        "Entity '{0}' (model *{1}) has a non-finite or degenerate bounding box; trigger volume skipped.",
                        classname, modelIndex));
                    continue;
                }

                bool hasDestination = false;
                Vector3 destinationUnity = Vector3.zero;
                float destinationYawUnity = 0f;

                if (kind == MapTrigger.Kind.Push || kind == MapTrigger.Kind.Teleport)
                {
                    ResolveDestination(entity, classname, byTargetName, warnings,
                        out hasDestination, out destinationUnity, out destinationYawUnity);
                }

                int hurtDamage = MapTrigger.DefaultHurtDamagePerTick;
                if (kind == MapTrigger.Kind.Hurt)
                {
                    string dmgStr = entity.Get("dmg");
                    if (!string.IsNullOrEmpty(dmgStr))
                    {
                        if (float.TryParse(dmgStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float dmg) &&
                            IsFiniteAndBounded(dmg) && dmg > 0f)
                        {
                            hurtDamage = Mathf.RoundToInt(dmg);
                        }
                        else
                        {
                            warnings.Add(string.Format(
                                "Entity '{0}' (model *{1}) has an unparsable/non-finite 'dmg' value '{2}'; using the original-approximation default of {3}.",
                                classname, modelIndex, dmgStr, MapTrigger.DefaultHurtDamagePerTick));
                        }
                    }
                }

                var go = new GameObject(classname + "_" + modelIndex);
                go.transform.SetParent(root, false);
                go.transform.localPosition = center;

                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = size;

                // Kinematic Rigidbody so trigger events are reported reliably
                // regardless of which side (this volume vs. the player's
                // CharacterController) Unity's physics backend expects to own a
                // Rigidbody for OnTrigger callbacks, without letting gravity or
                // collision response ever move this static volume.
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                var trigger = go.AddComponent<MapTrigger>();
                trigger.TriggerKind = kind;
                trigger.SourceClass = classname;
                trigger.HasDestination = hasDestination;
                trigger.Destination = destinationUnity;
                trigger.DestinationYaw = destinationYawUnity;
                trigger.HurtDamagePerTick = hurtDamage;

                warnings.Add(string.Format(
                    "Entity '{0}' (model *{1}): trigger volume approximated from the brush model's axis-aligned bounds (BoxCollider); a non-box source brush will not match exactly.",
                    classname, modelIndex));

                if ((kind == MapTrigger.Kind.Push || kind == MapTrigger.Kind.Teleport) && !hasDestination)
                {
                    warnings.Add(string.Format(
                        "Entity '{0}' (model *{1}) has no resolvable destination; the trigger volume was built but will be inert (no invented destination).",
                        classname, modelIndex));
                }

                importedCount++;
            }

            if (importedCount == 0)
            {
                warnings.Add("BspGameplayImporter: no trigger_push/trigger_teleport/trigger_hurt entities with a valid brush submodel were found.");
            }

            return warnings;
        }

        static bool TryGetKind(string classname, out MapTrigger.Kind kind)
        {
            switch (classname)
            {
                case "trigger_push":
                    kind = MapTrigger.Kind.Push;
                    return true;
                case "trigger_teleport":
                    kind = MapTrigger.Kind.Teleport;
                    return true;
                case "trigger_hurt":
                    kind = MapTrigger.Kind.Hurt;
                    return true;
                default:
                    kind = default;
                    return false;
            }
        }

        static Dictionary<string, BspEntity> BuildTargetNameLookup(BspDocument doc)
        {
            var byTargetName = new Dictionary<string, BspEntity>(StringComparer.Ordinal);
            foreach (var entity in doc.Entities)
            {
                string targetName = entity.Get("targetname");
                if (!string.IsNullOrEmpty(targetName) && !byTargetName.ContainsKey(targetName))
                {
                    byTargetName[targetName] = entity;
                }
            }
            return byTargetName;
        }

        static bool TryResolveModel(BspDocument doc, BspEntity entity, string classname, List<string> warnings,
            out int modelIndex, out BspModel model)
        {
            modelIndex = -1;
            model = default;

            string modelStr = entity.Get("model");
            if (string.IsNullOrEmpty(modelStr) || modelStr[0] != '*')
            {
                warnings.Add(string.Format(
                    "Entity '{0}' has no brush-model 'model' key (or a non-'*N' value '{1}'); this importer only builds brush-model trigger volumes, skipped.",
                    classname, modelStr ?? "<none>"));
                return false;
            }

            string indexPart = modelStr.Substring(1);
            if (!int.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out modelIndex) ||
                modelIndex < 0 || modelIndex >= doc.Models.Length)
            {
                warnings.Add(string.Format(
                    "Entity '{0}' model index '{1}' is out of range (document has {2} model(s)); skipped.",
                    classname, modelStr, doc.Models.Length));
                modelIndex = -1;
                return false;
            }

            // Model 0 is worldspawn's own static geometry; a trigger referencing it
            // would mean the entire level is one giant trigger, which is not a real
            // Boil trigger and almost certainly a malformed/hostile entities lump.
            if (modelIndex == 0)
            {
                warnings.Add(string.Format(
                    "Entity '{0}' references model *0 (worldspawn); refusing to treat the whole level as a trigger volume, skipped.",
                    classname));
                modelIndex = -1;
                return false;
            }

            model = doc.Models[modelIndex];
            return true;
        }

        static void ResolveDestination(BspEntity entity, string classname, Dictionary<string, BspEntity> byTargetName,
            List<string> warnings, out bool hasDestination, out Vector3 destinationUnity, out float destinationYawUnity)
        {
            hasDestination = false;
            destinationUnity = Vector3.zero;
            // Same default as BspImportPipeline.BuildSpawnMarkers: a missing
            // 'angle' key means quake yaw 0, which converts to Unity yaw 90
            // (QuakeYawToUnity(0) = 90 - 0 = 90), not Unity yaw 0. Keep that
            // convention here instead of defaulting to an unconverted 0.
            destinationYawUnity = BspCoordinateSpace.QuakeYawToUnity(0f);

            string targetName = entity.Get("target");
            if (string.IsNullOrEmpty(targetName))
            {
                warnings.Add(string.Format("Entity '{0}' has no 'target' key.", classname));
                return;
            }

            if (!byTargetName.TryGetValue(targetName, out BspEntity targetEntity))
            {
                warnings.Add(string.Format(
                    "Entity '{0}' targets '{1}', but no entity with that 'targetname' was found (expected target_position or misc_teleporter_dest).",
                    classname, targetName));
                return;
            }

            string originStr = targetEntity.Get("origin");
            if (originStr == null || !TryParseVec3(originStr, out BspVec3 quakeOrigin))
            {
                warnings.Add(string.Format(
                    "Entity '{0}' target '{1}' ({2}) has no parsable 'origin' key; destination unresolved.",
                    classname, targetName, targetEntity.Get("classname") ?? "?"));
                return;
            }

            var unityOrigin = BspCoordinateSpace.QuakeToUnity(quakeOrigin);
            destinationUnity = new Vector3(unityOrigin.X, unityOrigin.Y, unityOrigin.Z);
            hasDestination = true;

            string angleStr = targetEntity.Get("angle");
            if (angleStr != null)
            {
                if (float.TryParse(angleStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float quakeYaw) &&
                    IsFiniteAndBounded(quakeYaw))
                {
                    destinationYawUnity = BspCoordinateSpace.QuakeYawToUnity(quakeYaw);
                }
                else
                {
                    warnings.Add(string.Format(
                        "Entity '{0}' target '{1}' has an unparsable/non-finite 'angle' value '{2}'; defaulting destination yaw to 0.",
                        classname, targetName, angleStr));
                }
            }
        }

        /// <summary>
        /// Converts a Quake-space AABB (Mins/Maxs, arbitrary per-axis ordering) into a
        /// Unity-space center/size pair. BspCoordinateSpace.QuakeToUnity swaps Y/Z, so
        /// each converted corner is re-min/maxed per axis rather than assuming
        /// min stays min after the swap.
        /// </summary>
        internal static bool TryConvertBoundsToUnity(BspVec3 quakeMins, BspVec3 quakeMaxs, out Vector3 center, out Vector3 size)
        {
            center = Vector3.zero;
            size = Vector3.zero;

            if (!IsFiniteAndBounded(quakeMins) || !IsFiniteAndBounded(quakeMaxs))
            {
                return false;
            }

            var a = BspCoordinateSpace.QuakeToUnity(quakeMins);
            var b = BspCoordinateSpace.QuakeToUnity(quakeMaxs);

            float minX = Mathf.Min(a.X, b.X), maxX = Mathf.Max(a.X, b.X);
            float minY = Mathf.Min(a.Y, b.Y), maxY = Mathf.Max(a.Y, b.Y);
            float minZ = Mathf.Min(a.Z, b.Z), maxZ = Mathf.Max(a.Z, b.Z);

            size = new Vector3(maxX - minX, maxY - minY, maxZ - minZ);
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                return false;
            }

            center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, (minZ + maxZ) * 0.5f);
            return true;
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
        /// Dependency-free self-checks for the pure/static parts of this importer
        /// (no scene, no AssetDatabase, no BspReader) plus MapTrigger's arc math.
        /// Not wired into Editor/LocalTests.cs (not owned by this change); run it
        /// manually from an Editor menu item or add a one-line call from
        /// LocalTests.Run(). Throws on the first failed assertion, like LocalTests.
        /// </summary>
        public static void RunSelfTests()
        {
            // Boil's actual trigger_teleport model (*2): mins/maxs taken from the
            // real boil.bsp models lump, used here only as a plausible-shaped input,
            // not as any copied gameplay logic.
            var mins = new BspVec3(928.0f, 531.2000122f, -307.2000122f);
            var maxs = new BspVec3(1040.0001220f, 643.2000732f, -179.1999969f);
            if (!TryConvertBoundsToUnity(mins, maxs, out Vector3 center, out Vector3 size))
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: expected a valid AABB for a real Boil model.");
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: converted size must be strictly positive on every axis.");

            // Degenerate (zero-volume) box must be rejected.
            if (TryConvertBoundsToUnity(new BspVec3(0, 0, 0), new BspVec3(0, 0, 0), out _, out _))
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: a zero-volume AABB must be rejected.");

            // Non-finite input must be rejected.
            if (TryConvertBoundsToUnity(new BspVec3(float.NaN, 0, 0), new BspVec3(1, 1, 1), out _, out _))
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: NaN mins must be rejected.");

            if (!TryParseVec3("1 2 3", out BspVec3 parsed) || parsed.X != 1f || parsed.Y != 2f || parsed.Z != 3f)
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: TryParseVec3 basic parse.");
            if (TryParseVec3("1 2", out _))
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: TryParseVec3 must reject wrong arity.");
            if (TryParseVec3("1 2 notanumber", out _))
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: TryParseVec3 must reject unparsable components.");

            // Arc math sanity: launching level and landing level with a positive
            // apex must produce a strictly positive vertical launch speed and,
            // for a purely horizontal move, a horizontal velocity pointing at
            // the destination.
            Vector3 v = MapTrigger.ComputeArcVelocity(Vector3.zero, new Vector3(10f, 0f, 0f), 2f, 20f);
            if (v.y <= 0f)
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: level launch must have positive vertical speed.");
            if (v.x <= 0f || Mathf.Abs(v.z) > 1e-4f)
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: level launch toward +X must have positive Vx and ~0 Vz.");

            // A destination above the start still resolves to a finite, positive-time solution.
            Vector3 vUp = MapTrigger.ComputeArcVelocity(Vector3.zero, new Vector3(0f, 5f, 0f), 2f, 20f);
            if (float.IsNaN(vUp.y) || float.IsInfinity(vUp.y) || vUp.y <= 0f)
                throw new Exception("[BspGameplayImporter.RunSelfTests] FAIL: upward launch must yield a finite positive vertical speed.");

            Debug.Log("[BspGameplayImporter] RunSelfTests PASS");
        }
    }
}
