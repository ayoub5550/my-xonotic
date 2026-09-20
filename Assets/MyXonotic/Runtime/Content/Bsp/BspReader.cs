using System;
using System.Collections.Generic;

namespace MyXonotic.Content.Bsp
{
    /// <summary>
    /// Reads an IBSP version 46 (Quake III family, used by Xonotic) file
    /// into a <see cref="BspDocument"/>. Written from scratch against the
    /// publicly documented description of the file layout
    /// (magic/version/lump directory, fixed-size vertex/face/model records);
    /// no GPL engine source was read or copied.
    ///
    /// Hardening rules applied throughout:
    ///  - every offset/length is bounds-checked against the actual buffer;
    ///  - every record count is bounds-checked against a generous but finite
    ///    ceiling so a hostile lump length cannot force gigabytes of
    ///    allocation;
    ///  - every float is rejected if NaN/Infinity or absurdly large.
    /// </summary>
    public static class BspReader
    {
        public const int SupportedVersion = 46;
        private const int HeaderMagicSize = 4;
        private const int LumpDirEntrySize = 8; // int offset, int length
        private const int HeaderSize = HeaderMagicSize + 4 + ((int)BspLump.Count) * LumpDirEntrySize;

        private const int ShaderRecordSize = 64 + 4 + 4;
        private const int VertexRecordSize = (3 + 2 + 2 + 3) * 4 + 4; // pos+uv+lmuv+normal (floats) + 4 color bytes
        private const int MeshVertRecordSize = 4;
        private const int FaceRecordSize = 26 * 4;
        private const int ModelRecordSize = (3 + 3) * 4 + 4 * 4;

        // Sanity ceilings. Generous for legitimate Q3-era maps, far below
        // what would exhaust memory or overflow int math.
        private const int MaxShaders = 1 << 14;
        private const int MaxVertexes = 1 << 21;
        private const int MaxMeshVerts = 1 << 23;
        private const int MaxFaces = 1 << 19;
        private const int MaxModels = 1 << 16;
        private const int MaxEntityLumpBytes = 32 * 1024 * 1024;
        private const float MaxCoordinateMagnitude = 1_000_000f;

        public static BspDocument Read(byte[] data)
        {
            if (data == null)
            {
                throw new BspFormatException("BSP data buffer is null.");
            }
            if (data.Length < HeaderSize)
            {
                throw new BspFormatException(string.Format(
                    "BSP file too small to contain a header ({0} bytes, need at least {1}).",
                    data.Length, HeaderSize));
            }

            var reader = new BspLittleEndianReader(data);
            var doc = new BspDocument();

            string magic = reader.ReadAsciiRun(0, 4);
            if (magic != "IBSP")
            {
                throw new BspFormatException(
                    "Unsupported BSP magic '" + magic + "': only IBSP (Quake III family) is supported.");
            }

            int version = reader.ReadInt32(4);
            doc.Version = version;
            if (version != SupportedVersion)
            {
                throw new BspFormatException(string.Format(
                    "Unsupported IBSP version {0}: only version {1} (Quake III / Xonotic) is implemented.",
                    version, SupportedVersion));
            }

            var lumps = new BspLumpDirEntry[(int)BspLump.Count];
            for (int i = 0; i < lumps.Length; i++)
            {
                int entryOffset = 8 + i * LumpDirEntrySize;
                int offset = reader.ReadInt32(entryOffset);
                int length = reader.ReadInt32(entryOffset + 4);
                if (offset < 0 || length < 0)
                {
                    throw new BspFormatException(string.Format(
                        "Lump {0} has a negative offset/length ({1}/{2}).", (BspLump)i, offset, length));
                }
                long end = (long)offset + length;
                if (end > data.Length)
                {
                    throw new BspFormatException(string.Format(
                        "Lump {0} claims range [{1},{2}) which exceeds file size {3}.",
                        (BspLump)i, offset, end, data.Length));
                }
                lumps[i] = new BspLumpDirEntry { Offset = offset, Length = length };
            }

            ReadEntities(reader, lumps[(int)BspLump.Entities], doc);
            doc.Shaders = ReadShaders(reader, lumps[(int)BspLump.Shaders], doc.Warnings);
            doc.Vertexes = ReadVertexes(reader, lumps[(int)BspLump.Vertexes], doc.Warnings);
            doc.MeshVerts = ReadMeshVerts(reader, lumps[(int)BspLump.MeshVerts]);
            doc.Faces = ReadFaces(reader, lumps[(int)BspLump.Faces], doc.Warnings);
            doc.Models = ReadModels(reader, lumps[(int)BspLump.Models], doc.Warnings);
            doc.Lightmaps = ReadLightmaps(data, lumps[(int)BspLump.LightMaps], doc.Warnings);

            ValidateFaceReferences(doc);

            return doc;
        }

        /// <summary>
        /// Internal lightmaps: consecutive 128x128 RGB blocks. Maps compiled with
        /// external lightmaps (Xonotic default) have an empty lump and ship
        /// maps/&lt;name&gt;/lm_XXXX.tga instead; the importer resolves those.
        /// </summary>
        private static byte[][] ReadLightmaps(byte[] data, BspLumpDirEntry lump, List<string> warnings)
        {
            const int blockSize = 128 * 128 * 3;
            if (lump.Length <= 0) return new byte[0][];
            if (lump.Length % blockSize != 0)
            {
                warnings.Add("Lightmap lump is not a multiple of 128x128x3 bytes; internal lightmaps ignored.");
                return new byte[0][];
            }
            int count = lump.Length / blockSize;
            if (count > 4096)
            {
                warnings.Add("Lightmap lump declares more than 4096 blocks; internal lightmaps ignored.");
                return new byte[0][];
            }
            var result = new byte[count][];
            for (int i = 0; i < count; i++)
            {
                result[i] = new byte[blockSize];
                System.Buffer.BlockCopy(data, lump.Offset + i * blockSize, result[i], 0, blockSize);
            }
            return result;
        }

        private static void RequireExactMultiple(BspLumpDirEntry lump, int recordSize, BspLump which)
        {
            if (lump.Length % recordSize != 0)
            {
                throw new BspFormatException(string.Format(
                    "Lump {0} length {1} is not a multiple of the {2}-byte record size.",
                    which, lump.Length, recordSize));
            }
        }

        private static void CheckCount(int count, int max, BspLump which)
        {
            if (count > max)
            {
                throw new BspFormatException(string.Format(
                    "Lump {0} record count {1} exceeds the safety ceiling {2}; refusing to parse (possible malicious/corrupt file).",
                    which, count, max));
            }
        }

        private const float MaxUvMagnitude = 1_000_000f; // UVs can legitimately repeat many times over large surfaces; only reject NaN/Infinity/absurd.

        private static void CheckFiniteUv(float v, string what)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                throw new BspFormatException("Non-finite float (" + what + ") encountered in BSP data.");
            }
            if (v > MaxUvMagnitude || v < -MaxUvMagnitude)
            {
                throw new BspFormatException(string.Format(
                    "UV magnitude {0} in {1} exceeds sanity bound {2}.", v, what, MaxUvMagnitude));
            }
        }

        private static void CheckFinite(float v, string what)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                throw new BspFormatException("Non-finite float (" + what + ") encountered in BSP data.");
            }
            if (v > MaxCoordinateMagnitude || v < -MaxCoordinateMagnitude)
            {
                throw new BspFormatException(string.Format(
                    "Coordinate magnitude {0} in {1} exceeds sanity bound {2}.", v, what, MaxCoordinateMagnitude));
            }
        }

        private static void ReadEntities(BspLittleEndianReader reader, BspLumpDirEntry lump, BspDocument doc)
        {
            if (lump.Length > MaxEntityLumpBytes)
            {
                throw new BspFormatException(string.Format(
                    "Entities lump is {0} bytes, exceeding the {1}-byte safety ceiling.", lump.Length, MaxEntityLumpBytes));
            }
            // The lump is a NUL-terminated (or not) ASCII/UTF8-ish text blob.
            int length = lump.Length;
            if (length > 0)
            {
                // Trim a single trailing NUL if present, without assuming it exists.
                string raw = reader.ReadAsciiRun(lump.Offset, length);
                int nul = raw.IndexOf('\0');
                if (nul >= 0) raw = raw.Substring(0, nul);
                doc.Entities = BspEntityParser.Parse(raw, doc.Warnings);
            }
        }

        private static BspShader[] ReadShaders(BspLittleEndianReader reader, BspLumpDirEntry lump, List<string> warnings)
        {
            RequireExactMultiple(lump, ShaderRecordSize, BspLump.Shaders);
            int count = lump.Length / ShaderRecordSize;
            CheckCount(count, MaxShaders, BspLump.Shaders);
            var result = new BspShader[count];
            for (int i = 0; i < count; i++)
            {
                int o = lump.Offset + i * ShaderRecordSize;
                result[i] = new BspShader
                {
                    Name = reader.ReadFixedString(o, 64),
                    // Q3 shader_t field order is { name[64]; surfaceFlags; contentFlags; }
                    // (verified against a real Xonotic map: caulk decodes to
                    // contentFlags==CONTENTS_SOLID and surfaceFlags carrying
                    // SURF_NODRAW/SKIP-style bits only with this ordering).
                    SurfaceFlags = reader.ReadInt32(o + 64),
                    ContentFlags = reader.ReadInt32(o + 68),
                };
                if (string.IsNullOrEmpty(result[i].Name))
                {
                    warnings.Add(string.Format("Shader #{0} has an empty name; geometry using it will render untextured.", i));
                }
            }
            return result;
        }

        private static BspVertex[] ReadVertexes(BspLittleEndianReader reader, BspLumpDirEntry lump, List<string> warnings)
        {
            RequireExactMultiple(lump, VertexRecordSize, BspLump.Vertexes);
            int count = lump.Length / VertexRecordSize;
            CheckCount(count, MaxVertexes, BspLump.Vertexes);
            var result = new BspVertex[count];
            for (int i = 0; i < count; i++)
            {
                int o = lump.Offset + i * VertexRecordSize;
                float px = reader.ReadFloat32(o + 0);
                float py = reader.ReadFloat32(o + 4);
                float pz = reader.ReadFloat32(o + 8);
                CheckFinite(px, "vertex.position.x");
                CheckFinite(py, "vertex.position.y");
                CheckFinite(pz, "vertex.position.z");

                float su = reader.ReadFloat32(o + 12);
                float sv = reader.ReadFloat32(o + 16);
                float lu = reader.ReadFloat32(o + 20);
                float lv = reader.ReadFloat32(o + 24);
                CheckFiniteUv(su, "vertex.surfaceUv.u");
                CheckFiniteUv(sv, "vertex.surfaceUv.v");
                CheckFiniteUv(lu, "vertex.lightmapUv.u");
                CheckFiniteUv(lv, "vertex.lightmapUv.v");
                float nx = reader.ReadFloat32(o + 28);
                float ny = reader.ReadFloat32(o + 32);
                float nz = reader.ReadFloat32(o + 36);
                CheckFinite(nx, "vertex.normal.x");
                CheckFinite(ny, "vertex.normal.y");
                CheckFinite(nz, "vertex.normal.z");

                byte r = reader.ReadByte(o + 40);
                byte g = reader.ReadByte(o + 41);
                byte b = reader.ReadByte(o + 42);
                byte a = reader.ReadByte(o + 43);

                result[i] = new BspVertex
                {
                    Position = new BspVec3(px, py, pz),
                    SurfaceUv = new BspVec2(su, sv),
                    LightmapUv = new BspVec2(lu, lv),
                    Normal = new BspVec3(nx, ny, nz),
                    Color = new BspColor32(r, g, b, a),
                };
            }
            return result;
        }

        private static int[] ReadMeshVerts(BspLittleEndianReader reader, BspLumpDirEntry lump)
        {
            RequireExactMultiple(lump, MeshVertRecordSize, BspLump.MeshVerts);
            int count = lump.Length / MeshVertRecordSize;
            CheckCount(count, MaxMeshVerts, BspLump.MeshVerts);
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = reader.ReadInt32(lump.Offset + i * MeshVertRecordSize);
            }
            return result;
        }

        private static BspFace[] ReadFaces(BspLittleEndianReader reader, BspLumpDirEntry lump, List<string> warnings)
        {
            RequireExactMultiple(lump, FaceRecordSize, BspLump.Faces);
            int count = lump.Length / FaceRecordSize;
            CheckCount(count, MaxFaces, BspLump.Faces);
            var result = new BspFace[count];
            for (int i = 0; i < count; i++)
            {
                int o = lump.Offset + i * FaceRecordSize;
                int idx = 0;
                int texture = reader.ReadInt32(o + (idx++) * 4);
                int effect = reader.ReadInt32(o + (idx++) * 4);
                int type = reader.ReadInt32(o + (idx++) * 4);
                int vertex = reader.ReadInt32(o + (idx++) * 4);
                int nVerts = reader.ReadInt32(o + (idx++) * 4);
                int meshVert = reader.ReadInt32(o + (idx++) * 4);
                int nMeshVerts = reader.ReadInt32(o + (idx++) * 4);
                int lmIndex = reader.ReadInt32(o + (idx++) * 4);
                idx += 2; // lm_start[2]
                idx += 2; // lm_size[2]
                idx += 3; // lm_origin[3]
                idx += 6; // lm_vecs[2][3]
                idx += 3; // normal[3]
                int patchWidth = reader.ReadInt32(o + (idx++) * 4);
                int patchHeight = reader.ReadInt32(o + (idx++) * 4);

                if (nVerts < 0 || nMeshVerts < 0 || vertex < 0 || meshVert < 0)
                {
                    throw new BspFormatException(string.Format("Face #{0} has a negative count/index field.", i));
                }

                result[i] = new BspFace
                {
                    Texture = texture,
                    Effect = effect,
                    Type = type,
                    Vertex = vertex,
                    NumVertexes = nVerts,
                    MeshVert = meshVert,
                    NumMeshVerts = nMeshVerts,
                    LightmapIndex = lmIndex,
                    PatchWidth = patchWidth,
                    PatchHeight = patchHeight,
                };

                if (type != (int)BspFaceType.Polygon && type != (int)BspFaceType.Mesh &&
                    type != (int)BspFaceType.Patch && type != (int)BspFaceType.Billboard)
                {
                    warnings.Add(string.Format("Face #{0} has unknown type {1}; it will be skipped.", i, type));
                }
            }
            return result;
        }

        private static BspModel[] ReadModels(BspLittleEndianReader reader, BspLumpDirEntry lump, List<string> warnings)
        {
            RequireExactMultiple(lump, ModelRecordSize, BspLump.Models);
            int count = lump.Length / ModelRecordSize;
            CheckCount(count, MaxModels, BspLump.Models);
            var result = new BspModel[count];
            for (int i = 0; i < count; i++)
            {
                int o = lump.Offset + i * ModelRecordSize;
                float minx = reader.ReadFloat32(o + 0);
                float miny = reader.ReadFloat32(o + 4);
                float minz = reader.ReadFloat32(o + 8);
                float maxx = reader.ReadFloat32(o + 12);
                float maxy = reader.ReadFloat32(o + 16);
                float maxz = reader.ReadFloat32(o + 20);
                CheckFinite(minx, "model.mins.x"); CheckFinite(miny, "model.mins.y"); CheckFinite(minz, "model.mins.z");
                CheckFinite(maxx, "model.maxs.x"); CheckFinite(maxy, "model.maxs.y"); CheckFinite(maxz, "model.maxs.z");

                int face = reader.ReadInt32(o + 24);
                int nFaces = reader.ReadInt32(o + 28);
                int brush = reader.ReadInt32(o + 32);
                int nBrushes = reader.ReadInt32(o + 36);
                if (face < 0 || nFaces < 0 || brush < 0 || nBrushes < 0)
                {
                    throw new BspFormatException(string.Format("Model #{0} has a negative count/index field.", i));
                }

                result[i] = new BspModel
                {
                    Mins = new BspVec3(minx, miny, minz),
                    Maxs = new BspVec3(maxx, maxy, maxz),
                    Face = face,
                    NumFaces = nFaces,
                    Brush = brush,
                    NumBrushes = nBrushes,
                };
            }
            if (count == 0)
            {
                warnings.Add("Models lump is empty; expected at least model #0 (worldspawn).");
            }
            return result;
        }

        /// <summary>
        /// Cross-checks that every face/vertex/meshvert index referenced by
        /// a model actually exists, so downstream geometry building can
        /// index arrays without further bounds checks.
        /// </summary>
        private static void ValidateFaceReferences(BspDocument doc)
        {
            foreach (var model in doc.Models)
            {
                long end = (long)model.Face + model.NumFaces;
                if (end > doc.Faces.Length)
                {
                    throw new BspFormatException(string.Format(
                        "Model references faces [{0},{1}) but only {2} faces exist.",
                        model.Face, end, doc.Faces.Length));
                }
            }
            for (int i = 0; i < doc.Faces.Length; i++)
            {
                var f = doc.Faces[i];
                long vEnd = (long)f.Vertex + f.NumVertexes;
                if (vEnd > doc.Vertexes.Length)
                {
                    throw new BspFormatException(string.Format(
                        "Face #{0} references vertices [{1},{2}) but only {3} vertices exist.",
                        i, f.Vertex, vEnd, doc.Vertexes.Length));
                }
                long mEnd = (long)f.MeshVert + f.NumMeshVerts;
                if (mEnd > doc.MeshVerts.Length)
                {
                    throw new BspFormatException(string.Format(
                        "Face #{0} references meshverts [{1},{2}) but only {3} meshverts exist.",
                        i, f.MeshVert, mEnd, doc.MeshVerts.Length));
                }
                if (f.Type == (int)BspFaceType.Polygon || f.Type == (int)BspFaceType.Mesh)
                {
                    if (f.NumMeshVerts % 3 != 0)
                        throw new BspFormatException("Face #" + i + " has a non-triangular meshvert count.");
                    for (int m = f.MeshVert; m < mEnd; m++)
                    {
                        int local = doc.MeshVerts[m];
                        if (local < 0 || local >= f.NumVertexes)
                            throw new BspFormatException("Face #" + i + " has a meshvert outside its local vertex slice.");
                    }
                }
            }
        }
    }
}
