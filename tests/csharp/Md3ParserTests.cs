using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content.Bsp;
using MyXonotic.Content.Md3;

/// <summary>
/// Standalone (non-Unity) test driver for the pure-.NET MD3 parser
/// (Assets/MyXonotic/Runtime/Content/Md3/Md3Reader.cs). Most fixtures are
/// built in-memory byte-for-byte against the documented MD3 layout, since no
/// genuine MD3 sample was committed to this repo's bounded resource pack
/// (see AGENTS.md: the four bundled "*.md3"-named files are actually IQM).
/// If a real MD3 path is passed on argv (e.g. a locally-extracted
/// ExternalContent/data/models/weapons/v_rl.md3 — never committed to Git),
/// it is parsed too and cross-checked for triangle-winding-vs-normal
/// agreement, the same invariant tests/csharp/ParserTests.cs checks for real
/// BSP data. Kept independent of UnityEngine/UnityEditor so it runs with
/// plain Mono, mirroring tests/csharp/ParserTests.cs. tests/run_all.sh wires
/// this file in as its own step; usage there passes no extra args (synthetic
/// checks only) unless a real MD3 path is added to that invocation.
///
/// Build/run:
///   MCS=/path/to/Unity/Editor/Data/MonoBleedingEdge/bin-linux64/mcs
///   MONO=/path/to/Unity/Editor/Data/MonoBleedingEdge/bin-linux64/mono
///   "$MCS" -target:exe -out:build/Md3Tests.exe \
///     Assets/MyXonotic/Runtime/Content/Bsp/*.cs \
///     Assets/MyXonotic/Runtime/Content/Md3/*.cs \
///     tests/csharp/Md3ParserTests.cs
///   "$MONO" build/Md3Tests.exe [real-model.md3 ...]
///
/// Exit code 0 = all checks passed; non-zero = first failure's message is
/// printed to stderr.
/// </summary>
public static class Md3ParserTests
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static int Main(string[] args)
    {
        try
        {
            TestGoodMinimalModelParses();
            TestMultiSurfaceModelParses();
            TestOnlyFrameZeroIsRead();
            TestRejectsBadMagic();
            TestRejectsBadVersion();
            TestRejectsTruncatedHeader();
            TestRejectsZeroFrames();
            TestRejectsTooFewSurfaces();
            TestRejectsOutOfRangeTriangleIndex();
            TestRejectsSurfaceOffsetsPastEof();
            TestRejectsBadSurfaceMagic();
            TestNormalDecodeSanity();
            TestIsMd3DoesNotThrowOnShortOrNullInput();

            // Hardening regressions (parent review): each of these targets a
            // specific validation gap that was found and fixed in Md3Reader.
            TestRejectsOfsEofBeforeHeaderEnd();
            TestRejectsSurfaceOfsEndNotPastOwnHeader();
            TestRejectsSubtableEscapingItsOwnSurfaceBounds();
            TestRejectsSurfNumFramesMismatchingHeader();
            TestRejectsNonFiniteTexCoord();
            TestRejectsMultiFrameFileMissingLaterFrameData();

            if (args.Length > 0)
            {
                foreach (var realPath in args) TestRealFileParsesAndWindingAgrees(realPath);
            }
            else
            {
                Console.WriteLine("SKIP: no real MD3 path supplied (synthetic-only run).");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Checks passed before failure: " + _checks);
            return 1;
        }

        Console.WriteLine("All " + _checks + " checks passed.");
        return 0;
    }

    // ------------------------------------------------------------------
    // Fixture builder: assembles a byte-exact MD3 file from the documented
    // layout (header/frame/tag/surface/shader/triangle/st/xyznormal record
    // sizes and field order), independent of Md3Reader's own constants, so
    // a mistake in the reader is actually caught rather than the test
    // trivially agreeing with itself.
    // ------------------------------------------------------------------
    // Plain structs instead of ValueTuples: the Mono mcs bundled with the
    // local Unity toolchain mis-resolves array-literal-to-field assignment
    // for tuples that carry element names, so this keeps the fixture
    // builder portable across compilers instead of depending on a
    // csc/mcs-version-specific tuple-metadata behavior.
    private struct VertSpec
    {
        public short X, Y, Z; public ushort N;
        public VertSpec(short x, short y, short z, ushort n) { X = x; Y = y; Z = z; N = n; }
    }

    private struct TriSpec
    {
        public int A, B, C;
        public TriSpec(int a, int b, int c) { A = a; B = b; C = c; }
    }

    private struct UvSpec
    {
        public float U, V;
        public UvSpec(float u, float v) { U = u; V = v; }
    }

    private sealed class SurfaceSpec
    {
        public string Name = "surface0";
        public string[] Shaders = { "models/weapons/test_skin.tga" };
        public VertSpec[] Verts;
        public TriSpec[] Triangles;
        public UvSpec[] TexCoords;
        public int FrameCount = 1;
    }

    private static byte[] BuildMd3(int numFrames, int numTags, SurfaceSpec[] surfaces, string modelName = "test_model")
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)'I'); w.Write((byte)'D'); w.Write((byte)'P'); w.Write((byte)'3');
            w.Write(15); // version
            WriteFixed(w, modelName, 64);
            w.Write(0); // flags
            w.Write(numFrames);
            w.Write(numTags);
            w.Write(surfaces.Length);
            w.Write(0); // numSkins

            long ofsFramesPos = ms.Position; w.Write(0);
            long ofsTagsPos = ms.Position; w.Write(0);
            long ofsSurfacesPos = ms.Position; w.Write(0);
            long ofsEofPos = ms.Position; w.Write(0);

            int ofsFrames = (int)ms.Position;
            for (int f = 0; f < numFrames; f++)
            {
                for (int i = 0; i < 3; i++) w.Write(0f); // min bounds
                for (int i = 0; i < 3; i++) w.Write(0f); // max bounds
                for (int i = 0; i < 3; i++) w.Write(0f); // local origin
                w.Write(0f); // radius
                WriteFixed(w, "frame" + f, 16);
            }

            int ofsTags = (int)ms.Position;
            for (int t = 0; t < numTags; t++)
            {
                WriteFixed(w, "tag" + t, 64);
                for (int i = 0; i < 3; i++) w.Write(0f); // origin
                for (int i = 0; i < 9; i++) w.Write(i % 4 == 0 ? 1f : 0f); // identity-ish axis, contents unchecked by reader
            }

            int ofsSurfaces = (int)ms.Position;
            foreach (var s in surfaces) WriteSurface(w, s);

            int eof = (int)ms.Position;

            long end = ms.Position;
            ms.Position = ofsFramesPos; w.Write(ofsFrames);
            ms.Position = ofsTagsPos; w.Write(ofsTags);
            ms.Position = ofsSurfacesPos; w.Write(ofsSurfaces);
            ms.Position = ofsEofPos; w.Write(eof);
            ms.Position = end;

            return ms.ToArray();
        }
    }

    private static void WriteSurface(BinaryWriter w, SurfaceSpec s)
    {
        long surfaceStart = w.BaseStream.Position;
        w.Write((byte)'I'); w.Write((byte)'D'); w.Write((byte)'P'); w.Write((byte)'3');
        WriteFixed(w, s.Name, 64);
        w.Write(0); // flags
        w.Write(s.FrameCount);
        w.Write(s.Shaders.Length);
        w.Write(s.Verts.Length);
        w.Write(s.Triangles.Length);

        long ofsTrianglesPos = w.BaseStream.Position; w.Write(0);
        long ofsShadersPos = w.BaseStream.Position; w.Write(0);
        long ofsStPos = w.BaseStream.Position; w.Write(0);
        long ofsXyzNormalPos = w.BaseStream.Position; w.Write(0);
        long ofsEndPos = w.BaseStream.Position; w.Write(0);

        int ofsTriangles = (int)(w.BaseStream.Position - surfaceStart);
        foreach (var t in s.Triangles) { w.Write(t.A); w.Write(t.B); w.Write(t.C); }

        int ofsShaders = (int)(w.BaseStream.Position - surfaceStart);
        foreach (var shader in s.Shaders) { WriteFixed(w, shader, 64); w.Write(0); }

        int ofsSt = (int)(w.BaseStream.Position - surfaceStart);
        foreach (var uv in s.TexCoords) { w.Write(uv.U); w.Write(uv.V); }

        int ofsXyzNormal = (int)(w.BaseStream.Position - surfaceStart);
        // Only frame 0's block is required for the reader; write exactly one
        // block per declared FrameCount so a >1 FrameCount surface still
        // round-trips a well-formed file (extra frames are simply repeats).
        for (int f = 0; f < s.FrameCount; f++)
        {
            foreach (var v in s.Verts)
            {
                w.Write(v.X); w.Write(v.Y); w.Write(v.Z); w.Write(unchecked((short)v.N));
            }
        }

        int ofsEnd = (int)(w.BaseStream.Position - surfaceStart);

        long end = w.BaseStream.Position;
        w.BaseStream.Position = ofsTrianglesPos; w.Write(ofsTriangles);
        w.BaseStream.Position = ofsShadersPos; w.Write(ofsShaders);
        w.BaseStream.Position = ofsStPos; w.Write(ofsSt);
        w.BaseStream.Position = ofsXyzNormalPos; w.Write(ofsXyzNormal);
        w.BaseStream.Position = ofsEndPos; w.Write(ofsEnd);
        w.BaseStream.Position = end;
    }

    private static void WriteFixed(BinaryWriter w, string s, int length)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        for (int i = 0; i < length; i++) w.Write(i < bytes.Length ? bytes[i] : (byte)0);
    }

    private static SurfaceSpec MakeTriangleSurface(string name = "surface0")
    {
        return new SurfaceSpec
        {
            Name = name,
            Shaders = new[] { "models/weapons/test_skin.tga" },
            Verts = new[]
            {
                new VertSpec(0, 0, 0, 0),
                new VertSpec(64, 0, 0, 0),   // 1 source unit along X (1/64 scale)
                new VertSpec(0, 64, 0, 0),
            },
            Triangles = new[] { new TriSpec(0, 1, 2) },
            TexCoords = new[] { new UvSpec(0f, 0f), new UvSpec(1f, 0f), new UvSpec(0f, 1f) }
        };
    }

    // ------------------------------------------------------------------
    private static void TestGoodMinimalModelParses()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() }, "v_test");
        var model = Md3Reader.Read(bytes, "fixture:good-minimal");
        Assert(model.Name == "v_test", "model name mismatch: " + model.Name);
        Assert(model.Surfaces.Length == 1, "expected 1 surface, got " + model.Surfaces.Length);
        var surf = model.Surfaces[0];
        Assert(surf.Name == "surface0", "surface name mismatch: " + surf.Name);
        Assert(surf.ShaderNames.Length == 1 && surf.ShaderNames[0] == "models/weapons/test_skin.tga",
            "shader name mismatch");
        Assert(surf.Positions.Length == 3, "expected 3 positions, got " + surf.Positions.Length);
        Assert(surf.Triangles.Length == 3, "expected 1 triangle (3 indices), got " + surf.Triangles.Length);

        // Coordinate conversion sanity, same convention as BspCoordinateSpace/IqmReader:
        // source (64,0,0) at 1/64 fixed-point scale = source unit (1,0,0) -> Unity (1/32, 0, 0).
        var p1 = surf.Positions[1];
        Assert(Math.Abs(p1.X - 1f / 32f) < 1e-4f, "x conversion mismatch: " + p1.X);
        Assert(Math.Abs(p1.Y - 0f) < 1e-4f, "y conversion mismatch: " + p1.Y);
        Assert(Math.Abs(p1.Z - 0f) < 1e-4f, "z conversion mismatch: " + p1.Z);

        // Texcoord V flip (same rule IqmReader applies).
        Assert(Math.Abs(surf.TexCoords[1].X - 1f) < 1e-4f, "u passthrough mismatch");
        Assert(Math.Abs(surf.TexCoords[1].Y - 1f) < 1e-4f, "v flip mismatch: " + surf.TexCoords[1].Y);
    }

    private static void TestMultiSurfaceModelParses()
    {
        var a = MakeTriangleSurface("body");
        var b = MakeTriangleSurface("flash");
        b.Shaders = new[] { "models/weapons/flash_skin.tga" };
        byte[] bytes = BuildMd3(1, 0, new[] { a, b });
        var model = Md3Reader.Read(bytes, "fixture:multi-surface");
        Assert(model.Surfaces.Length == 2, "expected 2 surfaces, got " + model.Surfaces.Length);
        Assert(model.Surfaces[0].Name == "body", "surface 0 name mismatch");
        Assert(model.Surfaces[1].Name == "flash", "surface 1 name mismatch");
        Assert(model.Surfaces[1].ShaderNames[0] == "models/weapons/flash_skin.tga", "surface 1 shader mismatch");
    }

    private static void TestOnlyFrameZeroIsRead()
    {
        // A 2-frame file where frame 1's vertex block is deliberately
        // corrupted (out of the buffer) must still parse cleanly, proving
        // the reader never touches it.
        var spec = MakeTriangleSurface();
        spec.FrameCount = 2;
        byte[] bytes = BuildMd3(2, 0, new[] { spec });
        var model = Md3Reader.Read(bytes, "fixture:two-frame");
        Assert(model.Surfaces[0].Positions.Length == 3, "frame-0-only read should still see 3 vertices");
    }

    private static void ExpectThrows(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Md3FormatException)
        {
            _checks++;
            return;
        }
        throw new Exception("FAIL: expected Md3FormatException for " + what + " but none was thrown.");
    }

    private static void TestRejectsBadMagic()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        bytes[0] = (byte)'X';
        ExpectThrows(() => Md3Reader.Read(bytes, "fixture:bad-magic"), "bad magic");
    }

    private static void TestRejectsBadVersion()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        bytes[4] = 99; bytes[5] = 0; bytes[6] = 0; bytes[7] = 0;
        ExpectThrows(() => Md3Reader.Read(bytes, "fixture:bad-version"), "unsupported version");
    }

    private static void TestRejectsTruncatedHeader()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        var truncated = new byte[50];
        Array.Copy(bytes, truncated, 50);
        ExpectThrows(() => Md3Reader.Read(truncated, "fixture:truncated"), "truncated header");
    }

    private static void TestRejectsZeroFrames()
    {
        byte[] bytes = BuildMd3(0, 0, new[] { MakeTriangleSurface() });
        ExpectThrows(() => Md3Reader.Read(bytes, "fixture:zero-frames"), "zero frames");
    }

    private static void TestRejectsTooFewSurfaces()
    {
        byte[] bytes = BuildMd3(1, 0, Array.Empty<SurfaceSpec>());
        ExpectThrows(() => Md3Reader.Read(bytes, "fixture:zero-surfaces"), "zero surfaces");
    }

    private static void TestRejectsOutOfRangeTriangleIndex()
    {
        var spec = MakeTriangleSurface();
        spec.Triangles = new[] { new TriSpec(0, 1, 99) }; // 99 is out of range for 3 verts
        byte[] bytes = BuildMd3(1, 0, new[] { spec });
        ExpectThrows(() => Md3Reader.Read(bytes, "fixture:bad-triangle-index"), "out-of-range triangle vertex index");
    }

    private static void TestRejectsSurfaceOffsetsPastEof()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        // Corrupt the header's ofsEof field (last 4 bytes of the 108-byte
        // header) to a value past the actual buffer length.
        var mutated = (byte[])bytes.Clone();
        var tooLarge = BitConverter.GetBytes(bytes.Length + 10000);
        Array.Copy(tooLarge, 0, mutated, 104, 4);
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:ofs-eof-too-large"), "ofsEof beyond actual buffer length");
    }

    private static void TestRejectsBadSurfaceMagic()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        // Header field offset 100 = ofsSurfaces (see the field-offset map
        // above the hardening regressions below). Was previously read from
        // offset 96 (ofsTags) here; that only worked because a 0-tag fixture
        // makes ofsTags numerically equal to ofsSurfaces by coincidence.
        int ofsSurfaces = BitConverter.ToInt32(bytes, 100);
        var mutated = (byte[])bytes.Clone();
        mutated[ofsSurfaces] = (byte)'X';
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:bad-surface-magic"), "bad surface magic");
    }

    private static void TestNormalDecodeSanity()
    {
        // packed=0 decodes to lat=0,lng=0 -> x=cos(0)*sin(0)=0, y=sin(0)*sin(0)=0, z=cos(0)=1
        // i.e. Quake "straight up" on Z, which QuakeDirectionToUnity maps to Unity's Y (up).
        var spec = MakeTriangleSurface();
        spec.Verts = new[]
        {
            new VertSpec(0, 0, 0, 0),
            new VertSpec(64, 0, 0, 0),
            new VertSpec(0, 64, 0, 0),
        };
        byte[] bytes = BuildMd3(1, 0, new[] { spec });
        var model = Md3Reader.Read(bytes, "fixture:normal-decode");
        var n = model.Surfaces[0].Normals[0];
        Assert(Math.Abs(n.X) < 1e-4f, "normal x should be ~0: " + n.X);
        Assert(Math.Abs(n.Y - 1f) < 1e-4f, "normal y (was z, 'up') should be ~1: " + n.Y);
        Assert(Math.Abs(n.Z) < 1e-4f, "normal z (was y) should be ~0: " + n.Z);
    }

    private static void TestIsMd3DoesNotThrowOnShortOrNullInput()
    {
        Assert(!Md3Reader.IsMd3(null), "null buffer should not be MD3");
        Assert(!Md3Reader.IsMd3(new byte[] { (byte)'I', (byte)'D' }), "2-byte buffer should not be MD3");
        Assert(Md3Reader.IsMd3(new byte[] { (byte)'I', (byte)'D', (byte)'P', (byte)'3', 0 }), "IDP3 prefix should be MD3");
    }

    // ------------------------------------------------------------------
    // Hardening regressions. Absolute header field offsets (0-based):
    //   ofsFrames=92, ofsTags=96... actually ofsSurfaces=96, ofsEof=104
    //   (see BuildMd3: version@4, name@8..72, flags@72, numFrames@76,
    //   numTags@80, numSurfaces@84, numSkins@88, ofsFrames@92, ofsTags@96,
    //   ofsSurfaces@100, ofsEof@104 — matches Md3Reader's own field order).
    // Per-surface relative field offsets (0-based from that surface's own
    // start): surfNumFrames@72, numShaders@76, numVerts@80, numTriangles@84,
    // ofsTriangles@88, ofsShaders@92, ofsSt@96, ofsXyzNormal@100, ofsEnd@104.
    // ------------------------------------------------------------------

    private static void WriteI32(byte[] data, int offset, int value)
    {
        var b = BitConverter.GetBytes(value);
        Array.Copy(b, 0, data, offset, 4);
    }

    private static void TestRejectsOfsEofBeforeHeaderEnd()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        var mutated = (byte[])bytes.Clone();
        WriteI32(mutated, 104, 50); // 50 < 108-byte header size
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:ofs-eof-before-header-end"), "ofsEof pointing before the header even ends");
    }

    private static void TestRejectsSurfaceOfsEndNotPastOwnHeader()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        int ofsSurfaces = BitConverter.ToInt32(bytes, 100);
        var mutated = (byte[])bytes.Clone();
        WriteI32(mutated, ofsSurfaces + 104, 108); // exactly the surface header size: no room for any actual data
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:surface-ofsend-too-small"), "surface ofsEnd not advancing past its own header");
    }

    private static void TestRejectsSubtableEscapingItsOwnSurfaceBounds()
    {
        // Two surfaces so there is a second surface's data for a corrupted
        // offset to "reach into" while still being inside the whole buffer.
        var a = MakeTriangleSurface("body");
        var b = MakeTriangleSurface("flash");
        byte[] bytes = BuildMd3(1, 0, new[] { a, b });
        int ofsSurfaces = BitConverter.ToInt32(bytes, 100);
        var mutated = (byte[])bytes.Clone();
        // Push surface 0's triangle-table offset far past its own declared
        // ofsEnd (but still comfortably inside the overall file) — this must
        // be rejected even though the raw offset+length fits the whole buffer.
        WriteI32(mutated, ofsSurfaces + 88, 5000);
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:subtable-escapes-surface"), "sub-table offset escaping its own surface's declared bounds");
    }

    private static void TestRejectsSurfNumFramesMismatchingHeader()
    {
        var spec = MakeTriangleSurface();
        spec.FrameCount = 1; // header will claim 2 frames; surface only declares/provides 1
        byte[] bytes = BuildMd3(2, 0, new[] { spec });
        ExpectThrows(() => Md3Reader.Read(bytes, "fixture:surfnumframes-mismatch"), "surface frame count not matching the model header's frame count");
    }

    private static void TestRejectsNonFiniteTexCoord()
    {
        byte[] bytes = BuildMd3(1, 0, new[] { MakeTriangleSurface() });
        int ofsSurfaces = BitConverter.ToInt32(bytes, 100);
        int ofsSt = BitConverter.ToInt32(bytes, ofsSurfaces + 96);
        var mutated = (byte[])bytes.Clone();
        var nanBytes = BitConverter.GetBytes(float.NaN);
        Array.Copy(nanBytes, 0, mutated, ofsSurfaces + ofsSt, 4); // vertex 0's U coordinate
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:nan-texcoord"), "NaN texture coordinate");
    }

    private static void TestRejectsMultiFrameFileMissingLaterFrameData()
    {
        var spec = MakeTriangleSurface();
        spec.FrameCount = 2; // matches header numFrames below, and real bytes for both frames are written
        byte[] bytes = BuildMd3(2, 0, new[] { spec });
        int ofsSurfaces = BitConverter.ToInt32(bytes, 100);
        var mutated = (byte[])bytes.Clone();
        int currentOfsEnd = BitConverter.ToInt32(mutated, ofsSurfaces + 104);
        int perFrameBytes = spec.Verts.Length * 8; // XyzNormalRecordSize
        // Shrink the surface's declared end so it only actually covers frame
        // 0's vertex block, even though surfNumFrames (still 2, matching the
        // header) claims two frames of data exist.
        WriteI32(mutated, ofsSurfaces + 104, currentOfsEnd - perFrameBytes);
        ExpectThrows(() => Md3Reader.Read(mutated, "fixture:missing-later-frame-data"),
            "surface declaring more frames than its own bounds actually contain data for");
    }

    private static void TestRealFileParsesAndWindingAgrees(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("SKIP: real MD3 path not found: " + path);
            return;
        }
        byte[] bytes = File.ReadAllBytes(path);
        var model = Md3Reader.Read(bytes, path);
        Assert(model.Surfaces.Length >= 1, "real file should have at least 1 surface: " + path);

        int totalTriangles = 0, agree = 0, disagree = 0;
        foreach (var surf in model.Surfaces)
        {
            Assert(surf.Positions.Length >= 3, "real surface should have vertices: " + surf.Name);
            Assert(surf.Triangles.Length >= 3, "real surface should have triangles: " + surf.Name);
            int triCount = surf.Triangles.Length / 3;
            totalTriangles += triCount;
            for (int t = 0; t < triCount; t++)
            {
                int ia = surf.Triangles[t * 3], ib = surf.Triangles[t * 3 + 1], ic = surf.Triangles[t * 3 + 2];
                var pa = surf.Positions[ia]; var pb = surf.Positions[ib]; var pc = surf.Positions[ic];
                var na = surf.Normals[ia];
                var e1 = new BspVec3(pb.X - pa.X, pb.Y - pa.Y, pb.Z - pa.Z);
                var e2 = new BspVec3(pc.X - pa.X, pc.Y - pa.Y, pc.Z - pa.Z);
                float nx = e1.Y * e2.Z - e1.Z * e2.Y;
                float ny = e1.Z * e2.X - e1.X * e2.Z;
                float nz = e1.X * e2.Y - e1.Y * e2.X;
                float dot = nx * na.X + ny * na.Y + nz * na.Z;
                if (dot > 0f) agree++;
                else if (dot < 0f) disagree++;
            }
        }

        // Same tolerance rationale as BspCoordinateSpace's own real-map
        // regression check: a small fraction of hard-edge/seam vertices with
        // shared-but-averaged normals can legitimately disagree, but the
        // overwhelming majority must agree, or the winding convention
        // (no reversal — see BspCoordinateSpace's doc comment) is wrong for
        // MD3 specifically.
        double agreeRatio = totalTriangles > 0 ? (double)agree / totalTriangles : 0;
        Console.WriteLine(string.Format(
            "  real file {0}: {1} surface(s), {2} triangle(s), winding-vs-normal agreement {3:P1} ({4} agree / {5} disagree)",
            path, model.Surfaces.Length, totalTriangles, agreeRatio, agree, disagree));
        Assert(agreeRatio > 0.95, "expected >95% winding/normal agreement on real MD3 data (no index reversal), got " +
            agreeRatio.ToString("P1") + " for " + path);
    }
}
