using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Reader for DarkPlaces skeletal models ("DARKPLACESMODEL", type 2). Several
    /// Xonotic first-person weapon models (<c>h_electro</c>, <c>h_crylink</c>,
    /// <c>h_gl</c>, <c>h_hagar</c>, <c>h_rl</c>, <c>h_minstanex</c>) ship in this
    /// format under an <c>.iqm</c> file name. The format is big-endian:
    /// bones (name, parent), frames (one parent-relative 3x4 matrix per bone),
    /// meshes whose vertices are stored per bone influence in bone space.
    /// Model-space vertex positions are Σ influence·(Mbone(frame0)·origin),
    /// verified against the models' extents (see docs/UNITY-DEV11.md).
    /// Raw (Quake) space internally; callers convert with the project's single
    /// Quake→Unity rule.
    /// </summary>
    public sealed class DpmDocument
    {
        public sealed class Bone { public string Name; public int Parent; public uint Flags; }
        public sealed class Influence { public Vector3 Origin; public float Weight; public Vector3 Normal; public int Bone; }
        public sealed class Vertex { public Influence[] Influences; }
        public sealed class Mesh
        {
            public string ShaderName;
            public Vertex[] Vertices;
            public Vector2[] TexCoords;
            public int[] Triangles;
        }
        public sealed class Frame
        {
            public string Name;
            /// <summary>Parent-relative bone matrices, row-major 3x4 (rotation|translation).</summary>
            public Matrix4x4[] Local;
        }

        public Bone[] Bones;
        public Mesh[] Meshes;
        public Frame[] Frames;
        public Vector3 Mins, Maxs;

        public int BoneCount => Bones.Length;
        public int FrameCount => Frames.Length;

        public static bool IsDpm(byte[] d)
        {
            const string magic = "DARKPLACESMODEL";
            if (d == null || d.Length < 16 + 60) return false;
            for (int i = 0; i < magic.Length; i++) if (d[i] != magic[i]) return false;
            return true;
        }

        public static DpmDocument Read(byte[] d, string src)
        {
            if (!IsDpm(d)) throw new InvalidDataException("not a DarkPlaces model: " + src);
            uint type = U32(d, 16);
            if (type != 2) throw new InvalidDataException("DPM type " + type + " unsupported (expected 2): " + src);
            var doc = new DpmDocument();
            doc.Mins = new Vector3(F32(d, 24), F32(d, 28), F32(d, 32));
            doc.Maxs = new Vector3(F32(d, 36), F32(d, 40), F32(d, 44));
            uint numBones = U32(d, 56), numMeshes = U32(d, 60), numFrames = U32(d, 64);
            uint ofsBones = U32(d, 68), ofsMeshes = U32(d, 72), ofsFrames = U32(d, 76);
            if (numBones == 0 || numBones > 256) throw new InvalidDataException("DPM bone count " + numBones + " out of range: " + src);
            if (numFrames == 0 || numFrames > 100000) throw new InvalidDataException("DPM frame count " + numFrames + " out of range: " + src);

            doc.Bones = new Bone[numBones];
            for (uint b = 0; b < numBones; b++)
            {
                uint o = ofsBones + b * 40;
                doc.Bones[b] = new Bone { Name = CStr(d, o, 32), Parent = (int)U32(d, o + 32), Flags = U32(d, o + 36) };
                if (doc.Bones[b].Parent >= (int)b) throw new InvalidDataException("DPM bone " + b + " has parent " + doc.Bones[b].Parent + " (must precede child): " + src);
            }

            doc.Meshes = new Mesh[numMeshes];
            for (uint m = 0; m < numMeshes; m++)
            {
                uint o = ofsMeshes + m * 56;
                var mesh = new Mesh { ShaderName = CStr(d, o, 32) };
                uint numVerts = U32(d, o + 32), numTris = U32(d, o + 36);
                uint ofsVerts = U32(d, o + 40), ofsTex = U32(d, o + 44), ofsIdx = U32(d, o + 48);
                mesh.Vertices = new Vertex[numVerts];
                uint p = ofsVerts;
                for (uint v = 0; v < numVerts; v++)
                {
                    uint n = U32(d, p); p += 4;
                    if (n > 64) throw new InvalidDataException("DPM vertex " + v + " has " + n + " influences: " + src);
                    var inf = new Influence[n];
                    for (uint k = 0; k < n; k++)
                    {
                        inf[k] = new Influence
                        {
                            Origin = new Vector3(F32(d, p), F32(d, p + 4), F32(d, p + 8)),
                            Weight = F32(d, p + 12),
                            Normal = new Vector3(F32(d, p + 16), F32(d, p + 20), F32(d, p + 24)),
                            Bone = (int)U32(d, p + 28)
                        };
                        if (inf[k].Bone < 0 || inf[k].Bone >= numBones) throw new InvalidDataException("DPM influence bone out of range: " + src);
                        p += 32;
                    }
                    mesh.Vertices[v] = new Vertex { Influences = inf };
                }
                mesh.TexCoords = new Vector2[numVerts];
                for (uint v = 0; v < numVerts; v++)
                    mesh.TexCoords[v] = new Vector2(F32(d, ofsTex + v * 8), 1f - F32(d, ofsTex + v * 8 + 4));
                mesh.Triangles = new int[numTris * 3];
                for (uint i = 0; i < numTris * 3; i++)
                {
                    int idx = (int)U32(d, ofsIdx + i * 4);
                    if (idx < 0 || idx >= numVerts) throw new InvalidDataException("DPM triangle index out of range: " + src);
                    mesh.Triangles[i] = idx;
                }
                doc.Meshes[m] = mesh;
            }

            doc.Frames = new Frame[numFrames];
            for (uint f = 0; f < numFrames; f++)
            {
                uint o = ofsFrames + f * 68;
                var frame = new Frame { Name = CStr(d, o, 32), Local = new Matrix4x4[numBones] };
                uint ofsPose = U32(d, o + 64);
                for (uint b = 0; b < numBones; b++)
                {
                    uint q = ofsPose + b * 48;
                    var m = Matrix4x4.identity;
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 4; c++)
                            m[r, c] = F32(d, q + (uint)(r * 4 + c) * 4);
                    frame.Local[b] = m;
                }
                doc.Frames[f] = frame;
            }
            return doc;
        }

        /// <summary>World (model-space) bone matrices for <paramref name="frame"/> in raw Quake space.</summary>
        public Matrix4x4[] WorldMatrices(int frame)
        {
            var local = Frames[Mathf.Clamp(frame, 0, Frames.Length - 1)].Local;
            var world = new Matrix4x4[Bones.Length];
            for (int b = 0; b < Bones.Length; b++)
            {
                int p = Bones[b].Parent;
                world[b] = p >= 0 ? world[p] * local[b] : local[b];
            }
            return world;
        }

        /// <summary>
        /// Model-space positions/normals of one mesh at <paramref name="frame"/> (raw Quake
        /// space): Σ weight·(Mbone·origin). Normals are re-normalised.
        /// </summary>
        public void SkinMesh(int meshIndex, int frame, out Vector3[] positions, out Vector3[] normals)
        {
            var world = WorldMatrices(frame);
            var mesh = Meshes[meshIndex];
            positions = new Vector3[mesh.Vertices.Length];
            normals = new Vector3[mesh.Vertices.Length];
            for (int v = 0; v < mesh.Vertices.Length; v++)
            {
                Vector3 pos = Vector3.zero, nrm = Vector3.zero;
                float total = 0f;
                foreach (var inf in mesh.Vertices[v].Influences)
                {
                    pos += world[inf.Bone].MultiplyPoint3x4(inf.Origin) * inf.Weight;
                    nrm += world[inf.Bone].MultiplyVector(inf.Normal) * inf.Weight;
                    total += inf.Weight;
                }
                if (total > 1e-6f && Mathf.Abs(total - 1f) > 1e-3f) pos /= total;
                positions[v] = pos;
                normals[v] = nrm.sqrMagnitude > 1e-10f ? nrm.normalized : Vector3.forward;
            }
        }

        /// <summary>Unity BoneWeight (top four influences, normalised) per vertex of one mesh.</summary>
        public BoneWeight[] BuildBoneWeights(int meshIndex)
        {
            var mesh = Meshes[meshIndex];
            var bw = new BoneWeight[mesh.Vertices.Length];
            for (int v = 0; v < mesh.Vertices.Length; v++)
            {
                var list = new List<Influence>(mesh.Vertices[v].Influences);
                list.Sort((a, b) => b.Weight.CompareTo(a.Weight));
                float total = 0f;
                for (int k = 0; k < Mathf.Min(4, list.Count); k++) total += list[k].Weight;
                if (total <= 0f) total = 1f;
                var w = new BoneWeight();
                if (list.Count > 0) { w.boneIndex0 = list[0].Bone; w.weight0 = list[0].Weight / total; }
                if (list.Count > 1) { w.boneIndex1 = list[1].Bone; w.weight1 = list[1].Weight / total; }
                if (list.Count > 2) { w.boneIndex2 = list[2].Bone; w.weight2 = list[2].Weight / total; }
                if (list.Count > 3) { w.boneIndex3 = list[3].Bone; w.weight3 = list[3].Weight / total; }
                bw[v] = w;
            }
            return bw;
        }

        /// <summary>Decompose a parent-relative bone matrix (raw space) into TRS.</summary>
        public static void Decompose(Matrix4x4 m, out Vector3 t, out Quaternion r, out Vector3 s)
        {
            t = new Vector3(m.m03, m.m13, m.m23);
            var c0 = new Vector3(m.m00, m.m10, m.m20);
            var c1 = new Vector3(m.m01, m.m11, m.m21);
            var c2 = new Vector3(m.m02, m.m12, m.m22);
            s = new Vector3(c0.magnitude, c1.magnitude, c2.magnitude);
            if (s.x < 1e-8f || s.y < 1e-8f || s.z < 1e-8f) { r = Quaternion.identity; s = Vector3.one; return; }
            // Negative determinant = mirrored bone; fold the sign into one axis.
            if (Vector3.Dot(Vector3.Cross(c0, c1), c2) < 0f) s.z = -s.z;
            var rot = Matrix4x4.identity;
            rot.SetColumn(0, c0 / s.x);
            rot.SetColumn(1, c1 / s.y);
            rot.SetColumn(2, c2 / s.z);
            r = QuaternionFromRotation(rot);
        }

        static Quaternion QuaternionFromRotation(Matrix4x4 m)
        {
            // Standard robust conversion (Shepperd's method).
            float tr = m.m00 + m.m11 + m.m22;
            float x, y, z, w;
            if (tr > 0f)
            {
                float sq = Mathf.Sqrt(tr + 1f) * 2f;
                w = 0.25f * sq; x = (m.m21 - m.m12) / sq; y = (m.m02 - m.m20) / sq; z = (m.m10 - m.m01) / sq;
            }
            else if (m.m00 > m.m11 && m.m00 > m.m22)
            {
                float sq = Mathf.Sqrt(1f + m.m00 - m.m11 - m.m22) * 2f;
                w = (m.m21 - m.m12) / sq; x = 0.25f * sq; y = (m.m01 + m.m10) / sq; z = (m.m02 + m.m20) / sq;
            }
            else if (m.m11 > m.m22)
            {
                float sq = Mathf.Sqrt(1f + m.m11 - m.m00 - m.m22) * 2f;
                w = (m.m02 - m.m20) / sq; x = (m.m01 + m.m10) / sq; y = 0.25f * sq; z = (m.m12 + m.m21) / sq;
            }
            else
            {
                float sq = Mathf.Sqrt(1f + m.m22 - m.m00 - m.m11) * 2f;
                w = (m.m10 - m.m01) / sq; x = (m.m02 + m.m20) / sq; y = (m.m12 + m.m21) / sq; z = 0.25f * sq;
            }
            return new Quaternion(x, y, z, w).normalized;
        }

        static uint U32(byte[] d, uint o)
        {
            if (o + 4 > d.Length) throw new InvalidDataException("DPM read past end of file at " + o);
            return (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
        }

        static float F32(byte[] d, uint o)
        {
            uint u = U32(d, o);
            var bytes = BitConverter.GetBytes(u);
            return BitConverter.ToSingle(bytes, 0);
        }

        static string CStr(byte[] d, uint o, int max)
        {
            int e = (int)o;
            while (e < d.Length && e < o + max && d[e] != 0) e++;
            return Encoding.ASCII.GetString(d, (int)o, e - (int)o);
        }
    }
}
