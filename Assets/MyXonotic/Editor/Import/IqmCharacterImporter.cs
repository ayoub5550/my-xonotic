using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Imports the original Xonotic player models (models/player/*.iqm) as
    /// STATIC meshes posed on one frame of their "idle" animation, so bots
    /// and the third-person player body look like Xonotic characters instead
    /// of capsules. This is a clean-room reader of the public IQM layout
    /// (joints / poses / frames; https://github.com/lsalzman/iqm): the frame
    /// pose is evaluated on the CPU at import time and baked into vertices.
    /// No runtime skeletal animation yet — that is a later gate.
    ///
    /// Output goes to Assets/MyXonotic/Generated/Resources/Characters so the
    /// runtime can Resources.Load it (see <c>MyXonotic.CharacterModels</c>).
    /// </summary>
    public static class IqmCharacterImporter
    {
        public const string ResourcesFolder = "Assets/MyXonotic/Generated/Resources/Characters";
        const string ShaderName = "MyXonotic/Lightmapped";

        /// <summary>Official 0.8.6 player models (without LOD variants).</summary>
        public static readonly string[] OfficialModels =
        {
            "erebus", "gak", "gakmasked", "ignis", "ignismasked", "megaerebus",
            "nyx", "pyria", "seraphina", "seraphinamasked", "umbra"
        };

        public sealed class Result
        {
            public string Name;
            public string SourcePath;
            public bool Ok;
            public string Error;
            public int Vertices;
            public int Triangles;
            public int Joints;
            public string PoseAnim;
            public int PoseFrame;
            public readonly List<string> Materials = new List<string>();
            public readonly List<string> Notes = new List<string>();
        }

        /// <summary>Import every official model found in the content roots; returns one result per name.</summary>
        public static List<Result> ImportAll(XonoticContentResolver resolver, List<string> warnings)
        {
            var results = new List<Result>();
            foreach (var name in OfficialModels)
            {
                var r = ImportOne(name, resolver);
                results.Add(r);
                if (!r.Ok) warnings.Add("Character '" + name + "': " + r.Error);
            }
            return results;
        }

        public static Result ImportOne(string name, XonoticContentResolver resolver)
        {
            var result = new Result { Name = name };
            string path = resolver.FindFile("models/player/" + name + ".iqm");
            result.SourcePath = path;
            if (path == null) { result.Error = "models/player/" + name + ".iqm not found in content roots."; return result; }
            try
            {
                byte[] data = File.ReadAllBytes(path);
                var doc = IqmSkinnedDocument.Read(data, path);
                int frame = doc.FindAnimStart("idle", out string animName);
                result.PoseAnim = animName;
                result.PoseFrame = frame;
                Vector3[] positions, normals;
                doc.SkinFrame(frame, out positions, out normals);
                result.Joints = doc.JointCount;

                EnsureFolders();
                var mesh = new Mesh { name = name };
                if (positions.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = positions;
                mesh.normals = normals;
                mesh.uv = doc.TexCoords;
                mesh.subMeshCount = doc.Meshes.Length;
                var mats = new Material[doc.Meshes.Length];
                int triCount = 0;
                for (int m = 0; m < doc.Meshes.Length; m++)
                {
                    var im = doc.Meshes[m];
                    mesh.SetTriangles(im.Triangles, m);
                    triCount += im.Triangles.Length / 3;
                    mats[m] = BuildMaterial(name, m, im.MaterialName, resolver, result);
                }
                mesh.RecalculateBounds();
                mesh = Persist(mesh, ResourcesFolder + "/" + name + "_Mesh.asset");
                result.Vertices = positions.Length;
                result.Triangles = triCount;
                result.Ok = true;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        static Material BuildMaterial(string name, int index, string materialName, XonoticContentResolver resolver, Result result)
        {
            var mat = new Material(Shader.Find(ShaderName) ?? Shader.Find("Diffuse")) { name = name + "_" + index };
            mat.SetFloat("_LightMode", 0);
            mat.SetFloat("_LightScale", 1);
            string tex = null;
            if (!string.IsNullOrEmpty(materialName))
            {
                tex = resolver.FindImage(materialName)
                      ?? resolver.FindImage("textures/" + materialName)
                      ?? resolver.FindImage("models/player/" + materialName);
                // megaerebus references "erebus_blender": the shipped skin is erebus.
                if (tex == null && materialName.EndsWith("_blender", StringComparison.OrdinalIgnoreCase))
                    tex = resolver.FindImage(materialName.Substring(0, materialName.Length - "_blender".Length));
            }
            if (tex != null)
            {
                string reason;
                var decoded = BspTextureLoader.Load(tex, srgb: true, failureReason: out reason);
                if (decoded != null)
                {
                    decoded.name = name + "_" + index;
                    var final = ImportedTexturePolicy.Finalize(decoded, repeat: false);
                    mat.mainTexture = Persist(final, ResourcesFolder + "/" + name + "_Texture_" + index + ".asset");
                    result.Materials.Add(materialName + " <- " + tex);
                }
                else
                {
                    result.Notes.Add("material '" + materialName + "': image '" + tex + "' failed to decode (" + reason + "); untextured.");
                }
            }
            else
            {
                result.Notes.Add("material '" + materialName + "': no image resolved (decode dds/" + materialName + ".dds with tools/content/prepare_unity_textures.py); untextured.");
            }
            return Persist(mat, ResourcesFolder + "/" + name + "_Material_" + index + ".mat");
        }

        static void EnsureFolders()
        {
            string[] parts = ResourcesFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
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
    }

    /// <summary>
    /// IQM with joints/poses/frames: enough to evaluate one animation frame
    /// and skin vertices on the CPU. Raw (Quake) space internally; output is
    /// converted with the project's single Quake→Unity rule.
    /// </summary>
    public sealed class IqmSkinnedDocument
    {
        public sealed class MeshInfo { public string Name; public string MaterialName; public int[] Triangles; }
        sealed class Joint { public string Name; public int Parent; public Vector3 T; public Quaternion R; public Vector3 S; }
        sealed class Pose { public int Parent; public uint Mask; public float[] Offset = new float[10]; public float[] Scale = new float[10]; }
        sealed class Anim { public string Name; public uint First, Count; }

        public MeshInfo[] Meshes;
        public Vector2[] TexCoords;
        public int JointCount => _joints.Length;

        Vector3[] _rawPositions;
        Vector3[] _rawNormals;
        byte[][] _blendIndex;
        float[][] _blendWeight;
        Joint[] _joints;
        Pose[] _poses;
        Anim[] _anims;
        ushort[] _frameData;
        uint _numFrames, _numFrameChannels;

        public static IqmSkinnedDocument Read(byte[] d, string src)
        {
            const string magic = "INTERQUAKEMODEL";
            if (d.Length < 124) throw new InvalidDataException("too short for IQM: " + src);
            for (int i = 0; i < magic.Length; i++) if (d[i] != magic[i]) throw new InvalidDataException("not IQM: " + src);
            uint[] h = new uint[27];
            for (int i = 0; i < 27; i++) h[i] = BitConverter.ToUInt32(d, 16 + i * 4);
            uint numText = h[3], ofsText = h[4], numMeshes = h[5], ofsMeshes = h[6];
            uint numVA = h[7], numVerts = h[8], ofsVA = h[9], numTris = h[10], ofsTris = h[11];
            uint numJoints = h[13], ofsJoints = h[14], numPoses = h[15], ofsPoses = h[16];
            uint numAnims = h[17], ofsAnims = h[18], numFrames = h[19], numFrameChannels = h[20], ofsFrames = h[21];
            if (h[0] != 2) throw new InvalidDataException("IQM version " + h[0] + " unsupported: " + src);
            Func<uint, string> cstr = idx =>
            {
                uint p = ofsText + idx; int e = (int)p;
                while (e < d.Length && d[e] != 0) e++;
                return System.Text.Encoding.UTF8.GetString(d, (int)p, e - (int)p);
            };
            var doc = new IqmSkinnedDocument();

            // vertex arrays
            doc._rawPositions = new Vector3[numVerts];
            doc._rawNormals = new Vector3[numVerts];
            doc.TexCoords = new Vector2[numVerts];
            doc._blendIndex = new byte[numVerts][];
            doc._blendWeight = new float[numVerts][];
            bool hasBlend = false;
            for (uint i = 0; i < numVA; i++)
            {
                uint b = ofsVA + i * 20;
                uint type = BitConverter.ToUInt32(d, (int)b), fmt = BitConverter.ToUInt32(d, (int)b + 8), size = BitConverter.ToUInt32(d, (int)b + 12), off = BitConverter.ToUInt32(d, (int)b + 16);
                for (uint v = 0; v < numVerts; v++)
                {
                    if (type == 0 && fmt == 7 && size == 3) doc._rawPositions[v] = new Vector3(F(d, off + v * 12), F(d, off + v * 12 + 4), F(d, off + v * 12 + 8));
                    else if (type == 1 && fmt == 7 && size == 2) doc.TexCoords[v] = new Vector2(F(d, off + v * 8), 1f - F(d, off + v * 8 + 4));
                    else if (type == 2 && fmt == 7 && size == 3) doc._rawNormals[v] = new Vector3(F(d, off + v * 12), F(d, off + v * 12 + 4), F(d, off + v * 12 + 8));
                    else if (type == 4 && fmt == 1 && size == 4) { doc._blendIndex[v] = new[] { d[off + v * 4], d[off + v * 4 + 1], d[off + v * 4 + 2], d[off + v * 4 + 3] }; hasBlend = true; }
                    else if (type == 5 && fmt == 1 && size == 4) doc._blendWeight[v] = new[] { d[off + v * 4] / 255f, d[off + v * 4 + 1] / 255f, d[off + v * 4 + 2] / 255f, d[off + v * 4 + 3] / 255f };
                }
            }
            if (!hasBlend) throw new InvalidDataException("IQM has no blend indexes (not a skinned model): " + src);

            doc.Meshes = new MeshInfo[numMeshes];
            for (uint m = 0; m < numMeshes; m++)
            {
                uint r = ofsMeshes + m * 24;
                uint nameIdx = BitConverter.ToUInt32(d, (int)r), matIdx = BitConverter.ToUInt32(d, (int)r + 4);
                uint firstTri = BitConverter.ToUInt32(d, (int)r + 16), nTri = BitConverter.ToUInt32(d, (int)r + 20);
                var tris = new int[nTri * 3];
                for (uint t = 0; t < nTri; t++)
                {
                    uint o = ofsTris + (firstTri + t) * 12;
                    tris[t * 3] = (int)BitConverter.ToUInt32(d, (int)o);
                    tris[t * 3 + 1] = (int)BitConverter.ToUInt32(d, (int)o + 4);
                    tris[t * 3 + 2] = (int)BitConverter.ToUInt32(d, (int)o + 8);
                }
                doc.Meshes[m] = new MeshInfo { Name = cstr(nameIdx), MaterialName = cstr(matIdx), Triangles = tris };
            }

            doc._joints = new Joint[numJoints];
            for (uint j = 0; j < numJoints; j++)
            {
                uint r = ofsJoints + j * 48;
                doc._joints[j] = new Joint
                {
                    Name = cstr(BitConverter.ToUInt32(d, (int)r)),
                    Parent = BitConverter.ToInt32(d, (int)r + 4),
                    T = new Vector3(F(d, r + 8), F(d, r + 12), F(d, r + 16)),
                    R = new Quaternion(F(d, r + 20), F(d, r + 24), F(d, r + 28), F(d, r + 32)),
                    S = new Vector3(F(d, r + 36), F(d, r + 40), F(d, r + 44)),
                };
            }
            doc._poses = new Pose[numPoses];
            for (uint p = 0; p < numPoses; p++)
            {
                uint r = ofsPoses + p * 88;
                var pose = new Pose { Parent = BitConverter.ToInt32(d, (int)r), Mask = BitConverter.ToUInt32(d, (int)r + 4) };
                for (int c = 0; c < 10; c++) { pose.Offset[c] = F(d, r + 8 + (uint)c * 4); pose.Scale[c] = F(d, r + 48 + (uint)c * 4); }
                doc._poses[p] = pose;
            }
            doc._anims = new Anim[numAnims];
            for (uint a = 0; a < numAnims; a++)
            {
                uint r = ofsAnims + a * 20;
                doc._anims[a] = new Anim { Name = cstr(BitConverter.ToUInt32(d, (int)r)), First = BitConverter.ToUInt32(d, (int)r + 4), Count = BitConverter.ToUInt32(d, (int)r + 8) };
            }
            doc._numFrames = numFrames; doc._numFrameChannels = numFrameChannels;
            doc._frameData = new ushort[numFrames * numFrameChannels];
            for (uint i = 0; i < doc._frameData.Length; i++) doc._frameData[i] = BitConverter.ToUInt16(d, (int)(ofsFrames + i * 2));
            return doc;
        }

        static float F(byte[] d, uint o) => BitConverter.ToSingle(d, (int)o);

        /// <summary>First frame of the named animation; falls back to frame 0 (bind pose) when absent.</summary>
        public int FindAnimStart(string name, out string used)
        {
            foreach (var a in _anims) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) { used = a.Name; return (int)a.First; }
            used = _anims.Length > 0 ? _anims[0].Name : "(bind pose)";
            return _anims.Length > 0 ? (int)_anims[0].First : -1;
        }

        static Matrix4x4 Trs(Vector3 t, Quaternion r, Vector3 s)
        {
            if (r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w < 1e-8f) r = Quaternion.identity;
            return Matrix4x4.TRS(t, r.normalized, s);
        }

        /// <summary>Skin every vertex at <paramref name="frame"/> (-1 = bind pose) and convert to Unity space.</summary>
        public void SkinFrame(int frame, out Vector3[] positions, out Vector3[] normals)
        {
            int n = _joints.Length;
            var baseWorld = new Matrix4x4[n];
            var invBase = new Matrix4x4[n];
            for (int j = 0; j < n; j++)
            {
                var local = Trs(_joints[j].T, _joints[j].R, _joints[j].S);
                baseWorld[j] = _joints[j].Parent >= 0 ? baseWorld[_joints[j].Parent] * local : local;
                invBase[j] = baseWorld[j].inverse;
            }

            var skin = new Matrix4x4[n];
            if (frame < 0 || frame >= _numFrames || _poses.Length != n)
            {
                for (int j = 0; j < n; j++) skin[j] = Matrix4x4.identity;
            }
            else
            {
                var frameWorld = new Matrix4x4[n];
                uint fp = (uint)frame * _numFrameChannels;
                for (int j = 0; j < n; j++)
                {
                    var p = _poses[j];
                    var ch = new float[10];
                    for (int c = 0; c < 10; c++)
                    {
                        ch[c] = p.Offset[c];
                        if ((p.Mask & (1u << c)) != 0) ch[c] += _frameData[fp++] * p.Scale[c];
                    }
                    var local = Trs(new Vector3(ch[0], ch[1], ch[2]), new Quaternion(ch[3], ch[4], ch[5], ch[6]), new Vector3(ch[7], ch[8], ch[9]));
                    frameWorld[j] = p.Parent >= 0 ? frameWorld[p.Parent] * local : local;
                    skin[j] = frameWorld[j] * invBase[j];
                }
            }

            int vcount = _rawPositions.Length;
            positions = new Vector3[vcount];
            normals = new Vector3[vcount];
            for (int v = 0; v < vcount; v++)
            {
                var idx = _blendIndex[v];
                var w = _blendWeight[v] ?? new[] { 1f, 0f, 0f, 0f };
                Vector3 pos = Vector3.zero, nrm = Vector3.zero;
                float total = 0f;
                for (int k = 0; k < 4; k++)
                {
                    if (w[k] <= 0f) continue;
                    int j = idx[k];
                    if (j >= n) continue;
                    pos += skin[j].MultiplyPoint3x4(_rawPositions[v]) * w[k];
                    nrm += skin[j].MultiplyVector(_rawNormals[v]) * w[k];
                    total += w[k];
                }
                if (total <= 0f) { pos = _rawPositions[v]; nrm = _rawNormals[v]; }
                const float s = MyXonotic.Content.Bsp.BspCoordinateSpace.SourceUnitsPerUnityUnit;
                positions[v] = new Vector3(pos.x / s, pos.z / s, pos.y / s);
                var un = new Vector3(nrm.x, nrm.z, nrm.y);
                normals[v] = un.sqrMagnitude > 1e-10f ? un.normalized : Vector3.up;
            }
        }
    }
}
