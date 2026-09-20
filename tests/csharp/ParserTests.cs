using System;
using System.IO;
using System.Linq;
using MyXonotic.Content.Bsp;

/// <summary>
/// Standalone (non-Unity) test driver for the pure-.NET BSP parser. Run
/// with Mono against the fixture files produced by
/// tools/content/fixture_bsp.py (see tests/run_all.sh). Kept
/// independent of UnityEngine/UnityEditor so it can execute with
/// `mono`/`mcs` directly, without booting the Unity Editor.
///
/// Exit code 0 = all checks passed; non-zero = first failure's message is
/// printed to stderr.
/// </summary>
public static class ParserTests
{
    private static int _checks;

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition)
        {
            throw new Exception("FAIL: " + message);
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: ParserTests <fixturesDir> [external-map.bsp ...]");
            return 2;
        }
        string dir = args[0];

        try
        {
            TestGoodFixtureParses(Path.Combine(dir, "good.bsp"));
            TestShaderFieldOrder(Path.Combine(dir, "shader_asymmetry.bsp"));
            TestRejectsBadMagic(Path.Combine(dir, "bad_magic.bsp"));
            TestRejectsUnsupportedVersion(Path.Combine(dir, "unsupported_version.bsp"));
            TestRejectsTruncatedHeader(Path.Combine(dir, "truncated_header.bsp"));
            TestRejectsHugeLumpAttack(Path.Combine(dir, "huge_lump_attack.bsp"));
            TestRejectsNanVertex(Path.Combine(dir, "nan_vertex_attack.bsp"));
            TestSafetyAndSemantics(Path.Combine(dir, "good.bsp"));

            if (args.Length > 1)
                foreach (var external in args.Skip(1)) TestRealMapParses(external);
            else
            {
                Console.WriteLine("SKIP: no external maps supplied (synthetic-only run).");
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

    private static byte[] ReadFixture(string path)
    {
        if (!File.Exists(path))
        {
            throw new Exception("Missing fixture: " + path + " (run tools/content/pk3_tool.py fixture first)");
        }
        return File.ReadAllBytes(path);
    }

    private static void TestGoodFixtureParses(string path)
    {
        var doc = BspReader.Read(ReadFixture(path));
        Assert(doc.Version == 46, "version should be 46");
        Assert(doc.Entities.Count == 2, "expected 2 entities, got " + doc.Entities.Count);
        Assert(doc.Entities[0].Get("classname") == "worldspawn", "entity 0 should be worldspawn");
        Assert(doc.Entities[1].Get("classname") == "info_player_deathmatch", "entity 1 should be info_player_deathmatch");
        Assert(doc.Shaders.Length == 1, "expected 1 shader");
        Assert(doc.Shaders[0].Name == "myx/fixture_floor", "shader name mismatch: " + doc.Shaders[0].Name);
        Assert(doc.Vertexes.Length == 4 + 9, "expected 13 vertices, got " + doc.Vertexes.Length);
        Assert(doc.Faces.Length == 2, "expected 2 faces, got " + doc.Faces.Length);
        Assert(doc.Faces[0].Type == (int)BspFaceType.Polygon, "face 0 should be polygon");
        Assert(doc.Faces[1].Type == (int)BspFaceType.Patch, "face 1 should be patch");
        Assert(doc.Models.Length == 2, "expected 2 models (worldspawn + 1 inline submodel)");

        var warnings = new System.Collections.Generic.List<string>(doc.Warnings);
        var mesh = BspGeometryBuilder.BuildModel(doc, 0, warnings);
        // Quad face contributes exactly 6 indices (2 triangles); the patch
        // face contributes PatchTessellationLevel^2 quads * 2 tris * 3 idx.
        int level = BspGeometryBuilder.PatchTessellationLevel;
        int expectedPatchIndices = level * level * 2 * 3;
        Assert(mesh.Triangles.Count == 6 + expectedPatchIndices,
            "expected 6 (quad) + " + expectedPatchIndices + " (patch) = " + (6 + expectedPatchIndices) +
            " triangle indices, got " + mesh.Triangles.Count);
        Assert(mesh.Positions.Count > 4, "patch tessellation should add more vertices than the raw 4+9 control points");
        Assert(mesh.CollisionTriangles.Count > 0, "solid shader should produce collision triangles");

        // Model 1 (inline submodel) must be buildable in isolation and must
        // NOT have been merged into model 0's output above (model 0 mesh
        // triangle count already checked as exactly the quad and patch indices,
        // proving the empty submodel contributed nothing extra).
        var submodelWarnings = new System.Collections.Generic.List<string>();
        var submodelMesh = BspGeometryBuilder.BuildModel(doc, 1, submodelWarnings);
        Assert(submodelMesh.Triangles.Count == 0, "inline submodel with 0 faces should yield 0 triangles");

        // Winding/handedness sanity: the quad is a Quake floor (normal +Z,
        // "up"). After BspCoordinateSpace.QuakeToUnity swaps Y/Z, "up" in
        // Unity is +Y. If winding reversal is correct, the converted
        // triangle's face normal (right-hand rule on its *emitted* index
        // order) must also point toward +Y, not -Y.
        {
            int ia = mesh.Triangles[0], ib = mesh.Triangles[1], ic = mesh.Triangles[2];
            var pa = mesh.Positions[ia]; var pb = mesh.Positions[ib]; var pc = mesh.Positions[ic];
            var e1 = new BspVec3(pb.X - pa.X, pb.Y - pa.Y, pb.Z - pa.Z);
            var e2 = new BspVec3(pc.X - pa.X, pc.Y - pa.Y, pc.Z - pa.Z);
            float nx = e1.Y * e2.Z - e1.Z * e2.Y;
            float ny = e1.Z * e2.X - e1.X * e2.Z;
            float nz = e1.X * e2.Y - e1.Y * e2.X;
            Assert(ny > 0f, string.Format(
                "expected winding-reversed floor triangle to face +Y (up) in Unity space, got normal ({0},{1},{2})", nx, ny, nz));
        }

        // Coordinate conversion sanity: quad vertex (64,64,0) in Quake space
        // (x,y,z) becomes Unity (x,z,y)/32 = (2, 0, 2).
        var converted = BspCoordinateSpace.QuakeToUnity(new BspVec3(64, 64, 0));
        Assert(Math.Abs(converted.X - 2f) < 1e-4f, "x conversion mismatch");
        Assert(Math.Abs(converted.Y - 0f) < 1e-4f, "y (was z) conversion mismatch");
        Assert(Math.Abs(converted.Z - 2f) < 1e-4f, "z (was y) conversion mismatch");
    }

    private static void TestShaderFieldOrder(string path)
    {
        // Regression test for a real bug found during review: shader_t
        // field order is { name; surfaceFlags; contentFlags }, not the
        // reverse. Two asymmetric, non-zero shaders make a byte-swap
        // regression fail loudly instead of silently.
        var doc = BspReader.Read(ReadFixture(path));
        Assert(doc.Shaders.Length == 2, "expected 2 shaders");
        Assert(doc.Shaders[0].ContentFlags == 0x1, "shader 0 contentFlags should be CONTENTS_SOLID(0x1), got 0x" + doc.Shaders[0].ContentFlags.ToString("X"));
        Assert(doc.Shaders[0].SurfaceFlags == 0x0, "shader 0 surfaceFlags should be 0, got 0x" + doc.Shaders[0].SurfaceFlags.ToString("X"));
        Assert(doc.Shaders[1].ContentFlags == 0x0, "shader 1 contentFlags should be 0, got 0x" + doc.Shaders[1].ContentFlags.ToString("X"));
        Assert(doc.Shaders[1].SurfaceFlags == 0x80, "shader 1 surfaceFlags should be SURF_NODRAW(0x80), got 0x" + doc.Shaders[1].SurfaceFlags.ToString("X"));

        var warnings = new System.Collections.Generic.List<string>(doc.Warnings);
        var mesh = BspGeometryBuilder.BuildModel(doc, 0, warnings);
        // Face 0 (solid_only) draws + collides; face 1 (nodraw_only) neither draws nor collides.
        Assert(mesh.Triangles.Count == 3, "only the solid_only face should draw, got " + (mesh.Triangles.Count / 3) + " tris");
        Assert(mesh.CollisionTriangles.Count == 3, "only the solid_only face should collide, got " + (mesh.CollisionTriangles.Count / 3) + " tris");
    }

    private static void ExpectThrows(Action action, string what)
    {
        try
        {
            action();
        }
        catch (BspFormatException)
        {
            _checks++;
            return;
        }
        throw new Exception("FAIL: expected BspFormatException for " + what + " but none was thrown.");
    }

    private static void TestRejectsBadMagic(string path)
    {
        ExpectThrows(() => BspReader.Read(ReadFixture(path)), "bad magic");
    }

    private static void TestRejectsUnsupportedVersion(string path)
    {
        ExpectThrows(() => BspReader.Read(ReadFixture(path)), "unsupported version");
    }

    private static void TestRejectsTruncatedHeader(string path)
    {
        ExpectThrows(() => BspReader.Read(ReadFixture(path)), "truncated header");
    }

    private static void TestRejectsHugeLumpAttack(string path)
    {
        ExpectThrows(() => BspReader.Read(ReadFixture(path)), "huge/out-of-bounds lump length");
    }

    private static void TestRejectsNanVertex(string path)
    {
        ExpectThrows(() => BspReader.Read(ReadFixture(path)), "NaN vertex coordinate");
    }

    private static int LumpOffset(byte[] bytes, int lump) => BitConverter.ToInt32(bytes, 8 + lump * 8);

    private static void MutateAndReject(byte[] original, int offset, int value, string label)
    {
        var mutated = (byte[])original.Clone();
        var encoded = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(encoded);
        Array.Copy(encoded, 0, mutated, offset, 4);
        ExpectThrows(() => BspReader.Read(mutated), label);
    }

    private static void TestSafetyAndSemantics(string path)
    {
        byte[] good = ReadFixture(path);
        int vertex = LumpOffset(good, 10), meshvert = LumpOffset(good, 11), face = LumpOffset(good, 13);
        MutateAndReject(good, meshvert, 13, "meshvert escapes its own face slice");
        MutateAndReject(good, meshvert, -1, "negative local meshvert");
        MutateAndReject(good, face + 24, 5, "incomplete triangle");
        MutateAndReject(good, face + 12, int.MaxValue, "face vertex overflow");
        MutateAndReject(good, 8 + 10 * 8, -1, "negative lump offset");
        MutateAndReject(good, vertex + 12, unchecked((int)0x7f800000), "infinite surface UV");
        MutateAndReject(good, vertex + 28, unchecked((int)0x7fc00000), "NaN normal");
        ExpectThrows(() => BspReader.Read(null), "null buffer");

        var doc = BspReader.Read(good);
        doc.Shaders[0].SurfaceFlags = 0x80; // invisible caulk remains collidable
        var mesh = BspGeometryBuilder.BuildModel(doc, 0, new System.Collections.Generic.List<string>());
        Assert(mesh.Triangles.Count == 0 && mesh.CollisionTriangles.Count == 102, "NODRAW does not erase collision");
        doc.Shaders[0].SurfaceFlags = 0x4000; // non-solid is visible
        mesh = BspGeometryBuilder.BuildModel(doc, 0, new System.Collections.Generic.List<string>());
        Assert(mesh.Triangles.Count == 102 && mesh.CollisionTriangles.Count == 0, "NONSOLID does not erase drawing");
        doc.Shaders[0].SurfaceFlags = 0x10; // NOIMPACT is not SKIP
        mesh = BspGeometryBuilder.BuildModel(doc, 0, new System.Collections.Generic.List<string>());
        Assert(mesh.Triangles.Count == 102, "NOIMPACT is not SKIP");
        doc.Shaders[0].SurfaceFlags = 0x200;
        mesh = BspGeometryBuilder.BuildModel(doc, 0, new System.Collections.Generic.List<string>());
        Assert(mesh.Triangles.Count == 0, "SKIP excludes drawing");
        doc.Shaders[0].SurfaceFlags = 0;
        doc.Faces[1].PatchWidth = int.MaxValue;
        var warnings = new System.Collections.Generic.List<string>();
        mesh = BspGeometryBuilder.BuildModel(doc, 0, warnings);
        Assert(mesh.Triangles.Count == 6 && warnings.Count > 0, "oversized patch skipped with diagnostic");

        doc = BspReader.Read(good);
        // Direct builder callers also get an expansion budget before allocation.
        doc.Faces[0].NumVertexes = int.MaxValue;
        ExpectThrows(() => BspGeometryBuilder.BuildModel(doc, 0, new System.Collections.Generic.List<string>()),
            "generated geometry budget");

        var parsed = BspEntityParser.Parse("// comment\n{\"k\" \"escaped \\\"quote\\\"\"}", warnings);
        Assert(parsed.Count == 1 && parsed[0].Get("k") == "escaped \"quote\"", "escaped quoted entity token");
        ExpectThrows(() => BspEntityParser.Parse("{\"k\" \"" + new string('x', 65537) + "\"}", warnings),
            "entity token budget");
        Assert(BspCoordinateSpace.QuakeYawToUnity(0) == 90, "source +X yaw");
        Assert(BspCoordinateSpace.QuakeYawToUnity(90) == 0, "source +Y yaw");
    }

    private static void TestRealMapParses(string path)
    {
        var bytes = ReadFixture(path);
        // A supplied real_map.bsp fixture is asserted to be a well-formed,
        // supported IBSP v46 map: if BspReader throws here the test must
        // fail loudly, not be swallowed as "expected". Unsupported/malformed
        // input has its own dedicated expected-failure tests above.
        var doc = BspReader.Read(bytes);

        Console.WriteLine(string.Format(
            "{0} stats: entities={1} shaders={2} vertexes={3} meshverts={4} faces={5} models={6} parseWarnings={7}",
            Path.GetFileName(path), doc.Entities.Count, doc.Shaders.Length, doc.Vertexes.Length, doc.MeshVerts.Length,
            doc.Faces.Length, doc.Models.Length, doc.Warnings.Count));
        foreach (var w in doc.Warnings.Take(20))
        {
            Console.WriteLine("  warning: " + w);
        }

        var warnings = new System.Collections.Generic.List<string>(doc.Warnings);
        var mesh = BspGeometryBuilder.BuildModel(doc, 0, warnings);
        Console.WriteLine(string.Format(
            "{0} model0 mesh: positions={1} triangles={2} collisionTriangles={3} diagnostics={4}",
            Path.GetFileName(path), mesh.Positions.Count, mesh.Triangles.Count / 3, mesh.CollisionTriangles.Count / 3,
            warnings.Count));
        Assert(mesh.Positions.Count > 0, "real map worldspawn should produce some geometry");
        Assert(mesh.CollisionTriangles.Count > 0, "real sample worldspawn should produce collision triangles");
    }
}
