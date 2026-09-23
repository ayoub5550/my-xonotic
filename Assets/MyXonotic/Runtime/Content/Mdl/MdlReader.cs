using System;
using System.IO;
using System.Text;
using MyXonotic.Content.Bsp;
using MyXonotic.Content.Md3;

namespace MyXonotic.Content.Mdl
{
    /// <summary>
    /// dev.18: reader for the Quake-1 "IDPO" MDL format (version 6), the one
    /// projectile format this project could not load before (hagarmissile.mdl in
    /// the Xonotic 0.8.6 pack; bullet.mdl too). Written from the well-known public
    /// description of the format (id Software's released specification): header
    /// (84 bytes: ident, version, scale[3], translate[3], boundingradius, eye[3],
    /// numskins, skinwidth, skinheight, numverts, numtris, numframes, synctype,
    /// flags, size), then skins (group flag + 8-bit indexed pixels, group skins
    /// carry a count and per-skin times), stvert[] (onseam, s, t as int32),
    /// triangle[] (facesfront, vertindex[3]), frames (type, bboxmin/max as
    /// packed vertices, name[16], packed vertices: byte xyz + byte normal index).
    /// Vertex = packed * scale + translate (Quake units) → QuakeToUnity.
    /// Only frame 0 is decoded (same static-first rule as <see cref="Md3Reader"/>).
    /// Texture coordinates: s/skinwidth, t/skinheight; back-facing triangles on
    /// "onseam" vertices add skinwidth/2 to s as the format prescribes.
    /// The indexed skin is expanded to RGBA with the standard Quake palette
    /// (DarkPlaces' built-in default table; Xonotic ships no palette.lmp).
    /// </summary>
    public static class MdlReader
    {
        public const int Version = 6;
        public const int HeaderSize = 84;

        public sealed class MdlSkin { public int Width, Height; public byte[] Rgba; }

        public sealed class MdlModel
        {
            public Md3StaticModel Model;
            public MdlSkin Skin;   // first skin (or first of the first group), RGBA32 top-down
        }

        public static bool IsMdl(byte[] data) =>
            data != null && data.Length >= HeaderSize && data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'P' && data[3] == (byte)'O';

        public static MdlModel Read(byte[] data, string sourcePathForErrors, string surfaceName)
        {
            if (!IsMdl(data)) throw new Md3FormatException(sourcePathForErrors + ": not an IDPO model");
            using (var br = new BinaryReader(new MemoryStream(data)))
            {
                br.ReadInt32();
                int version = br.ReadInt32();
                if (version != Version) throw new Md3FormatException(sourcePathForErrors + ": unsupported MDL version " + version);
                float sx = br.ReadSingle(), sy = br.ReadSingle(), sz = br.ReadSingle();
                float tx = br.ReadSingle(), ty = br.ReadSingle(), tz = br.ReadSingle();
                br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); // boundingradius, eye
                int numSkins = br.ReadInt32(), skinW = br.ReadInt32(), skinH = br.ReadInt32();
                int numVerts = br.ReadInt32(), numTris = br.ReadInt32(), numFrames = br.ReadInt32();
                br.ReadInt32(); br.ReadInt32(); br.ReadSingle(); // synctype, flags, size
                if (numVerts <= 0 || numVerts > 65536 || numTris <= 0 || numTris > 65536 || numFrames <= 0 || skinW <= 0 || skinH <= 0 || skinW * skinH > 16 * 1024 * 1024)
                    throw new Md3FormatException(sourcePathForErrors + ": implausible MDL header counts");

                MdlSkin skin = null;
                for (int s = 0; s < numSkins; s++)
                {
                    int group = br.ReadInt32();
                    int count = 1;
                    if (group != 0)
                    {
                        count = br.ReadInt32();
                        if (count <= 0 || count > 1024) throw new Md3FormatException(sourcePathForErrors + ": implausible skin group");
                        for (int i = 0; i < count; i++) br.ReadSingle(); // times
                    }
                    for (int i = 0; i < count; i++)
                    {
                        byte[] idx = br.ReadBytes(skinW * skinH);
                        if (idx.Length != skinW * skinH) throw new Md3FormatException(sourcePathForErrors + ": truncated skin");
                        if (skin == null) skin = new MdlSkin { Width = skinW, Height = skinH, Rgba = Expand(idx) };
                    }
                }

                var st = new int[numVerts, 3];
                for (int v = 0; v < numVerts; v++) { st[v, 0] = br.ReadInt32(); st[v, 1] = br.ReadInt32(); st[v, 2] = br.ReadInt32(); }
                var tris = new int[numTris, 4];
                for (int t = 0; t < numTris; t++) { tris[t, 0] = br.ReadInt32(); tris[t, 1] = br.ReadInt32(); tris[t, 2] = br.ReadInt32(); tris[t, 3] = br.ReadInt32(); }

                // frame 0 (simple or the first of a group)
                int frameType = br.ReadInt32();
                if (frameType != 0)
                {
                    int n = br.ReadInt32();
                    if (n <= 0 || n > 4096) throw new Md3FormatException(sourcePathForErrors + ": implausible frame group");
                    br.ReadBytes(8); // group bboxmin/max
                    for (int i = 0; i < n; i++) br.ReadSingle();
                }
                br.ReadBytes(8);  // bboxmin, bboxmax
                br.ReadBytes(16); // name
                var packed = new byte[numVerts * 4];
                if (br.Read(packed, 0, packed.Length) != packed.Length) throw new Md3FormatException(sourcePathForErrors + ": truncated frame");

                // MDL shares one vertex between front and back faces with different s; split per (vertex, side).
                var positions = new System.Collections.Generic.List<BspVec3>();
                var normals = new System.Collections.Generic.List<BspVec3>();
                var uvs = new System.Collections.Generic.List<BspVec2>();
                var indexMap = new System.Collections.Generic.Dictionary<int, int>();
                var indices = new int[numTris * 3];
                for (int t = 0; t < numTris; t++)
                {
                    bool front = tris[t, 0] != 0;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = tris[t, 1 + k];
                        if (v < 0 || v >= numVerts) throw new Md3FormatException(sourcePathForErrors + ": triangle vertex out of range");
                        bool seamBack = !front && st[v, 0] != 0;
                        int key = v * 2 + (seamBack ? 1 : 0);
                        int outIndex;
                        if (!indexMap.TryGetValue(key, out outIndex))
                        {
                            outIndex = positions.Count;
                            indexMap[key] = outIndex;
                            var q = new BspVec3(packed[v * 4] * sx + tx, packed[v * 4 + 1] * sy + ty, packed[v * 4 + 2] * sz + tz);
                            positions.Add(BspCoordinateSpace.QuakeToUnity(q));
                            normals.Add(BspCoordinateSpace.QuakeDirectionToUnity(Anorm(packed[v * 4 + 3])));
                            float s = st[v, 1] + (seamBack ? skinW * 0.5f : 0f);
                            uvs.Add(new BspVec2((s + 0.5f) / skinW, 1f - (st[v, 2] + 0.5f) / skinH));
                        }
                        indices[t * 3 + k] = outIndex;
                    }
                }

                var surf = new Md3Surface
                {
                    Name = surfaceName,
                    ShaderNames = new string[0],
                    Positions = positions.ToArray(),
                    Normals = normals.ToArray(),
                    TexCoords = uvs.ToArray(),
                    Triangles = indices
                };
                return new MdlModel { Model = new Md3StaticModel { Name = surfaceName, Surfaces = new[] { surf } }, Skin = skin };
            }
        }

        static byte[] Expand(byte[] indexed)
        {
            var rgba = new byte[indexed.Length * 4];
            for (int i = 0; i < indexed.Length; i++)
            {
                int p = indexed[i] * 3;
                rgba[i * 4] = QuakePalette[p]; rgba[i * 4 + 1] = QuakePalette[p + 1]; rgba[i * 4 + 2] = QuakePalette[p + 2];
                rgba[i * 4 + 3] = indexed[i] == 255 ? (byte)0 : (byte)255; // index 255 = transparent in the Quake convention
            }
            return rgba;
        }

        /// id's 162 precomputed MDL vertex normals (anorms table of the public format description).
        static BspVec3 Anorm(byte index)
        {
            int i = index < 162 ? index * 3 : 0;
            return new BspVec3(Anorms[i], Anorms[i + 1], Anorms[i + 2]);
        }

        static readonly float[] Anorms =
        {
            -0.525731f, 0.000000f, 0.850651f, -0.442863f, 0.238856f, 0.864188f, -0.295242f, 0.000000f, 0.955423f, -0.309017f, 0.500000f, 0.809017f,
            -0.162460f, 0.262866f, 0.951056f, 0.000000f, 0.000000f, 1.000000f, 0.000000f, 0.850651f, 0.525731f, -0.147621f, 0.716567f, 0.681718f,
            0.147621f, 0.716567f, 0.681718f, 0.000000f, 0.525731f, 0.850651f, 0.309017f, 0.500000f, 0.809017f, 0.525731f, 0.000000f, 0.850651f,
            0.295242f, 0.000000f, 0.955423f, 0.442863f, 0.238856f, 0.864188f, 0.162460f, 0.262866f, 0.951056f, -0.681718f, 0.147621f, 0.716567f,
            -0.809017f, 0.309017f, 0.500000f, -0.587785f, 0.425325f, 0.688191f, -0.850651f, 0.525731f, 0.000000f, -0.864188f, 0.442863f, 0.238856f,
            -0.716567f, 0.681718f, 0.147621f, -0.688191f, 0.587785f, 0.425325f, -0.500000f, 0.809017f, 0.309017f, -0.238856f, 0.864188f, 0.442863f,
            -0.425325f, 0.688191f, 0.587785f, -0.716567f, 0.681718f, -0.147621f, -0.500000f, 0.809017f, -0.309017f, -0.525731f, 0.850651f, 0.000000f,
            0.000000f, 0.850651f, -0.525731f, -0.238856f, 0.864188f, -0.442863f, 0.000000f, 0.955423f, -0.295242f, -0.262866f, 0.951056f, -0.162460f,
            0.000000f, 1.000000f, 0.000000f, 0.000000f, 0.955423f, 0.295242f, -0.262866f, 0.951056f, 0.162460f, 0.238856f, 0.864188f, 0.442863f,
            0.262866f, 0.951056f, 0.162460f, 0.500000f, 0.809017f, 0.309017f, 0.238856f, 0.864188f, -0.442863f, 0.262866f, 0.951056f, -0.162460f,
            0.500000f, 0.809017f, -0.309017f, 0.850651f, 0.525731f, 0.000000f, 0.716567f, 0.681718f, 0.147621f, 0.716567f, 0.681718f, -0.147621f,
            0.525731f, 0.850651f, 0.000000f, 0.425325f, 0.688191f, 0.587785f, 0.864188f, 0.442863f, 0.238856f, 0.688191f, 0.587785f, 0.425325f,
            0.809017f, 0.309017f, 0.500000f, 0.681718f, 0.147621f, 0.716567f, 0.587785f, 0.425325f, 0.688191f, 0.955423f, 0.295242f, 0.000000f,
            1.000000f, 0.000000f, 0.000000f, 0.951056f, 0.162460f, 0.262866f, 0.850651f, -0.525731f, 0.000000f, 0.955423f, -0.295242f, 0.000000f,
            0.864188f, -0.442863f, 0.238856f, 0.951056f, -0.162460f, 0.262866f, 0.809017f, -0.309017f, 0.500000f, 0.681718f, -0.147621f, 0.716567f,
            0.850651f, 0.000000f, 0.525731f, 0.864188f, 0.442863f, -0.238856f, 0.809017f, 0.309017f, -0.500000f, 0.951056f, 0.162460f, -0.262866f,
            0.525731f, 0.000000f, -0.850651f, 0.681718f, 0.147621f, -0.716567f, 0.681718f, -0.147621f, -0.716567f, 0.850651f, 0.000000f, -0.525731f,
            0.809017f, -0.309017f, -0.500000f, 0.864188f, -0.442863f, -0.238856f, 0.951056f, -0.162460f, -0.262866f, 0.147621f, 0.716567f, -0.681718f,
            0.309017f, 0.500000f, -0.809017f, 0.425325f, 0.688191f, -0.587785f, 0.442863f, 0.238856f, -0.864188f, 0.587785f, 0.425325f, -0.688191f,
            0.688191f, 0.587785f, -0.425325f, -0.147621f, 0.716567f, -0.681718f, -0.309017f, 0.500000f, -0.809017f, 0.000000f, 0.525731f, -0.850651f,
            -0.525731f, 0.000000f, -0.850651f, -0.442863f, 0.238856f, -0.864188f, -0.295242f, 0.000000f, -0.955423f, -0.162460f, 0.262866f, -0.951056f,
            0.000000f, 0.000000f, -1.000000f, 0.295242f, 0.000000f, -0.955423f, 0.162460f, 0.262866f, -0.951056f, -0.442863f, -0.238856f, -0.864188f,
            -0.309017f, -0.500000f, -0.809017f, -0.162460f, -0.262866f, -0.951056f, 0.000000f, -0.850651f, -0.525731f, -0.147621f, -0.716567f, -0.681718f,
            0.147621f, -0.716567f, -0.681718f, 0.000000f, -0.525731f, -0.850651f, 0.309017f, -0.500000f, -0.809017f, 0.442863f, -0.238856f, -0.864188f,
            0.162460f, -0.262866f, -0.951056f, 0.238856f, -0.864188f, -0.442863f, 0.500000f, -0.809017f, -0.309017f, 0.425325f, -0.688191f, -0.587785f,
            0.716567f, -0.681718f, -0.147621f, 0.688191f, -0.587785f, -0.425325f, 0.587785f, -0.425325f, -0.688191f, 0.000000f, -0.955423f, -0.295242f,
            0.000000f, -1.000000f, 0.000000f, 0.262866f, -0.951056f, -0.162460f, 0.000000f, -0.850651f, 0.525731f, 0.000000f, -0.955423f, 0.295242f,
            0.238856f, -0.864188f, 0.442863f, 0.262866f, -0.951056f, 0.162460f, 0.500000f, -0.809017f, 0.309017f, 0.716567f, -0.681718f, 0.147621f,
            0.525731f, -0.850651f, 0.000000f, -0.238856f, -0.864188f, -0.442863f, -0.500000f, -0.809017f, -0.309017f, -0.262866f, -0.951056f, -0.162460f,
            -0.850651f, -0.525731f, 0.000000f, -0.716567f, -0.681718f, -0.147621f, -0.716567f, -0.681718f, 0.147621f, -0.525731f, -0.850651f, 0.000000f,
            -0.500000f, -0.809017f, 0.309017f, -0.238856f, -0.864188f, 0.442863f, -0.262866f, -0.951056f, 0.162460f, -0.864188f, -0.442863f, 0.238856f,
            -0.809017f, -0.309017f, 0.500000f, -0.688191f, -0.587785f, 0.425325f, -0.681718f, -0.147621f, 0.716567f, -0.442863f, -0.238856f, 0.864188f,
            -0.587785f, -0.425325f, 0.688191f, -0.309017f, -0.500000f, 0.809017f, -0.147621f, -0.716567f, 0.681718f, -0.425325f, -0.688191f, 0.587785f,
            -0.162460f, -0.262866f, 0.951056f, 0.442863f, -0.238856f, 0.864188f, 0.162460f, -0.262866f, 0.951056f, 0.309017f, -0.500000f, 0.809017f,
            0.147621f, -0.716567f, 0.681718f, 0.000000f, -0.525731f, 0.850651f, 0.425325f, -0.688191f, 0.587785f, 0.587785f, -0.425325f, 0.688191f,
            0.688191f, -0.587785f, 0.425325f, -0.955423f, 0.295242f, 0.000000f, -0.951056f, 0.162460f, 0.262866f, -1.000000f, 0.000000f, 0.000000f,
            -0.850651f, 0.000000f, 0.525731f, -0.955423f, -0.295242f, 0.000000f, -0.951056f, -0.162460f, 0.262866f, -0.864188f, 0.442863f, -0.238856f,
            -0.951056f, 0.162460f, -0.262866f, -0.809017f, 0.309017f, -0.500000f, -0.864188f, -0.442863f, -0.238856f, -0.951056f, -0.162460f, -0.262866f,
            -0.809017f, -0.309017f, -0.500000f, -0.681718f, 0.147621f, -0.716567f, -0.681718f, -0.147621f, -0.716567f, -0.850651f, 0.000000f, -0.525731f,
            -0.688191f, 0.587785f, -0.425325f, -0.587785f, 0.425325f, -0.688191f, -0.425325f, 0.688191f, -0.587785f, -0.425325f, -0.688191f, -0.587785f,
            -0.587785f, -0.425325f, -0.688191f, -0.688191f, -0.587785f, -0.425325f,
        };

        /// Standard Quake palette (256 × RGB) as carried by DarkPlaces' default table.
        public static readonly byte[] QuakePalette =
        {
            0,0,0,15,15,15,31,31,31,47,47,47,63,63,63,75,75,75,91,91,91,107,107,107,
            123,123,123,139,139,139,155,155,155,171,171,171,187,187,187,203,203,203,219,219,219,235,235,235,
            15,11,7,23,15,11,31,23,11,39,27,15,47,35,19,55,43,23,63,47,23,75,55,27,
            83,59,27,91,67,31,99,75,31,107,83,31,115,87,31,123,95,35,131,103,35,143,111,35,
            11,11,15,19,19,27,27,27,39,39,39,51,47,47,63,55,55,75,63,63,87,71,71,103,
            79,79,115,91,91,127,99,99,139,107,107,151,115,115,163,123,123,175,131,131,187,139,139,203,
            0,0,0,7,7,0,11,11,0,19,19,0,27,27,0,35,35,0,43,43,7,47,47,7,
            55,55,7,63,63,7,71,71,7,75,75,11,83,83,11,91,91,11,99,99,11,107,107,15,
            7,0,0,15,0,0,23,0,0,31,0,0,39,0,0,47,0,0,55,0,0,63,0,0,
            71,0,0,79,0,0,87,0,0,95,0,0,103,0,0,111,0,0,119,0,0,127,0,0,
            19,19,0,27,27,0,35,35,0,47,43,0,55,47,0,67,55,0,75,59,7,87,67,7,
            95,71,7,107,75,11,119,83,15,131,87,19,139,91,19,151,95,27,163,99,31,175,103,35,
            35,19,7,47,23,11,59,31,15,75,35,19,87,43,23,99,47,31,115,55,35,127,59,43,
            143,67,51,159,79,51,175,99,47,191,119,47,207,143,43,223,171,39,239,203,31,255,243,27,
            11,7,0,27,19,0,43,35,15,55,43,19,71,51,27,83,55,35,99,63,43,111,71,51,
            127,83,63,139,95,71,155,107,83,167,123,95,183,135,107,195,147,123,211,163,139,227,179,151,
            171,139,163,159,127,151,147,115,135,139,103,123,127,91,111,119,83,99,107,75,87,95,63,75,
            87,55,67,75,47,55,67,39,47,55,31,35,43,23,27,35,19,19,23,11,11,15,7,7,
            187,115,159,175,107,143,163,95,131,151,87,119,139,79,107,127,75,95,115,67,83,107,59,75,
            95,51,63,83,43,55,71,35,43,59,31,35,47,23,27,35,19,19,23,11,11,15,7,7,
            219,195,187,203,179,167,191,163,155,175,151,139,163,135,123,151,123,111,135,111,95,123,99,83,
            107,87,71,95,75,59,83,63,51,67,51,39,55,43,31,39,31,23,27,19,15,15,11,7,
            111,131,123,103,123,111,95,115,103,87,107,95,79,99,87,71,91,79,63,83,71,55,75,63,
            47,67,55,43,59,47,35,51,39,31,43,31,23,35,23,15,27,19,11,19,11,7,11,7,
            255,243,27,239,223,23,219,203,19,203,183,15,187,167,15,171,151,11,155,131,7,139,115,7,
            123,99,7,107,83,0,91,71,0,75,55,0,59,43,0,43,31,0,27,15,0,11,7,0,
            0,0,255,11,11,239,19,19,223,27,27,207,35,35,191,43,43,175,47,47,159,47,47,143,
            47,47,127,47,47,111,47,47,95,43,43,79,35,35,63,27,27,47,19,19,31,11,11,15,
            43,0,0,59,0,0,75,7,0,95,7,0,111,15,0,127,23,7,147,31,7,163,39,11,
            183,51,15,195,75,27,207,99,43,219,127,59,227,151,79,231,171,95,239,191,119,247,211,139,
            167,123,59,183,155,55,199,195,55,231,227,87,127,191,255,171,231,255,215,255,255,103,0,0,
            139,0,0,179,0,0,215,0,0,255,0,0,255,243,147,255,247,199,255,255,255,159,91,83,
        };
    }
}
