using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    /// only interprets fixed-layout binary lumps and quoted entity text, and
    /// parses material scripts (see XonoticContentResolver) purely as data.
    /// </summary>
    public static class BspImportPipeline
    {
        private const string GeneratedRoot = "Assets/MyXonotic/Generated/Imported";
        private const string VertexColorShaderName = "MyXonotic/VertexColor";
        private const string LightmappedShaderName = "MyXonotic/Lightmapped";
        private const string SkySixSidedShaderName = "MyXonotic/Sky6Sided";

        // A legitimate Q3-family map .bsp is typically a few hundred KB to
        // a few tens of MB. 128 MiB is a generous ceiling that still
        // refuses to blindly File.ReadAllBytes() an absurd/hostile input
        // before the parser's own lump-level bounds checks even run.
        private const long MaxBspFileSizeBytes = 128L * 1024 * 1024;

        /// <summary>
        /// Imports a single .bsp file. Returns the root GameObject (carrying
        /// an ImportedArena component) with child spawn-marker GameObjects
        /// (BspSpawnPoint), one mesh child for world geometry (one submesh +
        /// material per distinct shader/lightmap combination, when content
        /// roots resolve real textures/lightmaps; a flat vertex-colour
        /// fallback otherwise), and any gameplay trigger volumes
        /// BspGameplayImporter recognizes. Throws
        /// MyXonotic.Content.Bsp.BspFormatException for unsupported or
        /// malformed input — callers should surface that message to the
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
            string mapName = Path.GetFileNameWithoutExtension(bspPath);
            string safeName = MakeSafeFolderName(mapName);

            var warnings = new List<string>(doc.Warnings);
            var manifestEntries = new List<ManifestEntry>();

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
                        "{0} inline brush submodel(s) (movers/triggers) found; only worldspawn (model 0) geometry was imported here (see BspGameplayImporter for trigger_* volumes built from a subset of these submodels).",
                        doc.Models.Length - 1));
                }

                var meshData = BspGeometryBuilder.BuildModel(doc, 0, warnings);
                if (meshData.Positions.Count > 0 &&
                    (meshData.Triangles.Count > 0 || meshData.CollisionTriangles.Count > 0))
                {
                    BuildMeshChild(root.transform, safeName, mapName, doc, meshData, warnings, manifestEntries);
                }
                else
                {
                    warnings.Add("Worldspawn produced zero renderable triangles.");
                }
            }

            BuildSpawnMarkers(doc, root.transform, warnings);

            // Additive gameplay pass (trigger_push/trigger_teleport/trigger_hurt
            // volumes from brush submodels). Kept separate from worldspawn/spawn
            // building above; see BspGameplayImporter's own doc comment.
            try
            {
                warnings.AddRange(BspGameplayImporter.Import(doc, root.transform));
            }
            catch (Exception e)
            {
                warnings.Add("BspGameplayImporter.Import threw and was skipped: " + e.Message);
            }

            arena.warnings = warnings.ToArray();
            foreach (var w in warnings)
            {
                Debug.LogWarning("[BspImportPipeline] " + sourceName + ": " + w);
            }

            WriteImportManifest(safeName, sourceName, manifestEntries, warnings);

            return root;
        }

        // --------------------------------------------------------------
        // World mesh: geometry + per-(shader,lightmap) submeshes/materials
        // --------------------------------------------------------------

        private static void BuildMeshChild(
            Transform parent, string safeName, string mapName, BspDocument doc, BspMeshData meshData,
            List<string> warnings, List<ManifestEntry> manifestEntries)
        {
            string folder = GeneratedRoot + "/" + safeName;
            EnsureFolder(folder);
            EnsureFolder(folder + "/textures");
            EnsureFolder(folder + "/materials");

            var mesh = new Mesh();
            mesh.name = safeName + "_world";
            mesh.indexFormat = meshData.Positions.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            var positions = new Vector3[meshData.Positions.Count];
            var colors = new Color32[meshData.Colors.Count];
            var surfaceUvs = new Vector2[meshData.SurfaceUvs.Count];
            var lightmapUvs = new Vector2[meshData.LightmapUvs.Count];
            var normals = new Vector3[meshData.Normals.Count];
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
            for (int i = 0; i < surfaceUvs.Length; i++)
            {
                var uv = meshData.SurfaceUvs[i];
                surfaceUvs[i] = new Vector2(uv.X, uv.Y);
            }
            for (int i = 0; i < lightmapUvs.Length; i++)
            {
                var uv = meshData.LightmapUvs[i];
                lightmapUvs[i] = new Vector2(uv.X, uv.Y);
            }
            bool anyNonZeroNormal = false;
            for (int i = 0; i < normals.Length; i++)
            {
                var n = meshData.Normals[i];
                normals[i] = new Vector3(n.X, n.Y, n.Z);
                if (normals[i].sqrMagnitude > 1e-8f) anyNonZeroNormal = true;
            }

            mesh.vertices = positions;
            mesh.colors32 = colors;
            mesh.uv = surfaceUvs;
            mesh.uv2 = lightmapUvs;

            var resolver = new XonoticContentResolver();
            if (resolver.Roots.Count == 0)
            {
                warnings.Add(
                    "XonoticContentResolver found no existing content roots (checked XONOTIC_CONTENT_ROOTS, then " +
                    "ExternalContent/maps, ExternalContent/data, ThirdParty/Xonotic/maps-pk3, ThirdParty/Xonotic/data); " +
                    "world geometry will use the flat vertex-colour fallback material for every surface.");
            }

            bool hasDeluxemaps = HasLikelyDeluxemaps(resolver, mapName);
            if (hasDeluxemaps)
            {
                warnings.Add(
                    "External lightmap directory for '" + mapName + "' looks deluxemapped (alternating lighting/normal " +
                    "images); only the even lighting lightmaps referenced directly by face data are used, deluxemap " +
                    "(bumped specular) images are intentionally not sampled by this pass.");
            }

            var diffuseCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            var lightmapCache = new Dictionary<int, Texture2D>();
            var fallbackShader = Shader.Find(VertexColorShaderName);
            var lightmappedShader = Shader.Find(LightmappedShaderName);
            if (lightmappedShader == null)
            {
                warnings.Add("Shader '" + LightmappedShaderName + "' not found; all groups fall back to '" + VertexColorShaderName + "'.");
            }

            int groupCount = Math.Max(meshData.Groups.Count, 1);
            mesh.subMeshCount = groupCount;
            var materials = new Material[groupCount];

            if (meshData.Groups.Count == 0)
            {
                mesh.SetTriangles(meshData.Triangles, 0);
                materials[0] = null;
            }
            else
            {
                for (int gi = 0; gi < meshData.Groups.Count; gi++)
                {
                    var group = meshData.Groups[gi];
                    mesh.SetIndices(group.Triangles.ToArray(), MeshTopology.Triangles, gi, false);
                    materials[gi] = BuildGroupMaterial(
                        doc, group, folder, mapName, resolver, diffuseCache, lightmapCache,
                        lightmappedShader, fallbackShader, warnings, manifestEntries);
                }
            }

            if (anyNonZeroNormal)
            {
                mesh.normals = normals;
            }
            else
            {
                warnings.Add("Worldspawn vertex data had no usable normals; recalculating flat/smoothed normals instead.");
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();

            string meshAssetPath = folder + "/" + safeName + "_world.asset";
            mesh = CreateOrReplaceAsset(mesh, meshAssetPath);

            var go = new GameObject(safeName + "_World");
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.enabled = meshData.Triangles.Count > 0;
            mr.sharedMaterials = materials;

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
                warnings.Add(
                    "No collision-eligible (CONTENTS_SOLID) surfaces found; world has no MeshCollider. Collision, when " +
                    "present, still only covers eligible face triangles, not the original brush volumes — see AGENTS.md.");
            }
        }

        /// <summary>
        /// Builds (or fetches from the caches) the one persisted Material
        /// asset for a (shader,lightmap) surface group: resolves a diffuse
        /// texture and a lighting source (internal lump block, external
        /// lightmap image, or per-vertex colour) through
        /// <see cref="XonoticContentResolver"/>, and always returns a usable
        /// material — degrading to the flat vertex-colour fallback (with an
        /// explicit warning) rather than leaving a group unrendered.
        /// </summary>
        private static Material BuildGroupMaterial(
            BspDocument doc, BspSurfaceGroup group, string folder, string mapName, XonoticContentResolver resolver,
            Dictionary<string, Texture2D> diffuseCache, Dictionary<int, Texture2D> lightmapCache,
            Shader lightmappedShader, Shader fallbackShader, List<string> warnings, List<ManifestEntry> manifestEntries)
        {
            string shaderName = "shader#" + group.ShaderIndex;
            MaterialScript script = null;
            bool shaderValid = group.ShaderIndex >= 0 && group.ShaderIndex < doc.Shaders.Length;
            if (shaderValid)
            {
                shaderName = doc.Shaders[group.ShaderIndex].Name;
                if (string.IsNullOrEmpty(shaderName)) shaderName = "shader#" + group.ShaderIndex;
            }
            else
            {
                warnings.Add(string.Format("Surface group references out-of-range shader index {0}; using fallback material.", group.ShaderIndex));
            }

            string safeShaderName = MakeSafeFolderName(shaderName.Replace('/', '_'));
            string materialName = safeShaderName + "_lm" + group.LightmapIndex;

            // Sky surfaces (surfaceparm sky / skyparms) branch out here,
            // BEFORE the ordinary diffuse-texture resolution below: that
            // path's qer_editorimage fallback is exactly the credit/preview
            // texture (e.g. textures/skies/polluted_earth.jpg) that used to
            // get stretched across sky geometry as if it were a normal
            // diffuse map. See BuildSkyMaterial for the original six-side
            // replacement and its explicit missing/unsupported fallback.
            if (shaderValid)
            {
                var skyScript = resolver.GetScript(shaderName);
                bool isSky = skyScript != null && (skyScript.Has("sky") || !string.IsNullOrEmpty(skyScript.SkyEnv));
                if (isSky)
                {
                    return BuildSkyMaterial(
                        skyScript, shaderName, folder, resolver, warnings, manifestEntries, materialName, fallbackShader);
                }
            }

            Texture2D diffuse = null;
            if (shaderValid)
            {
                string diffusePath = resolver.ResolveDiffuse(shaderName, out script);
                if (diffusePath == null)
                {
                    warnings.Add("No texture resolved for shader '" + shaderName + "'; check XONOTIC_CONTENT_ROOTS/ThirdParty coverage. Group uses fallback material.");
                }
                else if (!diffuseCache.TryGetValue(diffusePath, out diffuse))
                {
                    string reason;
                    var loaded = BspTextureLoader.Load(diffusePath, srgb: true, failureReason: out reason);
                    if (loaded == null)
                    {
                        warnings.Add("Texture for shader '" + shaderName + "' could not be loaded (" + reason + "); group uses fallback material.");
                        diffuseCache[diffusePath] = null;
                    }
                    else
                    {
                        string texAssetPath = folder + "/textures/" + MakeSafeFolderName(Path.GetFileNameWithoutExtension(diffusePath)) + "_" + StableShortHash(diffusePath) + ".asset";
                        diffuse = CreateOrReplaceAsset(loaded, texAssetPath);
                        diffuseCache[diffusePath] = diffuse;
                        manifestEntries.Add(ManifestEntry.For("diffuse", shaderName, diffusePath));
                    }
                }
            }

            if (diffuse == null)
            {
                return BuildFallbackMaterial(materialName, fallbackShader, folder);
            }

            var shader = lightmappedShader != null ? lightmappedShader
                : (fallbackShader != null ? fallbackShader : Shader.Find("Hidden/InternalErrorShader"));
            var mat = new Material(shader) { name = materialName };
            mat.SetTexture("_MainTex", diffuse);

            // Lighting source: internal lump block, else external lightmap
            // image, else per-vertex colour (LightMode 2), matching what
            // BspGeometryBuilder already carries per vertex either way.
            float lightMode = 2f; // default: vertex colour.
            if (group.LightmapIndex >= 0)
            {
                Texture2D lightmap;
                if (!lightmapCache.TryGetValue(group.LightmapIndex, out lightmap))
                {
                    lightmap = ResolveLightmapTexture(doc, group.LightmapIndex, folder, mapName, resolver, warnings, manifestEntries);
                    lightmapCache[group.LightmapIndex] = lightmap;
                }
                if (lightmap != null)
                {
                    mat.SetTexture("_LightMap", lightmap);
                    lightMode = 1f;
                }
                else
                {
                    warnings.Add(string.Format(
                        "Shader '{0}': lightmap index {1} had no internal block and no external image resolved; falling back to per-vertex colour lighting.",
                        shaderName, group.LightmapIndex));
                }
            }
            // Negative indices (-1/-2/-3/...) are the documented Q3-family
            // sentinels for "no lightmap"/"fullbright"/"by vertex colour";
            // per-vertex colour (already carried on every vertex) is the
            // correct, format-faithful fallback for all of them here.

            mat.SetFloat("_LightMode", lightMode);
            bool cullNone = script != null && script.CullNone;
            mat.SetFloat("_Cull", cullNone ? 0f : 2f);

            bool wantsAlphaTest = script != null && script.Stages.Any(s => !string.IsNullOrEmpty(s.AlphaFunc) || !string.IsNullOrEmpty(s.BlendFunc));
            if (wantsAlphaTest)
            {
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cutoff", 0.5f);
                warnings.Add("Shader '" + shaderName + "' has an alpha-blended/alpha-tested stage; approximated here as a 0.5 alpha-cutout, not true translucency (no blend pass in Resources/Lightmapped.shader).");
            }

            return CreateOrReplaceAsset(mat, folder + "/materials/" + materialName + ".mat");
        }

        /// <summary>
        /// Never-Standard, always-cheap opaque fallback material shared by
        /// every "could not resolve real content" path (missing content
        /// roots, unloadable image, unresolved sky env, missing
        /// Sky6Sided/Lightmapped shader, ...). "Hidden/InternalErrorShader"
        /// is Unity's always-available, trivially cheap built-in error
        /// shader; "Standard" is never used here (AGENTS.md documents it as
        /// a ~1h shader-variant-compile trap in this sandboxed toolchain).
        /// </summary>
        private static Material BuildFallbackMaterial(string materialName, Shader fallbackShader, string folder)
        {
            var fallbackShaderChoice = fallbackShader != null ? fallbackShader : Shader.Find("Hidden/InternalErrorShader");
            var fallbackMat = new Material(fallbackShaderChoice)
            {
                name = materialName + "_Fallback"
            };
            return CreateOrReplaceAsset(fallbackMat, folder + "/materials/" + materialName + "_fallback.mat");
        }

        // --------------------------------------------------------------
        // Sky surfaces: bounded original six-side images, never the
        // qer_editorimage credit/editor-preview texture.
        // --------------------------------------------------------------

        private static readonly string[] SkySideOrder = { "rt", "lf", "ft", "bk", "up", "dn" };

        private static string SkySidePropertyName(string side)
        {
            switch (side)
            {
                case "rt": return "_SkyRt";
                case "lf": return "_SkyLf";
                case "ft": return "_SkyFt";
                case "bk": return "_SkyBk";
                case "up": return "_SkyUp";
                case "dn": return "_SkyDn";
                default: throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown sky side key.");
            }
        }

        /// <summary>
        /// Builds a "MyXonotic/Sky6Sided" material for a surfaceparm-sky
        /// group from the map's real skyparms env base (e.g.
        /// "env/polluted_earth/polluted_earth"), assigning all six original
        /// rt/lf/ft/bk/up/dn side images from
        /// <see cref="XonoticContentResolver.FindSkybox"/> — each stays an
        /// individually loaded/provenance-tracked texture ("sky-rt" ..
        /// "sky-dn" manifest entries), not a single texture repeated across
        /// every sky face and never the shader script's qer_editorimage
        /// credit/preview image. Falls back to the ordinary flat/error
        /// material (with an explicit warning, never silently) when the env
        /// base is missing, any of the six side images cannot be found or
        /// loaded, or the Sky6Sided shader itself is unavailable — the
        /// group still renders something reasonable, it never crashes the
        /// import or leaves a group without a material.
        /// </summary>
        private static Material BuildSkyMaterial(
            MaterialScript script, string shaderName, string folder, XonoticContentResolver resolver,
            List<string> warnings, List<ManifestEntry> manifestEntries, string materialName, Shader fallbackShader)
        {
            string envBase = script.SkyEnv;
            if (string.IsNullOrEmpty(envBase))
            {
                warnings.Add(
                    "Shader '" + shaderName + "' has surfaceparm sky but no resolvable skyparms env base " +
                    "(both sides may be '-', e.g. a decal-only sky variant); using the flat fallback material " +
                    "instead of any single stretched preview texture.");
                return BuildFallbackMaterial(materialName, fallbackShader, folder);
            }

            var sides = resolver.FindSkybox(envBase);
            if (sides == null)
            {
                warnings.Add(
                    "Shader '" + shaderName + "' sky env '" + envBase + "' is missing one or more of the six " +
                    "rt/lf/ft/bk/up/dn side images under XONOTIC_CONTENT_ROOTS/ThirdParty; sky surface uses the " +
                    "flat fallback material rather than any single editor-preview/credit texture.");
                return BuildFallbackMaterial(materialName, fallbackShader, folder);
            }

            var skyShader = Shader.Find(SkySixSidedShaderName);
            if (skyShader == null)
            {
                warnings.Add("Shader '" + SkySixSidedShaderName + "' not found in Resources; sky surface uses the flat fallback material.");
                return BuildFallbackMaterial(materialName, fallbackShader, folder);
            }

            var mat = new Material(skyShader) { name = materialName + "_Sky" };
            foreach (var side in SkySideOrder)
            {
                string sourcePath = sides[side];
                string reason;
                var loaded = BspTextureLoader.Load(sourcePath, srgb: true, failureReason: out reason);
                if (loaded == null)
                {
                    warnings.Add(
                        "Sky side image '" + sourcePath + "' (" + side + ") for env '" + envBase + "' could not be " +
                        "loaded (" + reason + "); sky surface uses the flat fallback material.");
                    return BuildFallbackMaterial(materialName, fallbackShader, folder);
                }
                string texAssetPath = folder + "/textures/" + MakeSafeFolderName(envBase.Replace('/', '_')) + "_" +
                    side + "_" + StableShortHash(sourcePath) + ".asset";
                var tex = CreateOrReplaceAsset(loaded, texAssetPath);
                mat.SetTexture(SkySidePropertyName(side), tex);
                manifestEntries.Add(ManifestEntry.For("sky-" + side, envBase, sourcePath));
            }

            return CreateOrReplaceAsset(mat, folder + "/materials/" + materialName + "_sky.mat");
        }

        private static Texture2D ResolveLightmapTexture(
            BspDocument doc, int lightmapIndex, string folder, string mapName, XonoticContentResolver resolver,
            List<string> warnings, List<ManifestEntry> manifestEntries)
        {
            if (doc.Lightmaps != null && lightmapIndex < doc.Lightmaps.Length)
            {
                var block = doc.Lightmaps[lightmapIndex];
                var internalTex = BspTextureLoader.LoadInternalLightmap(block, srgb: true, debugName: "lm_internal_" + lightmapIndex);
                if (internalTex != null)
                {
                    string assetPath = folder + "/textures/lm_internal_" + lightmapIndex + ".asset";
                    manifestEntries.Add(ManifestEntry.ForInternal("lightmap-internal", lightmapIndex));
                    return CreateOrReplaceAsset(internalTex, assetPath);
                }
            }

            string externalPath = resolver.FindExternalLightmap(mapName, lightmapIndex);
            if (externalPath != null)
            {
                string reason;
                var loaded = BspTextureLoader.Load(externalPath, srgb: true, failureReason: out reason);
                if (loaded == null)
                {
                    warnings.Add("External lightmap '" + externalPath + "' could not be loaded (" + reason + ").");
                    return null;
                }
                string assetPath = folder + "/textures/lm_external_" + lightmapIndex + "_" + StableShortHash(externalPath) + ".asset";
                manifestEntries.Add(ManifestEntry.For("lightmap-external", "lm_" + lightmapIndex.ToString("D4"), externalPath));
                return CreateOrReplaceAsset(loaded, assetPath);
            }

            return null;
        }

        /// <summary>
        /// Cheap heuristic: a map ships deluxemaps when its external
        /// lightmap directory has an even count of lm_XXXX images (lighting,
        /// normal-detail pairs) larger than the number of distinct lightmap
        /// indices actually referenced by faces — i.e. there are "extra"
        /// images beyond the ones face data points at. Only used to emit an
        /// informational warning; it never changes which image is loaded.
        /// </summary>
        private static bool HasLikelyDeluxemaps(XonoticContentResolver resolver, string mapName)
        {
            for (int i = 1; i < 64; i += 2)
            {
                if (resolver.FindExternalLightmap(mapName, i) != null) return true;
            }
            return false;
        }

        // --------------------------------------------------------------
        // Entities: spawn markers + unsupported-class reporting
        // --------------------------------------------------------------

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
                warnings.Add("Entity class '" + c + "' present in map but not instantiated by this import pass (see BspGameplayImporter for the trigger_push/trigger_teleport/trigger_hurt subset that is; movers/items remain out of scope).");
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

        /// <summary>Short, deterministic (content-independent, path-dependent) suffix so two different source images that reduce to the same safe name don't collide/overwrite each other's generated asset.</summary>
        private static string StableShortHash(string absolutePath)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(absolutePath));
                return BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            }
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

        // --------------------------------------------------------------
        // Provenance manifest: which real upstream files this import used.
        // --------------------------------------------------------------

        [Serializable]
        private sealed class ManifestEntry
        {
            public string role;
            public string shaderOrContentPath;
            public string sourcePath;
            public string sha256;
            public long sizeBytes;

            public static ManifestEntry For(string role, string shaderOrContentPath, string absoluteSourcePath)
            {
                var entry = new ManifestEntry { role = role, shaderOrContentPath = shaderOrContentPath, sourcePath = absoluteSourcePath };
                try
                {
                    var bytes = File.ReadAllBytes(absoluteSourcePath);
                    entry.sizeBytes = bytes.LongLength;
                    using (var sha = SHA256.Create())
                        entry.sha256 = BitConverter.ToString(sha.ComputeHash(bytes), 0, 32).Replace("-", "").ToLowerInvariant();
                }
                catch (Exception)
                {
                    entry.sha256 = null;
                }
                return entry;
            }

            public static ManifestEntry ForInternal(string role, int lightmapIndex)
            {
                return new ManifestEntry
                {
                    role = role,
                    shaderOrContentPath = "internal-lump#" + lightmapIndex,
                    sourcePath = "(embedded in source .bsp LightMaps lump)",
                    sha256 = null,
                    sizeBytes = 128 * 128 * 3,
                };
            }
        }

        [Serializable]
        private sealed class ImportManifest
        {
            public string sourceBsp;
            public string generatedAtUtc;
            public ManifestEntry[] entries;
            public string[] warnings;
        }

        private static void WriteImportManifest(string safeName, string sourceName, List<ManifestEntry> entries, List<string> warnings)
        {
            var manifest = new ImportManifest
            {
                sourceBsp = sourceName,
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                entries = entries.ToArray(),
                warnings = warnings.ToArray(),
            };
            string path = GeneratedRoot + "/" + safeName + "/import-manifest.json";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(manifest, true));
                AssetDatabase.ImportAsset(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BspImportPipeline] failed to write import manifest '" + path + "': " + e.Message);
            }
        }
    }
}
