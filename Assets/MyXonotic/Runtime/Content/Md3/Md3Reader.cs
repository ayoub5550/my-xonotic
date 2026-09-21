using System;
using MyXonotic.Content.Bsp;

namespace MyXonotic.Content.Md3
{
    /// <summary>
    /// Minimal, bounded, clean-room reader for the publicly documented Quake
    /// III-family MD3 model binary layout (fixed-size header + frame table +
    /// tag table + a small list of surfaces, each with its own shader/
    /// triangle/texcoord/vertex sub-tables). Written from the format's
    /// well-known public description (field order/sizes of the header, frame,
    /// tag, surface, shader, triangle, st and xyznormal records, and the
    /// lat/long-encoded vertex normal scheme) — no GPL engine or QuakeC
    /// source was read or copied to write it, matching the same clean-room
    /// approach IqmReader documents for IQM.
    ///
    /// Deliberately static-first (see <see cref="Md3StaticModel"/>): only
    /// frame 0 of each surface's vertex-animated geometry is decoded. Tags
    /// (attachment points) are not parsed; nothing downstream consumes them
    /// yet. Every offset/count is bounds-checked against the actual buffer
    /// length and a generous-but-finite sanity ceiling before use, so a
    /// malformed or hostile file throws <see cref="Md3FormatException"/>
    /// instead of reading out of range or exhausting memory.
    /// </summary>
    public static class Md3Reader
    {
        public const int SupportedVersion = 15;
        const string Magic = "IDP3";

        const int HeaderSize = 4 + 4 + 64 + 4 + 4 + 4 + 4 + 4 + 4 + 4 + 4 + 4; // 108
        const int FrameSize = 12 + 12 + 12 + 4 + 16;                          // 56
        const int SurfaceHeaderSize = 4 + 64 + 4 + 4 + 4 + 4 + 4 + 4 + 4 + 4 + 4 + 4; // 108
        const int ShaderRecordSize = 64 + 4;
        const int TriangleRecordSize = 12;
        const int StRecordSize = 8;
        const int XyzNormalRecordSize = 8;

        // Generous ceilings for a legitimate model file, far below anything
        // that would exhaust memory or overflow int math from a hostile count.
        const int MaxFrames = 1 << 12;
        const int MaxTags = 1 << 12;
        const int MaxSurfaces = 1 << 8;
        const int MaxVertsPerSurface = 1 << 16;
        const int MaxTrianglesPerSurface = 1 << 17;
        const int MaxShadersPerSurface = 1 << 10;

        /// <summary>True when the buffer starts with MD3's "IDP3" magic (4 bytes, no version check).</summary>
        public static bool IsMd3(byte[] data)
        {
            return data != null && data.Length >= 4 &&
                   data[0] == (byte)'I' && data[1] == (byte)'D' && data[2] == (byte)'P' && data[3] == (byte)'3';
        }

        public static Md3StaticModel Read(byte[] data, string sourcePathForErrors)
        {
            if (data == null) throw new Md3FormatException("MD3 data buffer is null: " + sourcePathForErrors);
            if (data.Length < HeaderSize)
                throw new Md3FormatException("File too short for an MD3 header: " + sourcePathForErrors);
            if (!IsMd3(data))
                throw new Md3FormatException("Not an MD3 file (magic mismatch, expected \"IDP3\"): " + sourcePathForErrors);

            int p = 4;
            int version = ReadI32(data, p); p += 4;
            if (version != SupportedVersion)
                throw new Md3FormatException("Unsupported MD3 version " + version + " (expected " + SupportedVersion + "): " + sourcePathForErrors);

            string name = ReadFixedString(data, p, 64); p += 64;
            p += 4; // flags, unused
            int numFrames = ReadI32(data, p); p += 4;
            int numTags = ReadI32(data, p); p += 4;
            int numSurfaces = ReadI32(data, p); p += 4;
            p += 4; // numSkins, unused: per-model skin list, superseded by per-surface shaders
            int ofsFrames = ReadI32(data, p); p += 4;
            int ofsTags = ReadI32(data, p); p += 4;
            int ofsSurfaces = ReadI32(data, p); p += 4;
            int ofsEof = ReadI32(data, p); p += 4;

            if (numFrames < 1) throw new Md3FormatException("MD3 file declares zero frames; needs at least frame 0: " + sourcePathForErrors);
            if (numFrames > MaxFrames) throw new Md3FormatException("MD3 numFrames " + numFrames + " exceeds sanity ceiling: " + sourcePathForErrors);
            if (numTags < 0 || numTags > MaxTags) throw new Md3FormatException("MD3 numTags " + numTags + " out of sane range: " + sourcePathForErrors);
            if (numSurfaces < 1 || numSurfaces > MaxSurfaces) throw new Md3FormatException("MD3 numSurfaces " + numSurfaces + " out of sane range (need at least 1): " + sourcePathForErrors);
            // ofsEof marks the end of file content; it can never legitimately
            // point before the fixed header it follows, and never past the
            // actual buffer.
            if (ofsEof < HeaderSize || ofsEof > data.Length)
                throw new Md3FormatException("MD3 header ofsEof (" + ofsEof + ") is out of range (must be >= header size " +
                    HeaderSize + " and <= buffer length " + data.Length + "): " + sourcePathForErrors);

            // Frame 0's bounding data is not consumed (RecalculateBounds() at
            // the Editor boundary covers it), but the table itself must exist
            // and be in range for the file to be well-formed.
            RequireRange(data, ofsFrames, (long)numFrames * FrameSize, "frame table", sourcePathForErrors);
            RequireRange(data, ofsTags, (long)numTags * 112, "tag table", sourcePathForErrors);

            var surfaces = new Md3Surface[numSurfaces];
            int surfaceOffset = ofsSurfaces;
            for (int s = 0; s < numSurfaces; s++)
            {
                RequireRange(data, surfaceOffset, SurfaceHeaderSize, "surface header", sourcePathForErrors);
                if (!IsMd3(SliceMagic(data, surfaceOffset)))
                    throw new Md3FormatException("Surface " + s + " has wrong magic (expected \"IDP3\"): " + sourcePathForErrors);

                int sp = surfaceOffset + 4;
                string surfaceName = ReadFixedString(data, sp, 64); sp += 64;
                sp += 4; // surface flags, unused
                int surfNumFrames = ReadI32(data, sp); sp += 4; // per-surface frame count; must match numFrames in a well-formed file, but only frame 0 is read regardless
                int numShaders = ReadI32(data, sp); sp += 4;
                int numVerts = ReadI32(data, sp); sp += 4;
                int numTriangles = ReadI32(data, sp); sp += 4;
                int ofsTriangles = ReadI32(data, sp); sp += 4;
                int ofsShaders = ReadI32(data, sp); sp += 4;
                int ofsSt = ReadI32(data, sp); sp += 4;
                int ofsXyzNormal = ReadI32(data, sp); sp += 4;
                int ofsEnd = ReadI32(data, sp); sp += 4;

                if (surfNumFrames < 1) throw new Md3FormatException("Surface " + s + " declares zero frames: " + sourcePathForErrors);
                if (surfNumFrames > MaxFrames)
                    throw new Md3FormatException("Surface " + s + " surfNumFrames " + surfNumFrames + " exceeds sanity ceiling: " + sourcePathForErrors);
                // A well-formed MD3 file always gives every surface the same
                // frame count as the model header (that IS the model's frame
                // count — MD3 has no separate per-surface animation). A
                // mismatch means either corruption or a hostile file trying to
                // claim fewer frames exist for bounds-check purposes than are
                // actually read elsewhere; reject rather than guess which
                // count is "real".
                if (surfNumFrames != numFrames)
                    throw new Md3FormatException("Surface " + s + " surfNumFrames (" + surfNumFrames +
                        ") does not match the model header's numFrames (" + numFrames + "): " + sourcePathForErrors);
                if (numShaders < 0 || numShaders > MaxShadersPerSurface)
                    throw new Md3FormatException("Surface " + s + " numShaders " + numShaders + " out of sane range: " + sourcePathForErrors);
                if (numVerts < 3 || numVerts > MaxVertsPerSurface)
                    throw new Md3FormatException("Surface " + s + " numVerts " + numVerts + " out of sane range: " + sourcePathForErrors);
                if (numTriangles < 1 || numTriangles > MaxTrianglesPerSurface)
                    throw new Md3FormatException("Surface " + s + " numTriangles " + numTriangles + " out of sane range: " + sourcePathForErrors);
                // ofsEnd is this surface's own declared length (next surface's
                // record starts at surfaceOffset + ofsEnd). It must place the
                // next surface strictly after this surface's own fixed header
                // (zero/negative/too-small would either loop forever re-reading
                // the same header or let a later surface's tables overlap this
                // one's), and the resulting absolute offset must still fit in
                // the buffer.
                if (ofsEnd <= SurfaceHeaderSize)
                    throw new Md3FormatException("Surface " + s + " ofsEnd (" + ofsEnd +
                        ") does not leave room for its own " + SurfaceHeaderSize + "-byte header (would not advance " +
                        "past it, risking a zero-progress loop or overlap): " + sourcePathForErrors);
                if ((long)surfaceOffset + ofsEnd > data.Length)
                    throw new Md3FormatException("Surface " + s + " ofsEnd exceeds actual buffer length: " + sourcePathForErrors);
                long surfaceDataEnd = (long)surfaceOffset + ofsEnd;

                // All of a surface's own sub-table offsets are relative to
                // THIS surface's start, not the file start — a well-known MD3
                // gotcha; every derived absolute offset is still bounds
                // checked below, both against the actual buffer AND against
                // this surface's own declared end (surfaceDataEnd), so a
                // surface cannot claim a small ofsEnd while quietly pointing
                // its sub-tables into a neighboring surface's data or past EOF.
                long absShaders = (long)surfaceOffset + ofsShaders;
                long absTriangles = (long)surfaceOffset + ofsTriangles;
                long absSt = (long)surfaceOffset + ofsSt;
                long absXyzNormal = (long)surfaceOffset + ofsXyzNormal;

                var shaderNames = new string[numShaders];
                RequireRangeWithin(data, absShaders, (long)numShaders * ShaderRecordSize, surfaceDataEnd, "surface " + s + " shader table", sourcePathForErrors);
                for (int i = 0; i < numShaders; i++)
                {
                    shaderNames[i] = ReadFixedString(data, (int)(absShaders + (long)i * ShaderRecordSize), 64);
                }

                RequireRangeWithin(data, absTriangles, (long)numTriangles * TriangleRecordSize, surfaceDataEnd, "surface " + s + " triangle table", sourcePathForErrors);
                var triangles = new int[numTriangles * 3];
                for (int t = 0; t < numTriangles; t++)
                {
                    int o = (int)(absTriangles + (long)t * TriangleRecordSize);
                    int a = ReadI32(data, o), b = ReadI32(data, o + 4), c = ReadI32(data, o + 8);
                    if (a < 0 || a >= numVerts || b < 0 || b >= numVerts || c < 0 || c >= numVerts)
                        throw new Md3FormatException("Surface " + s + " triangle " + t + " references an out-of-range vertex index: " + sourcePathForErrors);
                    triangles[t * 3 + 0] = a;
                    triangles[t * 3 + 1] = b;
                    triangles[t * 3 + 2] = c;
                }

                RequireRangeWithin(data, absSt, (long)numVerts * StRecordSize, surfaceDataEnd, "surface " + s + " texcoord table", sourcePathForErrors);
                var texCoords = new BspVec2[numVerts];
                for (int v = 0; v < numVerts; v++)
                {
                    int o = (int)(absSt + (long)v * StRecordSize);
                    float u = ReadF32(data, o), vv = ReadF32(data, o + 4);
                    if (!IsFiniteCoordinate(u) || !IsFiniteCoordinate(vv))
                        throw new Md3FormatException("Surface " + s + " vertex " + v + " has a non-finite (NaN/Infinity) texture coordinate: " + sourcePathForErrors);
                    // Same OpenGL-vs-Unity V flip IqmReader applies to IQM texcoords.
                    texCoords[v] = new BspVec2(u, 1f - vv);
                }

                // The file must actually contain every frame of vertex data it
                // claims (surfNumFrames, already required == numFrames above),
                // even though only frame 0 is decoded below — a file that
                // claims N frames but only backs frame 0 with real data is
                // still malformed and must be rejected, not silently accepted
                // because this importer happens not to read the rest yet.
                RequireRangeWithin(data, absXyzNormal, (long)numVerts * surfNumFrames * XyzNormalRecordSize, surfaceDataEnd,
                    "surface " + s + " full (all-frames) vertex table", sourcePathForErrors);
                var positions = new BspVec3[numVerts];
                var normals = new BspVec3[numVerts];
                const float xyzScale = 1f / 64f; // MD3_XYZ_SCALE: vertex shorts are fixed-point, 1 unit = 1/64 source unit
                for (int v = 0; v < numVerts; v++)
                {
                    int o = (int)(absXyzNormal + (long)v * XyzNormalRecordSize);
                    short sx = ReadI16(data, o), sy = ReadI16(data, o + 2), sz = ReadI16(data, o + 4);
                    ushort packedNormal = (ushort)ReadI16(data, o + 6);
                    var raw = new BspVec3(sx * xyzScale, sy * xyzScale, sz * xyzScale);
                    positions[v] = BspCoordinateSpace.QuakeToUnity(raw);
                    normals[v] = BspCoordinateSpace.QuakeDirectionToUnity(DecodeNormal(packedNormal));
                }

                surfaces[s] = new Md3Surface
                {
                    Name = surfaceName,
                    ShaderNames = shaderNames,
                    Positions = positions,
                    Normals = normals,
                    TexCoords = texCoords,
                    Triangles = triangles
                };

                surfaceOffset += ofsEnd;
            }

            return new Md3StaticModel { Name = name, Surfaces = surfaces };
        }

        /// <summary>
        /// MD3's compressed vertex normal: latitude/longitude each quantized
        /// to a byte, packed high-byte-latitude/low-byte-longitude into one
        /// 16-bit value, matching the format's well-known public decode
        /// (lat/lng each scaled by 2*pi/255; x=cos(lat)*sin(lng), y=sin(lat)*sin(lng), z=cos(lng)).
        /// </summary>
        static BspVec3 DecodeNormal(ushort packed)
        {
            double lat = ((packed >> 8) & 0xff) * (2.0 * Math.PI / 255.0);
            double lng = (packed & 0xff) * (2.0 * Math.PI / 255.0);
            float x = (float)(Math.Cos(lat) * Math.Sin(lng));
            float y = (float)(Math.Sin(lat) * Math.Sin(lng));
            float z = (float)Math.Cos(lng);
            return new BspVec3(x, y, z);
        }

        static byte[] SliceMagic(byte[] data, int offset)
        {
            // Cheap 4-byte peek reusing IsMd3's own comparison; avoids a second bounds-check helper.
            return new[] { data[offset], data[offset + 1], data[offset + 2], data[offset + 3] };
        }

        static void RequireRange(byte[] data, long offset, long length, string what, string sourcePathForErrors)
        {
            if (offset < 0 || length < 0 || offset > data.Length || length > data.Length || offset + length > data.Length)
                throw new Md3FormatException(string.Format(
                    "MD3 {0} out of range (offset {1}, length {2}, file {3} bytes): {4}",
                    what, offset, length, data.Length, sourcePathForErrors));
        }

        /// <summary>
        /// Same as <see cref="RequireRange"/>, but additionally requires the
        /// read to stay within <paramref name="ceiling"/> (a surface's own
        /// declared end, computed from its ofsEnd) rather than only the whole
        /// file buffer. Without this, a surface could declare a small ofsEnd
        /// (so it looks tiny/cheap) while its shader/triangle/st/vertex
        /// offsets actually point past that into a neighboring surface's
        /// records or into unrelated file tail data.
        /// </summary>
        static void RequireRangeWithin(byte[] data, long offset, long length, long ceiling, string what, string sourcePathForErrors)
        {
            RequireRange(data, offset, length, what, sourcePathForErrors);
            if (offset < 0 || length < 0 || offset + length > ceiling)
                throw new Md3FormatException(string.Format(
                    "MD3 {0} escapes its own surface's declared bounds (offset {1}, length {2}, surface end {3}): {4}",
                    what, offset, length, ceiling, sourcePathForErrors));
        }

        static bool IsFiniteCoordinate(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static int ReadI32(byte[] data, int offset)
        {
            CheckPrimitiveRange(data, offset, 4);
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
        }

        static short ReadI16(byte[] data, int offset)
        {
            CheckPrimitiveRange(data, offset, 2);
            return (short)(data[offset] | (data[offset + 1] << 8));
        }

        static float ReadF32(byte[] data, int offset)
        {
            CheckPrimitiveRange(data, offset, 4);
            return BitConverter.ToSingle(data, offset);
        }

        static string ReadFixedString(byte[] data, int offset, int maxLength)
        {
            CheckPrimitiveRange(data, offset, maxLength);
            int len = 0;
            while (len < maxLength && data[offset + len] != 0) len++;
            return System.Text.Encoding.ASCII.GetString(data, offset, len);
        }

        static void CheckPrimitiveRange(byte[] data, int offset, int size)
        {
            if (offset < 0 || size < 0 || (long)offset + size > data.Length)
                throw new Md3FormatException(string.Format(
                    "MD3 primitive read out of bounds (offset {0}, size {1}, file {2} bytes).", offset, size, data.Length));
        }
    }
}
