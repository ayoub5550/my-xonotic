using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MyXonotic.Content.Bsp;
using MyXonotic.Content.Md3;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Places the original MD3 map decorations referenced by
    /// misc_gamemodel / misc_breakablemodel / misc_model entities (pipes,
    /// lamps, crates, trees, ...). Without them large parts of the official
    /// maps looked empty in dev.5. Static frame-0 geometry only (see
    /// <see cref="Md3WeaponModelBuilder"/>); OBJ models and non-MD3 formats
    /// are reported and skipped, never guessed.
    /// </summary>
    public static class BspMapModelImporter
    {
        const string GeneratedRoot = "Assets/MyXonotic/Generated/MapModels";
        const string ModelShaderName = "MyXonotic/Lightmapped";

        public static readonly HashSet<string> ModelClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "misc_gamemodel", "misc_breakablemodel", "misc_model", "misc_clientmodel"
        };

        sealed class Built { public Mesh Mesh; public Material[] Materials; public string Error; }
        static readonly Dictionary<string, Built> Cache = new Dictionary<string, Built>(StringComparer.OrdinalIgnoreCase);

        public static void ClearCache() { Cache.Clear(); }

        /// <summary>
        /// Resolve + build (cached) an original MD3 by content path, e.g.
        /// "models/items/g_h25.md3". Returns false with a reason when the
        /// file is missing or not MD3; never substitutes another model.
        /// </summary>
        public static bool TryGetModel(string modelPath, XonoticContentResolver resolver,
            out Mesh mesh, out Material[] materials, out string error)
        {
            var b = GetOrBuild(modelPath, resolver);
            mesh = b.Mesh;
            materials = b.Materials;
            error = b.Error;
            return mesh != null;
        }

        /// <summary>Returns warnings; places models under <paramref name="parent"/>. <paramref name="placed"/> receives the count.</summary>
        public static List<string> Import(BspDocument doc, Transform parent, XonoticContentResolver resolver, out int placed)
        {
            var warnings = new List<string>();
            placed = 0;
            if (doc == null || parent == null) return warnings;
            var root = new GameObject("MapModels");
            root.transform.SetParent(parent, false);

            foreach (var entity in doc.Entities)
            {
                string classname = entity.Get("classname") ?? "";
                if (!ModelClasses.Contains(classname)) continue;
                string modelPath = entity.Get("model");
                if (string.IsNullOrEmpty(modelPath) || modelPath[0] == '*') continue;

                var built = GetOrBuild(modelPath, resolver);
                if (built.Mesh == null)
                {
                    warnings.Add(string.Format("{0} '{1}': {2}", classname, modelPath, built.Error));
                    continue;
                }

                BspVec3 quakeOrigin;
                if (!TryParseVec3(entity.Get("origin", "0 0 0"), out quakeOrigin))
                {
                    warnings.Add(string.Format("{0} '{1}': unparsable origin; skipped.", classname, modelPath));
                    continue;
                }
                var unityOrigin = BspCoordinateSpace.QuakeToUnity(quakeOrigin);

                float pitch = 0f, yaw = 0f, roll = 0f;
                BspVec3 angles;
                if (TryParseVec3(entity.Get("angles", ""), out angles)) { pitch = angles.X; yaw = angles.Y; roll = angles.Z; }
                else
                {
                    float a;
                    if (float.TryParse(entity.Get("angle", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out a)) yaw = a;
                }
                float scale = 1f;
                float.TryParse(entity.Get("modelscale", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
                if (scale <= 0f) scale = 1f;

                var go = new GameObject(classname + "_" + Path.GetFileNameWithoutExtension(modelPath));
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(unityOrigin.X, unityOrigin.Y, unityOrigin.Z);
                // Mesh vertices are already in Unity space (x, z, y swap), so
                // quake yaw (CCW from above) becomes a negative Unity Y rotation.
                go.transform.localRotation = Quaternion.Euler(pitch, -yaw, -roll);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = built.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = built.Materials;
                if (classname != "misc_clientmodel" && entity.Get("solid", "0") != "0")
                {
                    var col = go.AddComponent<MeshCollider>();
                    col.sharedMesh = built.Mesh;
                }
                placed++;
            }
            return warnings;
        }

        static Built GetOrBuild(string modelPath, XonoticContentResolver resolver)
        {
            Built b;
            if (Cache.TryGetValue(modelPath, out b)) return b;
            b = new Built();
            Cache[modelPath] = b;

            string ext = Path.GetExtension(modelPath).ToLowerInvariant();
            string abs = resolver.FindFile(modelPath);
            if (abs == null) { b.Error = "model file not found in content roots; skipped."; return b; }
            if (ext != ".md3" && ext != ".iqm")
            {
                b.Error = "format '" + ext + "' not supported by the MD3/IQM map-model importer; skipped.";
                return b;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(abs);
                string safe = SafeName(modelPath);
                if (!AssetDatabase.IsValidFolder(GeneratedRoot))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/MyXonotic/Generated")) AssetDatabase.CreateFolder("Assets/MyXonotic", "Generated");
                    AssetDatabase.CreateFolder("Assets/MyXonotic/Generated", "MapModels");
                }
                if (Md3Reader.IsMd3(bytes))
                {
                    var model = Md3Reader.Read(bytes, abs);
                    var result = Md3WeaponModelBuilder.Build(model, resolver, safe, GeneratedRoot, ModelShaderName);
                    b.Mesh = result.Mesh;
                    b.Materials = result.Materials;
                }
                else if (IsIqm(bytes))
                {
                    // Several official ".md3"-named item files are really static IQM.
                    var iqm = IqmReader.ReadStaticAll(bytes, abs, allowAnimated: true);
                    BuildIqm(iqm, resolver, safe, b);
                }
                else
                {
                    b.Error = "file starts with neither the MD3 nor the IQM magic; skipped.";
                }
            }
            catch (Exception e)
            {
                b.Error = "failed to read/build MD3: " + e.Message;
            }
            return b;
        }

        static bool IsIqm(byte[] bytes)
        {
            const string magic = "INTERQUAKEMODEL";
            if (bytes.Length < 16) return false;
            for (int i = 0; i < magic.Length; i++) if (bytes[i] != (byte)magic[i]) return false;
            return bytes[15] == 0;
        }

        static void BuildIqm(IqmStaticModel[] meshes, XonoticContentResolver resolver, string safe, Built b)
        {
            var mesh = new Mesh { name = safe };
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>();
            var subs = new List<int[]>();
            bool allNormals = true;
            foreach (var m in meshes)
            {
                int baseIndex = pos.Count;
                pos.AddRange(m.Positions);
                uv.AddRange(m.TexCoords);
                if (m.Normals != null && m.Normals.Length == m.Positions.Length) nrm.AddRange(m.Normals); else allNormals = false;
                var tris = new int[m.Triangles.Length];
                for (int i = 0; i < tris.Length; i++) tris[i] = m.Triangles[i] + baseIndex;
                subs.Add(tris);
            }
            if (pos.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetUVs(0, uv);
            if (allNormals) mesh.SetNormals(nrm);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i);
            mesh.RecalculateBounds();
            if (!allNormals) mesh.RecalculateNormals();
            mesh = Persist(mesh, GeneratedRoot + "/" + safe + "_Mesh.asset");

            var mats = new Material[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                var model = meshes[i];
                var mat = new Material(Shader.Find(ModelShaderName) ?? Shader.Find("Diffuse")) { name = safe + "_" + i };
                mat.SetFloat("_LightMode", 0);
                mat.SetFloat("_LightScale", 1);
                string tex = null;
                if (!string.IsNullOrEmpty(model.MaterialName))
                {
                    MaterialScript script;
                    tex = resolver.ResolveDiffuse(model.MaterialName, out script)
                          ?? resolver.ResolveDiffuse(Path.ChangeExtension(model.MaterialName, null), out script)
                          ?? resolver.FindImage("textures/" + model.MaterialName)
                          ?? resolver.FindImage("models/items/" + model.MaterialName)
                          ?? resolver.FindImage("models/weapons/" + model.MaterialName);
                }
                if (tex != null)
                {
                    string reason;
                    var decoded = BspTextureLoader.Load(tex, srgb: true, failureReason: out reason);
                    if (decoded != null) mat.mainTexture = Persist(decoded, GeneratedRoot + "/" + safe + "_Texture_" + i + ".asset");
                }
                mats[i] = Persist(mat, GeneratedRoot + "/" + safe + "_Material_" + i + ".mat");
            }
            b.Mesh = mesh;
            b.Materials = mats;
        }

        static T Persist<T>(T asset, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset, path); return asset; }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        static string SafeName(string path)
        {
            var chars = path.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars);
        }

        static bool TryParseVec3(string s, out BspVec3 v)
        {
            v = new BspVec3(0, 0, 0);
            if (string.IsNullOrEmpty(s)) return false;
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;
            v = new BspVec3(x, y, z);
            return true;
        }
    }
}
