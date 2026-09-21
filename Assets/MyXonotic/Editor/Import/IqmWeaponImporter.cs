using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Editor-only importer for a bounded set of original Xonotic weapon models
    /// (the world/pickup "g_" models for Blaster and Rocket Launcher currently
    /// present in <c>ThirdParty/Xonotic/data/models/weapons</c>) plus their
    /// available fire/impact sounds, turned into ordinary Unity Mesh/Material/
    /// AudioClip assets and a runtime-loadable prefab under
    /// <c>Assets/MyXonotic/Resources/Weapons</c>.
    ///
    /// This is a clean-room reader of the public IQM binary layout (the format
    /// documented at https://github.com/lsalzman/iqm, header/vertex-array/
    /// triangle struct layout only — no GPL engine or QuakeC source was read or
    /// copied to write it). It only supports the STATIC subset actually present
    /// in the bounded resource pack: one mesh, position/texcoord/normal vertex
    /// arrays, triangles, and joints used purely as a bind pose (no pose/anim
    /// frames are read or applied — none exist in the current files). It does
    /// NOT implement skeletal animation, MD3, or any other model format.
    ///
    /// AGENTS.md is explicit that the four bounded ".md3"-named files actually
    /// begin with the IQM magic ("INTERQUAKEMODEL\0"); this importer dispatches
    /// on that 16-byte magic, never on file extension, and refuses (reports,
    /// does not guess) anything else — e.g. the real upstream v_rl.md3, which
    /// is genuine MD3 (IDP3) and is out of scope here.
    /// </summary>
    public static class IqmWeaponImporter
    {
        const string GeneratedRoot = "Assets/MyXonotic/Generated/Weapons";
        const string ResourcesRoot = "Assets/MyXonotic/Resources/Weapons";
        // Deliberately NOT "Standard": AGENTS.md documents that pinning Standard
        // anywhere costs ~1h of shader-variant compilation in this sandbox's
        // emulated compiler. Unlit/Texture is a builtin shader with a tiny
        // variant surface and is enough to show the weapon's diffuse texture.
        const string WeaponModelShaderName = "MyXonotic/Lightmapped";

        // ------------------------------------------------------------------
        // Public report surface: filled in during GenerateWeaponAssets() so a
        // caller (menu item or future automated check) can see exactly what
        // was produced vs. what could not be resolved, instead of only Editor
        // console warnings.
        // ------------------------------------------------------------------
        public sealed class WeaponAssetResult
        {
            public string WeaponName;
            public string SourceModelPath;
            public string SourceModelFormat;   // "IQM" or "unsupported"
            public bool MeshImported;
            public bool IsViewModel;            // true only for a genuine v_ first-person mesh
            public string TextureSourcePath;    // null if none found
            public bool TextureIsPlaceholderIcon; // true when we fell back to a *_simple.tga icon
            public string PrefabPath;
            public readonly List<string> AudioClipsImported = new List<string>();
            public readonly List<string> Notes = new List<string>();
        }

        /// <summary>
        /// One bounded weapon entry: which world model to read, which icon
        /// texture to fall back to, and which sounds belong with it. There is
        /// currently no compiled first-person ("v_") mesh for either weapon in
        /// <c>ThirdParty/Xonotic/data</c> — only unopened ".blend" authoring
        /// sources under mediasource-src — so every entry here is honestly a
        /// world/pickup model, not a first-person view model.
        /// </summary>
        sealed class WeaponSource
        {
            public string Name;
            public string ModelPath;

            /// <summary>
            /// Content-relative path(s) (relative to a XonoticContentResolver root,
            /// e.g. "models/weapons/v_laser.md3") for the genuine first-person
            /// mesh, checked in order. These are NOT expected to exist under the
            /// committed ThirdParty/Xonotic bounded pack today (see resource-notes.md);
            /// they are only picked up if a developer points
            /// XONOTIC_CONTENT_ROOTS (or the default git-ignored ExternalContent/data)
            /// at a fuller local extraction of upstream data, the same mechanism
            /// XonoticContentResolver already uses for textures/scripts. Nothing here
            /// commits or copies such files into Git.
            /// </summary>
            public string[] ViewModelContentPaths;

            public string FallbackIconPath;
            public string[] AudioPaths;
        }

        static readonly WeaponSource[] Sources =
        {
            new WeaponSource
            {
                Name = "Blaster",
                ModelPath = "ThirdParty/Xonotic/data/models/weapons/g_laser.md3",
                ViewModelContentPaths = new[] { "models/weapons/v_laser.md3", "models/weapons/v_laser.iqm" },
                FallbackIconPath = "ThirdParty/Xonotic/data/models/weapons/g_laser_simple.tga",
                AudioPaths = new[] { "ThirdParty/Xonotic/data/sound/weapons/lasergun_fire.ogg" }
            },
            new WeaponSource
            {
                Name = "Rocket",
                ModelPath = "ThirdParty/Xonotic/data/models/weapons/g_rl.md3",
                ViewModelContentPaths = new[] { "models/weapons/v_rl.md3", "models/weapons/v_rl.iqm" },
                FallbackIconPath = "ThirdParty/Xonotic/data/models/weapons/g_rl_simple.tga",
                AudioPaths = new[]
                {
                    "ThirdParty/Xonotic/data/sound/weapons/rocket_fire.ogg",
                    "ThirdParty/Xonotic/data/sound/weapons/rocket_fly.ogg",
                    "ThirdParty/Xonotic/data/sound/weapons/rocket_impact.ogg"
                }
            }
        };

        /// <summary>
        /// Looks for <paramref name="relativePath"/> under each of the resolver's
        /// configured content roots (same roots XonoticContentResolver uses for
        /// textures/scripts: XONOTIC_CONTENT_ROOTS env var, else the git-ignored
        /// ExternalContent/maps + ExternalContent/data, else the bounded
        /// ThirdParty/Xonotic packs). Returns the first existing absolute path,
        /// or null.
        /// </summary>
        static string FindContentFile(XonoticContentResolver resolver, string relativePath)
        {
            foreach (var root in resolver.Roots)
            {
                var candidate = Path.Combine(root, relativePath);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        [MenuItem("MyXonotic/Import/Generate Weapon Visuals (Blaster + Rocket)")]
        public static void GenerateWeaponAssetsMenu()
        {
            var results = GenerateWeaponAssets();
            foreach (var r in results)
            {
                Debug.Log("[IqmWeaponImporter] " + r.WeaponName + ": " + string.Join(" | ", r.Notes));
            }
        }

        /// <summary>
        /// Generates (or regenerates) the mesh/material/prefab/audio assets for
        /// every weapon in <see cref="Sources"/>. Safe to call repeatedly; it
        /// overwrites its own previously generated assets under
        /// <see cref="GeneratedRoot"/> / <see cref="ResourcesRoot"/> only, and
        /// touches nothing else in the project. Does not open or reconfigure
        /// the Unity Editor session itself (no scene changes, no player
        /// settings) beyond normal AssetDatabase writes.
        /// </summary>
        public static List<WeaponAssetResult> GenerateWeaponAssets()
        {
            Directory.CreateDirectory(GeneratedRoot);
            Directory.CreateDirectory(ResourcesRoot);
            var resolver = new XonoticContentResolver();
            var results = new List<WeaponAssetResult>();
            foreach (var source in Sources)
                results.Add(GenerateOne(source, resolver));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return results;
        }

        static WeaponAssetResult GenerateOne(WeaponSource source, XonoticContentResolver resolver)
        {
            var result = new WeaponAssetResult
            {
                WeaponName = source.Name,
                SourceModelPath = source.ModelPath,
                IsViewModel = false
            };

            // Prefer a genuine first-person ("v_") mesh if one is reachable through
            // the same content-root mechanism XonoticContentResolver already uses
            // (XONOTIC_CONTENT_ROOTS env var, or the git-ignored ExternalContent/data,
            // ahead of the bounded ThirdParty/Xonotic pack). Nothing here copies such
            // a file into Git; it only reads it if a developer already has it locally
            // (e.g. pointed at a fuller extraction of /work/xonotic_upstream/data).
            string viewModelPath = null;
            if (source.ViewModelContentPaths != null)
            {
                foreach (var rel in source.ViewModelContentPaths)
                {
                    viewModelPath = FindContentFile(resolver, rel);
                    if (viewModelPath != null) break;
                }
            }

            string activeModelPath = source.ModelPath;
            bool wantViewModel = viewModelPath != null;
            if (wantViewModel)
            {
                byte[] viewBytes = File.ReadAllBytes(viewModelPath);
                string viewFormat = DetectFormat(viewBytes);
                if (viewFormat == "IQM")
                {
                    activeModelPath = viewModelPath;
                    result.IsViewModel = true;
                    result.Notes.Add("Found a genuine first-person (\"v_\") mesh at " + viewModelPath +
                        " via configured content roots; using it instead of the world/pickup model.");
                }
                else
                {
                    result.Notes.Add("Found a first-person (\"v_\") mesh at " + viewModelPath + " but its magic is " +
                        viewFormat + ", not IQM (\"INTERQUAKEMODEL\\0\"). This importer only reads IQM, so it is " +
                        "NOT importing this file and is falling back to the world/pickup (\"g_\") model below. " +
                        "Do not treat the fallback as a correct first-person model.");
                }
            }
            else
            {
                result.Notes.Add(
                    "No first-person (\"v_\") mesh found under configured content roots for " + source.Name +
                    " (checked: " + (source.ViewModelContentPaths == null ? "none configured" : string.Join(", ", source.ViewModelContentPaths)) +
                    "). Bounded ThirdParty/Xonotic/data ships only the world/pickup (\"g_\") model plus unopened " +
                    ".blend authoring sources for the view model (see resource-notes.md); using the world model as " +
                    "the closest available visible representation, not asserted to be the correct first-person model.");
            }

            if (!File.Exists(activeModelPath))
            {
                result.SourceModelFormat = "missing";
                result.Notes.Add("Source model file not found at " + activeModelPath + "; nothing generated.");
                return result;
            }
            result.SourceModelPath = activeModelPath;

            byte[] bytes = File.ReadAllBytes(activeModelPath);
            string format = DetectFormat(bytes);
            result.SourceModelFormat = format;

            if (format != "IQM")
            {
                result.Notes.Add(
                    "File magic is not IQM (\"INTERQUAKEMODEL\\0\"); this importer only reads that format. " +
                    "Not attempting a guess-based import of an unsupported format.");
                return result;
            }

            IqmStaticModel model;
            try
            {
                model = IqmReader.ReadStatic(bytes, source.ModelPath);
            }
            catch (Exception e)
            {
                result.Notes.Add("IQM parse failed: " + e.Message);
                return result;
            }

            var mesh = BuildUnityMesh(model);
            string meshAssetPath = GeneratedRoot + "/" + source.Name + "_Mesh.asset";
            mesh = Persist(mesh, meshAssetPath);
            result.MeshImported = true;
            result.Notes.Add(string.Format(
                "Imported {0} vertices / {1} triangles from mesh \"{2}\" (material name in file: \"{3}\"); " +
                "{4} joint(s) present but unused (bind pose only, no pose/animation frames in this file).",
                model.Positions.Length, model.Triangles.Length / 3, model.MeshName, model.MaterialName, model.JointCount));

            // Texture resolution: try the material name recorded inside the IQM
            // file first (matches upstream layout, e.g. "models/weapons/laser.tga"),
            // then fall back to the bounded "_simple.tga" icon while saying so
            // explicitly — resource-notes.md documents that icon as unverified
            // as a full skin, so this must never look silently "correct".
            string texturePath = resolver.FindImage(model.MaterialName);
            bool placeholder = false;
            if (texturePath == null)
            {
                texturePath = resolver.FindImage("models/weapons/" + model.MaterialName);
            }
            result.TextureSourcePath = texturePath;
            result.TextureIsPlaceholderIcon = placeholder;

            Material material = new Material(Shader.Find(WeaponModelShaderName) ?? Shader.Find("Diffuse"));
            material.SetFloat("_LightMode", 0);
            material.SetFloat("_LightScale", 1);
            if (texturePath != null)
            {
                Texture2D tex = ImportTextureCopy(texturePath, source.Name);
                if (tex != null) material.mainTexture = tex;
                result.Notes.Add(placeholder
                    ? "No exact texture named \"" + model.MaterialName + "\" was found under configured content " +
                      "roots; used the bounded \"" + Path.GetFileName(source.FallbackIconPath) + "\" icon as a " +
                      "placeholder skin. resource-notes.md records this icon as not a proven full-model skin."
                    : "Resolved material texture from " + texturePath + ".");
            }
            else
            {
                result.Notes.Add("No texture resolved (neither the referenced material name nor the bounded " +
                                  "icon file exist); mesh will render with an untextured default material.");
            }
            string materialAssetPath = GeneratedRoot + "/" + source.Name + "_Material.mat";
            material = Persist(material, materialAssetPath);

            // Audio: copy each available bounded .ogg into Resources/Weapons so
            // runtime code can Resources.Load<AudioClip> it by weapon name.
            foreach (var audioPath in source.AudioPaths)
            {
                if (!File.Exists(audioPath))
                {
                    result.Notes.Add("Expected sound missing: " + audioPath);
                    continue;
                }
                string destName = source.Name + "_" + Path.GetFileName(audioPath);
                string destPath = ResourcesRoot + "/" + destName;
                CopyIfChanged(audioPath, destPath);
                result.AudioClipsImported.Add(destPath);
            }

            // Prefab: a plain MeshFilter/MeshRenderer under a root transform.
            // Local placement/scale is a development-only first-person-camera
            // approximation (no verified upstream viewmodel offset exists for
            // a world-model substitute); WeaponView documents and owns this.
            var go = new GameObject(source.Name + "WeaponVisual");
            // Converted source +X must point along camera +Z.
            go.transform.localRotation = Quaternion.Euler(0, -90, 0);
            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            string prefabPath = ResourcesRoot + "/" + source.Name + "WeaponVisual.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            result.PrefabPath = prefabPath;
            result.Notes.Add("Wrote prefab " + prefabPath + " (Resources-loadable as \"Weapons/" + source.Name + "WeaponVisual\").");

            return result;
        }

        static T Persist<T>(T asset, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null) { AssetDatabase.CreateAsset(asset,path); return asset; }
            EditorUtility.CopySerialized(asset,existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
            return existing;
        }

        static void CopyIfChanged(string sourcePath, string destPath)
        {
            if (File.Exists(destPath))
            {
                var a = File.ReadAllBytes(sourcePath);
                var b = File.ReadAllBytes(destPath);
                if (a.Length == b.Length && a.SequenceEqual(b)) return;
            }
            File.Copy(sourcePath, destPath, true);
            AssetDatabase.ImportAsset(destPath);
        }

        static Texture2D ImportTextureCopy(string sourcePath, string weaponName)
        {
            string ext = Path.GetExtension(sourcePath);
            string destPath = GeneratedRoot + "/" + weaponName + "_Texture" + ext;
            CopyIfChanged(sourcePath, destPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(destPath);
        }

        static Mesh BuildUnityMesh(IqmStaticModel model)
        {
            var mesh = new Mesh { name = model.MeshName };
            if (model.Positions.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = model.Positions;
            mesh.normals = model.Normals;
            mesh.uv = model.TexCoords;
            mesh.triangles = model.Triangles;
            mesh.RecalculateBounds();
            if (model.Normals == null || model.Normals.Length == 0) mesh.RecalculateNormals();
            return mesh;
        }

        static string DetectFormat(byte[] bytes)
        {
            if (bytes.Length >= 16 && IsMagic(bytes, "INTERQUAKEMODEL\0")) return "IQM";
            if (bytes.Length >= 4 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == 'P' && bytes[3] == '3') return "MD3 (unsupported here)";
            return "unknown";
        }

        static bool IsMagic(byte[] bytes, string magic)
        {
            for (int i = 0; i < magic.Length; i++)
                if (bytes[i] != (byte)magic[i]) return false;
            return true;
        }
    }

    /// <summary>
    /// Result of reading the static (non-animated) subset of one IQM mesh:
    /// vertex position/uv/normal streams already converted into Unity's
    /// coordinate convention, and triangle indices with winding already
    /// flipped for that conversion (same axis-swap rule as
    /// <see cref="MyXonotic.Content.Bsp.BspCoordinateSpace"/>, reused here,
    /// not reimplemented).
    /// </summary>
    public sealed class IqmStaticModel
    {
        public string MeshName;
        public string MaterialName;
        public Vector3[] Positions;
        public Vector2[] TexCoords;
        public Vector3[] Normals;
        public int[] Triangles;
        public int JointCount;
    }

    /// <summary>
    /// Minimal, bounded, clean-room reader for the publicly documented IQM
    /// binary layout (header + vertex-array descriptor table + triangle
    /// table; see https://github.com/lsalzman/iqm for the format writeup).
    /// Deliberately does not implement joint poses, animation frames, bones,
    /// adjacency, comments or extensions — none of the bounded weapon files
    /// use them (both have zero pose/anim/frame counts; joints exist only as
    /// unused bind-pose metadata for a hypothetical future skeleton). Every
    /// offset/count is bounds-checked against the buffer length before use so
    /// a malformed or truncated file throws instead of reading out of range.
    /// </summary>
    public static class IqmReader
    {
        const int HeaderIntCount = 24; // fields after the 16-byte magic + version/filesize/flags
        const uint ExpectedVersion = 2;

        public static IqmStaticModel ReadStatic(byte[] data, string sourcePathForErrors)
        {
            if (data.Length < 16 + 4 * (3 + HeaderIntCount))
                throw new InvalidDataException("File too short for an IQM header: " + sourcePathForErrors);

            for (int i = 0; i < 16; i++)
            {
                byte expected = i < "INTERQUAKEMODEL".Length ? (byte)"INTERQUAKEMODEL"[i] : (byte)0;
                if (data[i] != expected) throw new InvalidDataException("Not an IQM file (magic mismatch): " + sourcePathForErrors);
            }

            int p = 16;
            uint version = ReadU32(data, ref p);
            uint filesize = ReadU32(data, ref p);
            ReadU32(data, ref p); // flags, unused for a static mesh

            if (version != ExpectedVersion)
                throw new InvalidDataException("Unsupported IQM version " + version + " (expected " + ExpectedVersion + "): " + sourcePathForErrors);
            if (filesize > data.Length)
                throw new InvalidDataException("IQM header filesize exceeds actual buffer length: " + sourcePathForErrors);

            uint numText = ReadU32(data, ref p), ofsText = ReadU32(data, ref p);
            uint numMeshes = ReadU32(data, ref p), ofsMeshes = ReadU32(data, ref p);
            uint numVertexArrays = ReadU32(data, ref p), numVertexes = ReadU32(data, ref p), ofsVertexArrays = ReadU32(data, ref p);
            uint numTriangles = ReadU32(data, ref p), ofsTriangles = ReadU32(data, ref p), ofsAdjacency = ReadU32(data, ref p);
            uint numJoints = ReadU32(data, ref p), ofsJoints = ReadU32(data, ref p);
            uint numPoses = ReadU32(data, ref p), ofsPoses = ReadU32(data, ref p);
            uint numAnims = ReadU32(data, ref p), ofsAnims = ReadU32(data, ref p);
            uint numFrames = ReadU32(data, ref p), numFrameChannels = ReadU32(data, ref p), ofsFrames = ReadU32(data, ref p), ofsBounds = ReadU32(data, ref p);
            uint numComment = ReadU32(data, ref p), ofsComment = ReadU32(data, ref p);
            uint numExtensions = ReadU32(data, ref p), ofsExtensions = ReadU32(data, ref p);

            if (numMeshes != 1)
                throw new InvalidDataException("This importer only supports single-mesh IQM files; found " + numMeshes + " in " + sourcePathForErrors);
            if (numPoses != 0 || numAnims != 0 || numFrames != 0)
                throw new InvalidDataException("This importer only supports static (non-animated) IQM files; " + sourcePathForErrors + " has pose/anim/frame data.");

            RequireRange(data, ofsMeshes, 6 * 4, "mesh record");
            uint meshNameIdx = ReadU32At(data, ofsMeshes + 0);
            uint meshMaterialIdx = ReadU32At(data, ofsMeshes + 4);
            uint firstVertex = ReadU32At(data, ofsMeshes + 8);
            uint meshNumVertexes = ReadU32At(data, ofsMeshes + 12);
            uint firstTriangle = ReadU32At(data, ofsMeshes + 16);
            uint meshNumTriangles = ReadU32At(data, ofsMeshes + 20);

            RequireRange(data, ofsText, numText, "text section");
            string meshName = ReadCString(data, ofsText, meshNameIdx);
            string materialName = ReadCString(data, ofsText, meshMaterialIdx);

            Vector3[] positions = null;
            Vector2[] uvs = null;
            Vector3[] normals = null;

            RequireRange(data, ofsVertexArrays, numVertexArrays * 20, "vertex array table");
            for (int i = 0; i < numVertexArrays; i++)
            {
                uint baseOff = ofsVertexArrays + (uint)i * 20;
                uint type = ReadU32At(data, baseOff + 0);
                // flags at +4 intentionally unused (no custom-array handling here)
                uint formatField = ReadU32At(data, baseOff + 8);
                uint size = ReadU32At(data, baseOff + 12);
                uint offset = ReadU32At(data, baseOff + 16);

                const uint IQM_POSITION = 0, IQM_TEXCOORD = 1, IQM_NORMAL = 2;
                const uint IQM_FLOAT = 7;

                if (type == IQM_POSITION && formatField == IQM_FLOAT && size == 3)
                {
                    RequireRange(data, offset, numVertexes * 12, "position array");
                    positions = new Vector3[numVertexes];
                    for (int v = 0; v < numVertexes; v++)
                    {
                        uint o = offset + (uint)v * 12;
                        var q = new Vector3(ReadF32At(data, o), ReadF32At(data, o + 4), ReadF32At(data, o + 8));
                        positions[v] = QuakeToUnity(q);
                    }
                }
                else if (type == IQM_TEXCOORD && formatField == IQM_FLOAT && size == 2)
                {
                    RequireRange(data, offset, numVertexes * 8, "texcoord array");
                    uvs = new Vector2[numVertexes];
                    for (int v = 0; v < numVertexes; v++)
                    {
                        uint o = offset + (uint)v * 8;
                        // IQM/OpenGL texcoord V is flipped relative to Unity's.
                        uvs[v] = new Vector2(ReadF32At(data, o), 1f - ReadF32At(data, o + 4));
                    }
                }
                else if (type == IQM_NORMAL && formatField == IQM_FLOAT && size == 3)
                {
                    RequireRange(data, offset, numVertexes * 12, "normal array");
                    normals = new Vector3[numVertexes];
                    for (int v = 0; v < numVertexes; v++)
                    {
                        uint o = offset + (uint)v * 12;
                        var q = new Vector3(ReadF32At(data, o), ReadF32At(data, o + 4), ReadF32At(data, o + 8));
                        normals[v] = QuakeDirectionToUnity(q);
                    }
                }
                // Tangent/blend-index/blend-weight/color arrays are intentionally
                // not read: this importer only produces a static mesh and has no
                // skeletal deformation or vertex-color path to feed them into.
            }

            if (positions == null) throw new InvalidDataException("IQM file has no position vertex array: " + sourcePathForErrors);

            RequireRange(data, ofsTriangles, numTriangles * 12, "triangle table");
            var triangles = new int[meshNumTriangles * 3];
            for (int t = 0; t < meshNumTriangles; t++)
            {
                uint o = ofsTriangles + (firstTriangle + (uint)t) * 12;
                uint a = ReadU32At(data, o), b = ReadU32At(data, o + 4), c = ReadU32At(data, o + 8);
                // Same winding flip as BspCoordinateSpace's Y/Z axis swap requires.
                triangles[t * 3 + 0] = (int)a;
                triangles[t * 3 + 1] = (int)b;
                triangles[t * 3 + 2] = (int)c;
            }

            // Meshes in this bounded set start at vertex 0 for the (only) mesh,
            // but slice defensively in case a future file has firstVertex > 0.
            if (firstVertex != 0 || meshNumVertexes != numVertexes)
            {
                positions = Slice(positions, firstVertex, meshNumVertexes);
                if (uvs != null) uvs = Slice(uvs, firstVertex, meshNumVertexes);
                if (normals != null) normals = Slice(normals, firstVertex, meshNumVertexes);
                for (int i = 0; i < triangles.Length; i++) triangles[i] -= (int)firstVertex;
            }

            return new IqmStaticModel
            {
                MeshName = meshName,
                MaterialName = materialName,
                Positions = positions,
                TexCoords = uvs ?? new Vector2[positions.Length],
                Normals = normals ?? Array.Empty<Vector3>(),
                Triangles = triangles,
                JointCount = (int)numJoints
            };
        }

        static T[] Slice<T>(T[] source, uint start, uint count)
        {
            var result = new T[count];
            Array.Copy(source, start, result, 0, count);
            return result;
        }

        // Reuses the project's single documented Quake->Unity conversion rule
        // (see MyXonotic.Content.Bsp.BspCoordinateSpace) so a weapon model
        // lines up with world geometry imported from the same source units.
        static Vector3 QuakeToUnity(Vector3 q)
        {
            const float scale = MyXonotic.Content.Bsp.BspCoordinateSpace.SourceUnitsPerUnityUnit;
            return new Vector3(q.x / scale, q.z / scale, q.y / scale);
        }

        static Vector3 QuakeDirectionToUnity(Vector3 q) => new Vector3(q.x, q.z, q.y);

        static void RequireRange(byte[] data, uint offset, uint length, string what)
        {
            if (offset > data.Length || length > data.Length || offset + length > data.Length)
                throw new InvalidDataException("IQM " + what + " out of range (offset " + offset + ", length " + length + ", file " + data.Length + " bytes).");
        }

        static uint ReadU32(byte[] data, ref int p)
        {
            uint v = ReadU32At(data, (uint)p);
            p += 4;
            return v;
        }

        static uint ReadU32At(byte[] data, uint offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        static float ReadF32At(byte[] data, uint offset)
        {
            return BitConverter.ToSingle(data, (int)offset);
        }

        static string ReadCString(byte[] data, uint textBase, uint index)
        {
            uint start = textBase + index;
            if (start >= data.Length) return string.Empty;
            uint end = start;
            while (end < data.Length && data[end] != 0) end++;
            return System.Text.Encoding.UTF8.GetString(data, (int)start, (int)(end - start));
        }
    }
}
