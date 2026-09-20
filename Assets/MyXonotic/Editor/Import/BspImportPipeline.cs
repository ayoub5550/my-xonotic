using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyXonotic.Content;
using MyXonotic.Content.Bsp;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Editor-only entry point: turns a single .bsp file on disk into an
    /// in-scene arena GameObject. Does not read/extract PK3 archives itself
    /// (see tools/content/pk3_tool.py for safe PK3 inventory/extraction) and
    /// never executes any script/code found inside a BSP or archive — it
    /// only interprets fixed-layout binary lumps and quoted entity text.
    /// </summary>
    public static class BspImportPipeline
    {
        private const string GeneratedRoot = "Assets/MyXonotic/Generated/Imported";
        private const string VertexColorShaderName = "MyXonotic/VertexColor";

        // A legitimate Q3-family map .bsp is typically a few hundred KB to
        // a few tens of MB. 128 MiB is a generous ceiling that still
        // refuses to blindly File.ReadAllBytes() an absurd/hostile input
        // before the parser's own lump-level bounds checks even run.
        private const long MaxBspFileSizeBytes = 128L * 1024 * 1024;

        /// <summary>
        /// Imports a single .bsp file. Returns the root GameObject (carrying
        /// an ImportedArena component) with child spawn-marker GameObjects
        /// (BspSpawnPoint) and one or more mesh children for world geometry.
        /// Throws MyXonotic.Content.Bsp.BspFormatException for unsupported
        /// or malformed input — callers should surface that message to the
        /// user rather than let Unity log a raw stack trace.
        /// </summary>
        public static GameObject Import(string bspPath)
        {
            if (string.IsNullOrEmpty(bspPath))
            {
                throw new ArgumentException("bspPath must not be null/empty.", nameof(bspPath));
            }
            if (!File.Exists(bspPath))
            {
                throw new FileNotFoundException("BSP file not found.", bspPath);
            }

            long fileSize = new FileInfo(bspPath).Length;
            if (fileSize > MaxBspFileSizeBytes)
            {
                throw new BspFormatException(string.Format(
                    "'{0}' is {1} bytes, exceeding the {2}-byte safety ceiling for a single BSP; refusing to read it into memory.",
                    bspPath, fileSize, MaxBspFileSizeBytes));
            }

            byte[] data = File.ReadAllBytes(bspPath);
            BspDocument doc = BspReader.Read(data); // throws BspFormatException on unsupported/malformed input

            string sourceName = Path.GetFileName(bspPath);
            string safeName = MakeSafeFolderName(Path.GetFileNameWithoutExtension(bspPath));

            var warnings = new List<string>(doc.Warnings);

            var root = new GameObject(safeName);
            var arena = root.AddComponent<ImportedArena>();
            arena.sourceName = sourceName;

            if (doc.Models.Length == 0)
            {
                warnings.Add("No models found; imported arena has no static geometry.");
            }
            else
            {
                // Model 0 is worldspawn: the only model treated as static
                // level geometry. Models 1..N are inline brush submodels
                // referenced by mover/trigger entities (doors, platforms,
                // trigger volumes) and must NOT be merged into the static
                // world mesh, or doors/lifts would render baked shut/open
                // and triggers would become solid world geometry.
                if (doc.Models.Length > 1)
                {
                    warnings.Add(string.Format(
                        "{0} inline brush submodel(s) (movers/triggers) found; only worldspawn (model 0) geometry was imported, submodels are skipped in this pass.",
                        doc.Models.Length - 1));
                }

                var meshData = BspGeometryBuilder.BuildModel(doc, 0, warnings);
                if (meshData.Positions.Count > 0 &&
                    (meshData.Triangles.Count > 0 || meshData.CollisionTriangles.Count > 0))
                {
                    BuildMeshChild(root.transform, safeName, meshData, warnings);
                }
                else
                {
                    warnings.Add("Worldspawn produced zero renderable triangles.");
                }
            }

            BuildSpawnMarkers(doc, root.transform, warnings);

            arena.warnings = warnings.ToArray();
            foreach (var w in warnings)
            {
                Debug.LogWarning("[BspImportPipeline] " + sourceName + ": " + w);
            }

            return root;
        }

        private static void BuildMeshChild(Transform parent, string safeName, BspMeshData meshData, List<string> warnings)
        {
            string folder = GeneratedRoot + "/" + safeName;
            EnsureFolder(folder);

            var mesh = new Mesh();
            mesh.name = safeName + "_world";
            mesh.indexFormat = meshData.Positions.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            var positions = new Vector3[meshData.Positions.Count];
            var colors = new Color32[meshData.Colors.Count];
            for (int i = 0; i < positions.Length; i++)
            {
                var p = meshData.Positions[i];
                positions[i] = new Vector3(p.X, p.Y, p.Z);
            }
            for (int i = 0; i < colors.Length; i++)
            {
                var c = meshData.Colors[i];
                colors[i] = new Color32(c.R, c.G, c.B, c.A);
            }

            mesh.vertices = positions;
            mesh.colors32 = colors;
            mesh.triangles = meshData.Triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string meshAssetPath = folder + "/" + safeName + "_world.asset";
            mesh = CreateOrReplaceAsset(mesh, meshAssetPath);

            var go = new GameObject(safeName + "_World");
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.enabled = meshData.Triangles.Count > 0;
            var shader = Shader.Find(VertexColorShaderName);
            if (shader == null)
            {
                warnings.Add("Shader '" + VertexColorShaderName + "' not found; world geometry has no material assigned. " +
                             "This importer intentionally does not attempt real texture/material parity with Xonotic.");
            }
            else
            {
                var mat = new Material(shader) { name = safeName + "_VertexColor" };
                string matAssetPath = folder + "/" + safeName + "_VertexColor.mat";
                mat = CreateOrReplaceAsset(mat, matAssetPath);
                mr.sharedMaterial = mat;
            }

            if (meshData.CollisionTriangles.Count >= 3)
            {
                var collisionMesh = new Mesh();
                collisionMesh.name = safeName + "_collision";
                collisionMesh.indexFormat = mesh.indexFormat;
                collisionMesh.vertices = positions;
                collisionMesh.triangles = meshData.CollisionTriangles.ToArray();
                collisionMesh.RecalculateBounds();

                string colliderMeshPath = folder + "/" + safeName + "_collision.asset";
                collisionMesh = CreateOrReplaceAsset(collisionMesh, colliderMeshPath);

                var collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = collisionMesh;
                collider.convex = false;
            }
            else
            {
                warnings.Add("No collision-eligible (CONTENTS_SOLID) surfaces found; world has no MeshCollider.");
            }
        }

        private static void BuildSpawnMarkers(BspDocument doc, Transform parent, List<string> warnings)
        {
            int spawnCount = 0;
            foreach (var entity in doc.Entities)
            {
                string classname = entity.Get("classname");
                if (classname != "info_player_deathmatch" && classname != "info_player_start")
                {
                    continue;
                }

                string originStr = entity.Get("origin");
                if (originStr == null)
                {
                    warnings.Add("Entity '" + classname + "' has no 'origin' key; spawn marker skipped.");
                    continue;
                }
                if (!TryParseVec3(originStr, out BspVec3 quakeOrigin))
                {
                    warnings.Add("Entity '" + classname + "' has an unparsable origin '" + originStr + "'; spawn marker skipped.");
                    continue;
                }

                float quakeYaw = 0f;
                string angleStr = entity.Get("angle");
                if (angleStr != null)
                {
                    bool parsed = float.TryParse(angleStr,
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out quakeYaw);
                    if (!parsed || !IsFiniteAndBounded(quakeYaw))
                    {
                        warnings.Add("Entity '" + classname + "' has an unparsable/non-finite 'angle' value '" + angleStr + "'; defaulting yaw to 0.");
                        quakeYaw = 0f;
                    }
                }

                var unityOrigin = BspCoordinateSpace.QuakeToUnity(quakeOrigin);
                var go = new GameObject(classname + "_" + spawnCount);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(unityOrigin.X, unityOrigin.Y, unityOrigin.Z);
                float unityYaw = BspCoordinateSpace.QuakeYawToUnity(quakeYaw);
                go.transform.localRotation = Quaternion.Euler(0f, unityYaw, 0f);

                var marker = go.AddComponent<BspSpawnPoint>();
                marker.yaw = unityYaw;
                marker.sourceClass = classname;
                spawnCount++;
            }

            if (spawnCount == 0)
            {
                warnings.Add("No info_player_deathmatch/info_player_start entities found; arena has no spawn markers.");
            }

            // Flag other gameplay-relevant entity classes this pass does not
            // build anything for, so missing functionality is explicit
            // rather than silently absent.
            var unsupportedClasses = new HashSet<string>();
            foreach (var entity in doc.Entities)
            {
                string classname = entity.Get("classname");
                if (string.IsNullOrEmpty(classname)) continue;
                if (classname == "info_player_deathmatch" || classname == "info_player_start" || classname == "worldspawn")
                {
                    continue;
                }
                if (classname.StartsWith("func_") || classname.StartsWith("trigger_") ||
                    classname.StartsWith("item_") || classname.StartsWith("weapon_") ||
                    classname.StartsWith("target_") || classname.StartsWith("path_"))
                {
                    unsupportedClasses.Add(classname);
                }
            }
            foreach (var c in unsupportedClasses.OrderBy(s => s, StringComparer.Ordinal))
            {
                warnings.Add("Entity class '" + c + "' present in map but not instantiated by this import pass (movers/items/triggers are out of scope here).");
            }
        }

        private static bool TryParseVec3(string s, out BspVec3 v)
        {
            v = default(BspVec3);
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, culture, out float x)) return false;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, culture, out float y)) return false;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, culture, out float z)) return false;
            if (!IsFiniteAndBounded(x) || !IsFiniteAndBounded(y) || !IsFiniteAndBounded(z)) return false;
            v = new BspVec3(x, y, z);
            return true;
        }

        private const float MaxSaneEntityCoordinate = 1_000_000f;

        private static bool IsFiniteAndBounded(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            return v >= -MaxSaneEntityCoordinate && v <= MaxSaneEntityCoordinate;
        }

        private static string MakeSafeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "arena";
            // Same safe folder names on Windows, macOS and Linux.
            var chars = name.Take(80).Select(ch =>
                char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_').ToArray();
            string cleaned = new string(chars);
            // Defensive: also strip any path separators/traversal sequences
            // even though GetFileNameWithoutExtension already removed them.
            cleaned = cleaned.Replace("..", "_").Trim('_');
            return string.IsNullOrEmpty(cleaned) ? "arena" : cleaned;
        }

        /// <summary>
        /// Update in place so repeat imports preserve GUIDs and existing scene
        /// references. Returning the persisted object is important: the caller
        /// must not keep a reference to the discarded transient mesh/material.
        /// </summary>
        private static T CreateOrReplaceAsset<T>(T asset, string assetPath) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (existing != null)
            {
                if (!(existing is T typed))
                    throw new InvalidOperationException("Existing generated asset has a different type: " + assetPath);
                EditorUtility.CopySerialized(asset, typed);
                EditorUtility.SetDirty(typed);
                UnityEngine.Object.DestroyImmediate(asset);
                return typed;
            }
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void EnsureFolder(string assetFolderPath)
        {
            var parts = assetFolderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
