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

                // Skeletal variant: bind-pose skinned mesh + rig asset (dev.9).
                try
                {
                    BuildRigAssets(name, path, doc, mats, result);
                }
                catch (Exception rigError)
                {
                    result.Notes.Add("rig: " + rigError.Message + " (static mesh only)");
                }
                result.Ok = true;
            }
            catch (Exception e)
            {
                result.Error = e.Message;
            }
            return result;
        }

        /// <summary>
        /// Writes <c>&lt;name&gt;_Skinned.asset</c> (bind-pose mesh with bone weights and
        /// bindposes, Unity space) and <c>&lt;name&gt;_Rig.asset</c> (joints + every
        /// animation frame as local TRS, Unity space) so the runtime can animate
        /// the character with a SkinnedMeshRenderer. Frame rates / loop flags come
        /// from the model's <c>.framegroups</c> file when present.
        /// </summary>
        static void BuildRigAssets(string name, string iqmPath, IqmSkinnedDocument doc, Material[] mats, Result result)
        {
            int n = doc.JointCount;
            if (n == 0) { result.Notes.Add("rig: no joints; skipped."); return; }
            var rig = ScriptableObject.CreateInstance<CharacterRig>();
            rig.ModelName = name;
            rig.JointNames = new string[n];
            rig.JointParents = new int[n];
            rig.BindLocal = new float[n * 10];
            for (int j = 0; j < n; j++)
            {
                doc.GetJoint(j, out rig.JointNames[j], out rig.JointParents[j], out Vector3 t, out Quaternion r, out Vector3 sc);
                WriteTrs(rig.BindLocal, j * 10, ToUnityT(t), ToUnityQ(r), ToUnityS(sc));
            }
            int frames = doc.FrameCount;
            rig.FrameCount = frames;
            rig.Poses = new float[frames * n * 10];
            for (int f = 0; f < frames; f++)
                for (int j = 0; j < n; j++)
                {
                    doc.GetFramePose(f, j, out Vector3 t, out Quaternion r, out Vector3 sc);
                    WriteTrs(rig.Poses, (f * n + j) * 10, ToUnityT(t), ToUnityQ(r), ToUnityS(sc));
                }
            var groups = ReadFrameGroups(iqmPath + ".framegroups");
            var clips = new List<CharacterRig.Clip>();
            for (int a = 0; a < doc.AnimCount; a++)
            {
                doc.GetAnim(a, out string aname, out int first, out int count, out float rate, out bool loop);
                if (groups != null && a < groups.Count)
                {
                    // framegroups is authoritative for timing (IQM files ship rate 0).
                    var g = groups[a];
                    if (g.First == first) { count = g.Count; rate = g.Fps; loop = g.Loop; }
                }
                if (rate <= 0f) rate = 20f;
                clips.Add(new CharacterRig.Clip { Name = aname, FirstFrame = first, FrameCount = Mathf.Max(1, Mathf.Min(count, frames - first)), FramesPerSecond = rate, Loop = loop });
            }
            rig.Clips = clips.ToArray();

            // Bind-pose mesh in Unity space with bone weights; bindposes = inverse bind world matrices (root-local).
            Vector3[] bindPos, bindNrm;
            doc.SkinFrame(-1, out bindPos, out bindNrm);
            var skinned = new Mesh { name = name + "_Skinned" };
            if (bindPos.Length > 65535) skinned.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            skinned.vertices = bindPos;
            skinned.normals = bindNrm;
            skinned.uv = doc.TexCoords;
            skinned.subMeshCount = doc.Meshes.Length;
            for (int m = 0; m < doc.Meshes.Length; m++) skinned.SetTriangles(doc.Meshes[m].Triangles, m);
            skinned.boneWeights = doc.BuildBoneWeights();
            var world = new Matrix4x4[n];
            var bindposes = new Matrix4x4[n];
            for (int j = 0; j < n; j++)
            {
                rig.GetBind(j, out Vector3 t, out Quaternion r, out Vector3 sc);
                var local = Matrix4x4.TRS(t, r.normalized, sc);
                int p = rig.JointParents[j];
                world[j] = p >= 0 ? world[p] * local : local;
                bindposes[j] = world[j].inverse;
            }
            skinned.bindposes = bindposes;
            skinned.RecalculateBounds();
            Persist(skinned, ResourcesFolder + "/" + name + "_Skinned.asset");
            Persist(rig, ResourcesFolder + "/" + name + "_Rig.asset");
            result.Notes.Add(string.Format("rig: {0} joints, {1} frames, {2} clips ({3}).", n, frames, rig.Clips.Length,
                groups != null ? "framegroups timing" : "default 20 fps"));
        }

        public struct FrameGroup { public int First, Count; public float Fps; public bool Loop; }

        public static List<FrameGroup> ReadFrameGroups(string path)
        {
            if (!File.Exists(path)) return null;
            var list = new List<FrameGroup>();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw;
                int c = line.IndexOf("//", StringComparison.Ordinal);
                if (c >= 0) line = line.Substring(0, c);
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                int first, count; float fps; int loop = 1;
                if (!int.TryParse(parts[0], out first) || !int.TryParse(parts[1], out count)) continue;
                if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fps)) continue;
                if (parts.Length > 3) int.TryParse(parts[3], out loop);
                list.Add(new FrameGroup { First = first, Count = count, Fps = fps, Loop = loop != 0 });
            }
            return list;
        }

        public static void WriteTrs(float[] dst, int o, Vector3 t, Quaternion r, Vector3 s)
        {
            dst[o] = t.x; dst[o + 1] = t.y; dst[o + 2] = t.z;
            dst[o + 3] = r.x; dst[o + 4] = r.y; dst[o + 5] = r.z; dst[o + 6] = r.w;
            dst[o + 7] = s.x; dst[o + 8] = s.y; dst[o + 9] = s.z;
        }

        // Quake (x, y, z; units) -> Unity (x, z, y; metres). The axis swap is a
        // reflection, so a rotation (axis a, angle θ) becomes (swap(a), -θ):
        // quaternion (x, y, z, w) -> (-x, -z, -y, w). Verified numerically against
        // CPU skinning of the raw data (see docs/UNITY-DEV9.md).
        const float Units = MyXonotic.Content.Bsp.BspCoordinateSpace.SourceUnitsPerUnityUnit;
        public static Vector3 ToUnityT(Vector3 t) => new Vector3(t.x / Units, t.z / Units, t.y / Units);
        public static Quaternion ToUnityQ(Quaternion q)
        {
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-8f) return Quaternion.identity;
            return new Quaternion(-q.x, -q.z, -q.y, q.w).normalized;
        }
        public static Vector3 ToUnityS(Vector3 s) => new Vector3(s.x, s.z, s.y);

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

        public static T Persist<T>(T asset, string path) where T : UnityEngine.Object
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
        sealed class Pose { public int Parent; public uint Mask; public bool V1; public float[] Offset = new float[10]; public float[] Scale = new float[10]; }

        /// IQM v1 quaternion: xyz stored, w reconstructed as -sqrt(1 - |xyz|^2) (IQM spec).
        static Quaternion Quat3(float x, float y, float z)
        {
            float w2 = 1f - (x * x + y * y + z * z);
            return new Quaternion(x, y, z, -Mathf.Sqrt(Mathf.Max(0f, w2)));
        }
        sealed class Anim { public string Name; public uint First, Count; public float Rate; public uint Flags; }

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
            // IQM v1 (h_fireball.iqm) stores 3-component quaternions (w = -sqrt(1-|xyz|^2))
            // and 9 pose channels; v2 stores full quaternions and 10 channels.
            uint version = h[0];
            if (version != 1 && version != 2) throw new InvalidDataException("IQM version " + h[0] + " unsupported: " + src);
            bool v1 = version == 1;
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
                uint r = ofsJoints + j * (v1 ? 44u : 48u);
                Quaternion rot = v1
                    ? Quat3(F(d, r + 20), F(d, r + 24), F(d, r + 28))
                    : new Quaternion(F(d, r + 20), F(d, r + 24), F(d, r + 28), F(d, r + 32));
                uint so = v1 ? r + 32 : r + 36;
                doc._joints[j] = new Joint
                {
                    Name = cstr(BitConverter.ToUInt32(d, (int)r)),
                    Parent = BitConverter.ToInt32(d, (int)r + 4),
                    T = new Vector3(F(d, r + 8), F(d, r + 12), F(d, r + 16)),
                    R = rot,
                    S = new Vector3(F(d, so), F(d, so + 4), F(d, so + 8)),
                };
            }
            doc._poses = new Pose[numPoses];
            for (uint p = 0; p < numPoses; p++)
            {
                uint r = ofsPoses + p * (v1 ? 80u : 88u);
                var pose = new Pose { Parent = BitConverter.ToInt32(d, (int)r), Mask = BitConverter.ToUInt32(d, (int)r + 4), V1 = v1 };
                int nch = v1 ? 9 : 10;
                for (int c = 0; c < nch; c++) { pose.Offset[c] = F(d, r + 8 + (uint)c * 4); pose.Scale[c] = F(d, r + 8 + (uint)nch * 4 + (uint)c * 4); }
                doc._poses[p] = pose;
            }
            doc._anims = new Anim[numAnims];
            for (uint a = 0; a < numAnims; a++)
            {
                uint r = ofsAnims + a * 20;
                doc._anims[a] = new Anim
                {
                    Name = cstr(BitConverter.ToUInt32(d, (int)r)), First = BitConverter.ToUInt32(d, (int)r + 4), Count = BitConverter.ToUInt32(d, (int)r + 8),
                    Rate = F(d, r + 12), Flags = BitConverter.ToUInt32(d, (int)r + 16)
                };
            }
            doc._numFrames = numFrames; doc._numFrameChannels = numFrameChannels;
            doc._frameData = new ushort[numFrames * numFrameChannels];
            for (uint i = 0; i < doc._frameData.Length; i++) doc._frameData[i] = BitConverter.ToUInt16(d, (int)(ofsFrames + i * 2));
            return doc;
        }

        static float F(byte[] d, uint o) => BitConverter.ToSingle(d, (int)o);

        public int FrameCount => (int)_numFrames;
        public int AnimCount => _anims.Length;

        public void GetJoint(int j, out string name, out int parent, out Vector3 t, out Quaternion r, out Vector3 s)
        {
            var jt = _joints[j];
            name = jt.Name; parent = jt.Parent; t = jt.T; r = jt.R; s = jt.S;
        }

        public void GetAnim(int a, out string name, out int first, out int count, out float rate, out bool loop)
        {
            var an = _anims[a];
            name = an.Name; first = (int)an.First; count = (int)an.Count; rate = an.Rate; loop = (an.Flags & 1) != 0;
        }

        /// <summary>Local TRS of joint <paramref name="j"/> at <paramref name="frame"/> in raw (Quake) space.</summary>
        public void GetFramePose(int frame, int j, out Vector3 t, out Quaternion r, out Vector3 s)
        {
            if (_poses.Length != _joints.Length || frame < 0 || frame >= _numFrames)
            {
                t = _joints[j].T; r = _joints[j].R; s = _joints[j].S; return;
            }
            // Channels are packed per frame in joint order; compute this joint's offset.
            uint fp = (uint)frame * _numFrameChannels;
            for (int k = 0; k < j; k++) fp += CountBits(_poses[k].Mask);
            var p = _poses[j];
            var ch = new float[10];
            if (p.V1)
            {
                // 9 channels: t(3) q.xyz(3) s(3); w derived.
                var raw = new float[9];
                for (int c = 0; c < 9; c++)
                {
                    raw[c] = p.Offset[c];
                    if ((p.Mask & (1u << c)) != 0) raw[c] += _frameData[fp++] * p.Scale[c];
                }
                t = new Vector3(raw[0], raw[1], raw[2]);
                r = Quat3(raw[3], raw[4], raw[5]);
                s = new Vector3(raw[6], raw[7], raw[8]);
                return;
            }
            for (int c = 0; c < 10; c++)
            {
                ch[c] = p.Offset[c];
                if ((p.Mask & (1u << c)) != 0) ch[c] += _frameData[fp++] * p.Scale[c];
            }
            t = new Vector3(ch[0], ch[1], ch[2]);
            r = new Quaternion(ch[3], ch[4], ch[5], ch[6]);
            s = new Vector3(ch[7], ch[8], ch[9]);
        }

        static uint CountBits(uint v) { uint c = 0; while (v != 0) { c += v & 1; v >>= 1; } return c; }

        /// <summary>Unity BoneWeight per vertex from the IQM blend indexes/weights (normalised, top 4).</summary>
        public BoneWeight[] BuildBoneWeights()
        {
            int vcount = _rawPositions.Length;
            var bw = new BoneWeight[vcount];
            for (int v = 0; v < vcount; v++)
            {
                var idx = _blendIndex[v] ?? new byte[] { 0, 0, 0, 0 };
                var w = _blendWeight[v] ?? new[] { 1f, 0f, 0f, 0f };
                float total = w[0] + w[1] + w[2] + w[3];
                if (total <= 0f) { w = new[] { 1f, 0f, 0f, 0f }; total = 1f; }
                bw[v] = new BoneWeight
                {
                    boneIndex0 = idx[0], weight0 = w[0] / total,
                    boneIndex1 = idx[1], weight1 = w[1] / total,
                    boneIndex2 = idx[2], weight2 = w[2] / total,
                    boneIndex3 = idx[3], weight3 = w[3] / total
                };
            }
            return bw;
        }

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
