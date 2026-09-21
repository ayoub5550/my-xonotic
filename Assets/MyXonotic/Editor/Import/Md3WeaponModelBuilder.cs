using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using MyXonotic.Content.Md3;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Editor-only glue that turns a parsed <see cref="Md3StaticModel"/> (see
    /// <c>Assets/MyXonotic/Runtime/Content/Md3/Md3Reader.cs</c> for the actual
    /// binary parser, which is independent of UnityEngine/UnityEditor and
    /// standalone-testable) into an ordinary Unity Mesh with one submesh per
    /// MD3 surface, plus one Material per submesh with its diffuse texture
    /// resolved through <see cref="XonoticContentResolver"/> — reusing the
    /// SAME shader-script-aware resolution <c>BspImportPipeline</c> already
    /// uses for BSP world materials (<c>ResolveDiffuse</c>: literal texture
    /// file first, else the shader script's first non-lightmap stage map,
    /// else its qer_editorimage), not a bare filename guess. This matters for
    /// real Xonotic content: an MD3 surface's embedded "shader name" (e.g.
    /// "RL") is a Q3-style material/script name, not a texture filename —
    /// the actual skin (e.g. textures/rl_new.tga) is declared inside
    /// scripts/rl.shader, exactly like a BSP face shader. Texture bytes are
    /// decoded with <see cref="BspTextureLoader"/> (same TGA/PNG/JPG decode
    /// path as BSP; DDS is reported as an explicit unsupported-format
    /// failure reason, never silently skipped) rather than a raw file copy.
    ///
    /// Static-first, matching <see cref="Md3StaticModel"/>: only each
    /// surface's frame-0 geometry is used; there is no per-frame vertex
    /// animation playback here, no tag/attachment support, and no claim that
    /// this reproduces original weapon view-model animation.
    /// </summary>
    public static class Md3WeaponModelBuilder
    {
        /// <summary>
        /// Structured, machine-readable provenance for one surface's texture
        /// resolution attempt — the same information <see cref="BuildResult.Notes"/>
        /// describes in prose, kept here too so a caller (e.g. a weapon
        /// manifest writer) doesn't have to parse English sentences.
        /// </summary>
        public sealed class SurfaceTextureProvenance
        {
            public string SurfaceName;
            public string ShaderName;          // embedded MD3 shader/material name, e.g. "RL"; null if the surface declared none
            public string MatchedScriptName;   // non-null when a scripts/*.shader definition was matched (may differ in case from ShaderName)
            public string ResolvedTexturePath; // absolute path to the actual image file used, or null if unresolved
            public bool Resolved;
            public string FailureReason;       // human-readable reason when Resolved is false or the image failed to decode
        }

        public sealed class BuildResult
        {
            public Mesh Mesh;
            public Material[] Materials;
            public readonly List<SurfaceTextureProvenance> Textures = new List<SurfaceTextureProvenance>();
            public readonly List<string> Notes = new List<string>();
        }

        /// <summary>
        /// Builds (or overwrites) the generated Mesh/Material assets for one
        /// MD3 model under <paramref name="generatedRoot"/>, named
        /// "&lt;weaponName&gt;_Mesh.asset" / "&lt;weaponName&gt;_Material_&lt;n&gt;.mat".
        /// Safe to call repeatedly.
        /// </summary>
        public static BuildResult Build(Md3StaticModel model, XonoticContentResolver resolver, string weaponName,
            string generatedRoot, string preferredShaderName)
        {
            var result = new BuildResult();
            int surfaceCount = model.Surfaces.Length;

            var mesh = new Mesh { name = string.IsNullOrEmpty(model.Name) ? weaponName : model.Name };
            var combinedPositions = new List<Vector3>();
            var combinedNormals = new List<Vector3>();
            var combinedUvs = new List<Vector2>();
            var submeshTriangles = new int[surfaceCount][];
            var materials = new Material[surfaceCount];

            int vertexBase = 0;
            for (int s = 0; s < surfaceCount; s++)
            {
                var surf = model.Surfaces[s];
                foreach (var p in surf.Positions) combinedPositions.Add(new Vector3(p.X, p.Y, p.Z));
                foreach (var n in surf.Normals) combinedNormals.Add(new Vector3(n.X, n.Y, n.Z));
                foreach (var uv in surf.TexCoords) combinedUvs.Add(new Vector2(uv.X, uv.Y));

                var tris = new int[surf.Triangles.Length];
                for (int i = 0; i < tris.Length; i++) tris[i] = surf.Triangles[i] + vertexBase;
                submeshTriangles[s] = tris;
                vertexBase += surf.Positions.Length;

                materials[s] = BuildOneMaterial(surf, s, resolver, weaponName, generatedRoot, preferredShaderName, result);
            }

            if (combinedPositions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(combinedPositions);
            if (combinedNormals.Count == combinedPositions.Count) mesh.SetNormals(combinedNormals);
            if (combinedUvs.Count == combinedPositions.Count) mesh.SetUVs(0, combinedUvs);
            mesh.subMeshCount = surfaceCount;
            for (int s = 0; s < surfaceCount; s++) mesh.SetTriangles(submeshTriangles[s], s);
            mesh.RecalculateBounds();
            if (combinedNormals.Count != combinedPositions.Count) mesh.RecalculateNormals();

            string meshAssetPath = generatedRoot + "/" + weaponName + "_Mesh.asset";
            mesh = PersistAsset(mesh, meshAssetPath);

            result.Mesh = mesh;
            result.Materials = materials;
            result.Notes.Add(string.Format(
                "Imported {0} surface(s) from MD3 model \"{1}\": {2} total vertices / {3} total triangles " +
                "(frame 0 / bind pose only — no vertex-animation playback implemented; tags/attachment points not read).",
                surfaceCount, model.Name, combinedPositions.Count, CountTriangles(submeshTriangles)));
            return result;
        }

        static int CountTriangles(int[][] submeshTriangles)
        {
            int total = 0;
            foreach (var t in submeshTriangles) total += t.Length / 3;
            return total;
        }

        static Material BuildOneMaterial(Md3Surface surf, int surfaceIndex, XonoticContentResolver resolver,
            string weaponName, string generatedRoot, string preferredShaderName, BuildResult result)
        {
            string shaderRefName = surf.ShaderNames.Length > 0 ? surf.ShaderNames[0] : null;
            var provenance = new SurfaceTextureProvenance { SurfaceName = surf.Name, ShaderName = shaderRefName };

            string texturePath = null;
            MaterialScript script = null;
            if (!string.IsNullOrEmpty(shaderRefName))
            {
                // Same resolution BspImportPipeline uses for a BSP face
                // shader: literal texture file first, else the shader
                // script's own first non-lightmap stage map, else its
                // qer_editorimage. An MD3 "shader name" is a Q3 material/
                // script name (e.g. "RL"), not a texture filename — going
                // straight to FindImage(shaderRefName) (the previous
                // behavior here) only worked by coincidence when a texture
                // happened to share the shader's bare name; it misses real
                // Xonotic weapons like v_rl.md3, whose actual skin
                // (textures/rl_new.tga) is declared inside scripts/rl.shader.
                texturePath = resolver.ResolveDiffuse(shaderRefName, out script);
                if (script != null) provenance.MatchedScriptName = script.Name;
                if (texturePath == null)
                {
                    // Some upstream view models name their surface after the
                    // decompiled mesh rather than the material (v_uzi) or use a
                    // different case (Hagar); try the documented aliases and the
                    // plain textures/<name> convention before giving up.
                    foreach (var candidate in AliasCandidates(shaderRefName))
                    {
                        texturePath = resolver.ResolveDiffuse(candidate, out script);
                        if (texturePath != null)
                        {
                            if (script != null) provenance.MatchedScriptName = script.Name;
                            result.Notes.Add(string.Format("Surface \"{0}\": shader name \"{1}\" resolved via alias \"{2}\".", surf.Name, shaderRefName, candidate));
                            break;
                        }
                    }
                }
            }

            var material = new Material(Shader.Find(preferredShaderName) ?? Shader.Find("Diffuse"));
            material.SetFloat("_LightMode", 0);
            material.SetFloat("_LightScale", 1);

            if (texturePath != null)
            {
                string failureReason;
                Texture2D decoded = BspTextureLoader.Load(texturePath, srgb: true, failureReason: out failureReason);
                if (decoded != null)
                {
                    string texAssetPath = generatedRoot + "/" + weaponName + "_Texture_" + surfaceIndex + ".asset";
                    var persistedTex = PersistAsset(decoded, texAssetPath);
                    material.mainTexture = persistedTex;
                    provenance.ResolvedTexturePath = texturePath;
                    provenance.Resolved = true;
                    result.Notes.Add(string.Format(
                        "Surface \"{0}\": resolved material texture from {1} (embedded shader name \"{2}\"{3}).",
                        surf.Name, texturePath, shaderRefName,
                        script != null ? ", matched scripts/" + script.Name + ".shader" : ""));
                }
                else
                {
                    provenance.ResolvedTexturePath = texturePath;
                    provenance.Resolved = false;
                    provenance.FailureReason = failureReason;
                    result.Notes.Add(string.Format(
                        "Surface \"{0}\": found texture path {1} for embedded shader name \"{2}\" but could not decode it ({3}); " +
                        "this submesh will render with an untextured default material.",
                        surf.Name, texturePath, shaderRefName, failureReason));
                }
            }
            else
            {
                provenance.Resolved = false;
                provenance.FailureReason = "no texture/script resolved for shader name under configured content roots";
                result.Notes.Add(string.Format(
                    "Surface \"{0}\": no texture found for embedded shader name \"{1}\" under configured content " +
                    "roots (checked a direct file match and any scripts/*.shader definition); this submesh will " +
                    "render with an untextured default material. Not substituting an unrelated texture.",
                    surf.Name, shaderRefName ?? "(none)"));
            }

            result.Textures.Add(provenance);

            string materialAssetPath = generatedRoot + "/" + weaponName + "_Material_" + surfaceIndex + ".mat";
            return PersistAsset(material, materialAssetPath);
        }

        static readonly Dictionary<string, string> SurfaceAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["v_uzi_decompiled"] = "uzi",
            ["v_mesh"] = "electro",
            ["shotgun2"] = "textures/shotgun2",
            ["shotgun_sight"] = "textures/shotgun_sight",
            ["grenadelauncher_sight"] = "textures/glsight01",
        };

        static IEnumerable<string> AliasCandidates(string name)
        {
            if (SurfaceAliases.TryGetValue(name, out var alias)) yield return alias;
            yield return name.ToLowerInvariant();
            yield return "textures/" + name;
            yield return "textures/" + name.ToLowerInvariant();
            yield return "models/weapons/" + name;
        }

        static T PersistAsset<T>(T asset, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        /// <summary>Lowercase-hex SHA256 of a file's bytes, or null if it can't be read. Shared with the weapon manifest writer.</summary>
        public static string Sha256OrNull(string absolutePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(absolutePath);
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(bytes), 0, 32).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
