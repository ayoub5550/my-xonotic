using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyXonotic.Content;
using MyXonotic.Content.Bsp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Dedicated Editor regression suite for the sky-surface fix in
    /// <see cref="BspImportPipeline"/> and <see cref="XonoticContentResolver"/>:
    /// a real Xonotic sky shader (scripts/skies_polluted_earth.shader,
    /// "textures/skies/polluted_earth") must resolve the ORIGINAL six
    /// rt/lf/ft/bk/up/dn skybox images via "MyXonotic/Sky6Sided", never the
    /// shader script's qer_editorimage credit/editor-preview texture
    /// (textures/skies/polluted_earth.jpg) stretched across the surface as
    /// an ordinary diffuse map — that regression is what a user-provided
    /// video actually showed (a large, repeated Quinn Herrick "polluted
    /// earth" preview texture tiling every sky-tagged surface).
    ///
    /// Entirely separate menu entry/file from the shared
    /// <see cref="LocalTests"/> driver (not modified here): run this suite
    /// on its own with
    ///   Unity -batchmode -nographics -projectPath . -quit \
    ///     -executeMethod MyXonotic.EditorTools.SkyImportRegressionTests.Run
    /// This file/class was never executed against a real Unity Editor in
    /// the environment that wrote it (no local Unity install was available
    /// there); see the accompanying report for exactly what remains to be
    /// run. Do not treat this comment as evidence the suite has passed.
    /// </summary>
    public static class SkyImportRegressionTests
    {
        private const string RealSkyShaderName = "textures/skies/polluted_earth";
        private const string RealSkyEnv = "env/polluted_earth/polluted_earth";
        private const string RealSkyEditorImageNeedle = "polluted_earth.jpg";
        private const string PlainShaderName = "myx/plain_floor_unresolvable";
        private const string MissingEnvShaderName = "textures/skies/test_missing_env";
        private const string NoEnvShaderName = "textures/skies/test_no_env";

        private static readonly List<string> Passed = new List<string>();

        [MenuItem("My Xonotic/Tests - Sky import regression (dedicated)")]
        public static void Run()
        {
            Passed.Clear();
            string tempRoot = null;
            string previousContentRoots = Environment.GetEnvironmentVariable("XONOTIC_CONTENT_ROOTS");
            try
            {
                tempRoot = CreateTempFallbackContentRoot();
                Environment.SetEnvironmentVariable("XONOTIC_CONTENT_ROOTS", tempRoot);

                RunResolverChecks();
                RunPipelineChecks();

                Directory.CreateDirectory("Artifacts");
                File.WriteAllText("Artifacts/sky-import-regression-tests.txt", string.Join("\n", Passed));
                Debug.Log("[my-xonotic] SKY IMPORT REGRESSION TESTS PASS " + Passed.Count);
            }
            finally
            {
                Environment.SetEnvironmentVariable("XONOTIC_CONTENT_ROOTS", previousContentRoots);
                if (tempRoot != null)
                {
                    try { Directory.Delete(tempRoot, recursive: true); } catch (Exception) { /* best effort */ }
                }
            }
        }

        // ------------------------------------------------------------------
        // Resolver-level checks: the real repo script/env content, no BSP.
        // ------------------------------------------------------------------

        private static void RunResolverChecks()
        {
            var resolver = new XonoticContentResolver();
            Check(resolver.Roots.Count > 0,
                "resolver finds at least one content root (ThirdParty/Xonotic/maps-pk3 by default)");

            var script = resolver.GetScript(RealSkyShaderName);
            Check(script != null, "real 'skies_polluted_earth.shader' script is parsed and resolvable by name");
            Check(script != null && script.Has("sky"), "real sky shader carries surfaceparm sky");
            Check(script != null && script.SkyEnv == RealSkyEnv,
                "real sky shader's skyparms env base is parsed as '" + RealSkyEnv + "'");
            Check(!string.IsNullOrEmpty(script.EditorImage) && script.EditorImage.Contains(RealSkyEditorImageNeedle),
                "sanity: the script's qer_editorimage IS the credit/preview jpg (confirms what the bug used to render)");

            var sides = resolver.FindSkybox(RealSkyEnv);
            Check(sides != null, "resolver.FindSkybox resolves all six side images for the real polluted_earth env");
            if (sides != null)
            {
                Check(sides.Count == 6, "exactly six sky side keys resolved");
                foreach (var side in new[] { "rt", "lf", "ft", "bk", "up", "dn" })
                {
                    Check(sides.ContainsKey(side) && File.Exists(sides[side]),
                        "side '" + side + "' resolves to an existing file on disk");
                    Check(!sides[side].Replace('\\', '/').EndsWith(RealSkyEditorImageNeedle, StringComparison.OrdinalIgnoreCase),
                        "side '" + side + "' image is NOT the qer_editorimage credit/preview texture");
                }
                var distinctPaths = sides.Values.Select(p => Path.GetFullPath(p)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                Check(distinctPaths == 6, "all six resolved side images are distinct files (no single image repeated across faces)");
            }
        }

        // ------------------------------------------------------------------
        // Pipeline-level checks: a small self-contained synthetic BSP (see
        // BuildSkyFixtureBsp below) exercising BspImportPipeline end to end.
        // ------------------------------------------------------------------

        private static void RunPipelineChecks()
        {
            string bspPath = Path.Combine(Path.GetTempPath(), "myx-sky-fixture-" + Guid.NewGuid().ToString("N") + ".bsp");
            File.WriteAllBytes(bspPath, BuildSkyFixtureBsp());
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = BspImportPipeline.Import(bspPath);
                var renderer = root.GetComponentInChildren<MeshRenderer>();
                Check(renderer != null, "fixture import produced a MeshRenderer");
                Check(renderer.sharedMaterials.Length == 4, "fixture has one material per one of the four distinct fixture shaders");

                var skyMat = renderer.sharedMaterials[0];
                Check(skyMat != null && skyMat.shader != null && skyMat.shader.name == "MyXonotic/Sky6Sided",
                    "the real sky shader group resolves to the MyXonotic/Sky6Sided material, not Lightmapped/VertexColor");

                var sideProps = new[] { "_SkyRt", "_SkyLf", "_SkyFt", "_SkyBk", "_SkyUp", "_SkyDn" };
                var sideTexPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in sideProps)
                {
                    var tex = skyMat.GetTexture(prop) as Texture2D;
                    Check(tex != null, "sky material has a non-null texture assigned for " + prop);
                    if (tex != null)
                    {
                        string texPath = AssetDatabase.GetAssetPath(tex);
                        Check(!string.IsNullOrEmpty(texPath), prop + " texture is a persisted project asset");
                        Check(!texPath.ToLowerInvariant().Contains("editor") &&
                              !AssetDatabase.LoadAssetAtPath<Texture2D>(texPath).name.ToLowerInvariant().Contains("editorimage"),
                              prop + " texture asset is not the editor-preview image");
                        sideTexPaths.Add(texPath);
                    }
                }
                Check(sideTexPaths.Count == 6,
                    "all six sky side textures are distinct generated assets (the original bug repeated one image on every face)");

                var plainMat = renderer.sharedMaterials[1];
                Check(plainMat != null && plainMat.shader != null && plainMat.shader.name != "MyXonotic/Sky6Sided",
                    "a non-sky shader group never gets routed through the sky material");

                var missingEnvMat = renderer.sharedMaterials[2];
                Check(missingEnvMat != null && missingEnvMat.shader != null && missingEnvMat.shader.name != "MyXonotic/Sky6Sided",
                    "a sky shader whose env has no resolvable six-side images falls back explicitly, not to Sky6Sided with missing textures");

                var noEnvMat = renderer.sharedMaterials[3];
                Check(noEnvMat != null && noEnvMat.shader != null && noEnvMat.shader.name != "MyXonotic/Sky6Sided",
                    "surfaceparm sky with 'skyparms - -' (no env) falls back explicitly rather than crashing or defaulting to a broken sky material");

                var arena = root.GetComponent<ImportedArena>();
                Check(arena != null, "fixture import produced an ImportedArena marker");
                bool warnedMissingEnv = arena.warnings.Any(w => w.Contains(MissingEnvShaderName) &&
                    (w.Contains("missing") || w.Contains("fallback")));
                Check(warnedMissingEnv, "an explicit warning names the missing-six-side-image sky shader and its fallback");
                bool warnedNoEnv = arena.warnings.Any(w => w.Contains(NoEnvShaderName) || w.Contains("no resolvable skyparms env base"));
                Check(warnedNoEnv, "an explicit warning covers the no-env-base ('skyparms - -') sky shader case");

                string manifestPath = "Assets/MyXonotic/Generated/Imported/" + root.name + "/import-manifest.json";
                Check(File.Exists(manifestPath), "import writes a provenance manifest for the sky fixture");
                string manifestJson = File.ReadAllText(manifestPath);
                foreach (var side in new[] { "sky-rt", "sky-lf", "sky-ft", "sky-bk", "sky-up", "sky-dn" })
                {
                    Check(manifestJson.Contains("\"" + side + "\""),
                        "manifest records a distinct '" + side + "' provenance role for the sky env");
                }
                Check(!manifestJson.Contains(RealSkyEditorImageNeedle),
                    "manifest never lists the credit/editor-preview jpg as a resolved diffuse/sky source for this shader");

                // Reimport stability: same fixture imported again must reuse
                // the same generated sky material/texture asset GUIDs.
                var previousSkyMatGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skyMat));
                var previousSkyTexGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skyMat.GetTexture("_SkyRt")));
                UnityEngine.Object.DestroyImmediate(root);
                var root2 = BspImportPipeline.Import(bspPath);
                var renderer2 = root2.GetComponentInChildren<MeshRenderer>();
                var skyMat2 = renderer2.sharedMaterials[0];
                Check(skyMat2 != null && skyMat2.shader.name == "MyXonotic/Sky6Sided", "repeat import still resolves the sky material");
                Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skyMat2)) == previousSkyMatGuid,
                    "repeat import preserves the generated sky material's GUID");
                Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(skyMat2.GetTexture("_SkyRt"))) == previousSkyTexGuid,
                    "repeat import preserves the generated sky side-texture's GUID");
            }
            finally
            {
                try { File.Delete(bspPath); } catch (Exception) { /* best effort */ }
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("[my-xonotic] SKY TEST FAIL: " + name);
            Passed.Add("PASS " + name);
        }

        // ------------------------------------------------------------------
        // A throwaway XONOTIC_CONTENT_ROOTS root providing two synthetic,
        // wholly-original material scripts this suite writes itself (never
        // extracted from any Xonotic asset) so the missing-six-side-image
        // and no-env-base sky fallback paths are exercised without needing
        // matching real upstream data. ThirdParty/Xonotic/maps-pk3 (the real
        // polluted_earth content) is still reachable because
        // XonoticContentResolver always appends its default candidates
        // after whatever XONOTIC_CONTENT_ROOTS names, never replacing them.
        // ------------------------------------------------------------------
        private static string CreateTempFallbackContentRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "myx-sky-content-" + Guid.NewGuid().ToString("N"));
            string scriptsDir = Path.Combine(root, "scripts");
            Directory.CreateDirectory(scriptsDir);
            string shaderText =
                MissingEnvShaderName + "\n" +
                "{\n" +
                "\tqer_editorimage textures/skies/test_missing_env.jpg\n" +
                "\tsurfaceparm sky\n" +
                "\tskyparms env/test_missing_env_nonexistent/test_missing_env_nonexistent - -\n" +
                "}\n" +
                NoEnvShaderName + "\n" +
                "{\n" +
                "\tsurfaceparm sky\n" +
                "\tskyparms - -\n" +
                "}\n";
            File.WriteAllText(Path.Combine(scriptsDir, "test_sky_fallback.shader"), shaderText);
            return root;
        }

        // ------------------------------------------------------------------
        // Minimal, self-contained, wholly-original synthetic IBSP v46
        // builder (four quad faces, one per fixture shader; no collision
        // requirements). Deliberately NOT touching tools/content/fixture_bsp.py
        // (that generator is shared/out of scope for this change) — this is
        // a standalone byte-for-byte equivalent writer scoped to this one
        // test file, matching the same record layouts BspReader.cs
        // documents/expects (see BspRecords.cs for the lump order).
        // ------------------------------------------------------------------
        private static byte[] BuildSkyFixtureBsp()
        {
            const int lumpCount = 17; // BspLump.Count
            const int entitiesLump = 0, shadersLump = 1, modelsLump = 7, vertexesLump = 10, meshVertsLump = 11, facesLump = 13;

            string entitiesText =
                "{\n\"classname\" \"worldspawn\"\n}\n" +
                "{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"0 0 0\"\n\"angle\" \"0\"\n}\n";
            byte[] entities = System.Text.Encoding.ASCII.GetBytes(entitiesText + "\0");

            string[] shaderNames = { RealSkyShaderName, PlainShaderName, MissingEnvShaderName, NoEnvShaderName };
            byte[] shaders = new byte[shaderNames.Length * 72];
            for (int i = 0; i < shaderNames.Length; i++)
            {
                int o = i * 72;
                var nameBytes = System.Text.Encoding.ASCII.GetBytes(shaderNames[i]);
                Array.Copy(nameBytes, 0, shaders, o, Math.Min(nameBytes.Length, 63));
                // surfaceFlags (o+64) = 0, contentFlags (o+68) = 0: not
                // NODRAW/SKIP, not solid; sky routing here is entirely
                // script-driven (surfaceparm sky), not a BSP surface-flag bit.
            }

            var vertexesStream = new MemoryStream();
            var meshVertsList = new List<int>();
            var faceRecords = new List<byte[]>();

            for (int shaderIndex = 0; shaderIndex < shaderNames.Length; shaderIndex++)
            {
                float x0 = shaderIndex * 128f;
                int firstVertex = (int)(vertexesStream.Length / 44);
                int firstMeshVert = meshVertsList.Count;
                WriteQuadVertexes(vertexesStream, x0);
                // Same winding as tools/content/fixture_bsp.py's quad
                // (0,2,1)/(0,3,2): matches this project's measured
                // real-map meshvert-order convention (see
                // BspCoordinateSpace.cs); irrelevant to sky-material
                // resolution but kept consistent/non-misleading.
                meshVertsList.AddRange(new[] { 0, 2, 1, 0, 3, 2 });
                faceRecords.Add(PackFace(
                    texture: shaderIndex, effect: -1, type: 1,
                    vertex: firstVertex, numVertexes: 4,
                    meshVert: firstMeshVert, numMeshVerts: 6,
                    lmIndex: -1));
            }

            byte[] vertexes = vertexesStream.ToArray();
            byte[] meshVerts = new byte[meshVertsList.Count * 4];
            for (int i = 0; i < meshVertsList.Count; i++)
                BitConverter.GetBytes(meshVertsList[i]).CopyTo(meshVerts, i * 4);
            byte[] faces = faceRecords.SelectMany(f => f).ToArray();

            byte[] model0 = PackModel(mins: new Vector3(-8, -8, -8), maxs: new Vector3(512, 128, 8), face: 0, numFaces: shaderNames.Length);

            var lumps = new byte[lumpCount][];
            for (int i = 0; i < lumpCount; i++) lumps[i] = Array.Empty<byte>();
            lumps[entitiesLump] = entities;
            lumps[shadersLump] = shaders;
            lumps[modelsLump] = model0;
            lumps[vertexesLump] = vertexes;
            lumps[meshVertsLump] = meshVerts;
            lumps[facesLump] = faces;

            int headerSize = 8 + lumpCount * 8;
            var body = new MemoryStream();
            var dirEntries = new (int offset, int length)[lumpCount];
            int offset = headerSize;
            for (int i = 0; i < lumpCount; i++)
            {
                dirEntries[i] = (offset, lumps[i].Length);
                body.Write(lumps[i], 0, lumps[i].Length);
                offset += lumps[i].Length;
            }

            var output = new MemoryStream();
            output.Write(System.Text.Encoding.ASCII.GetBytes("IBSP"), 0, 4);
            output.Write(BitConverter.GetBytes(46), 0, 4);
            foreach (var entry in dirEntries)
            {
                output.Write(BitConverter.GetBytes(entry.offset), 0, 4);
                output.Write(BitConverter.GetBytes(entry.length), 0, 4);
            }
            body.Position = 0;
            body.CopyTo(output);
            return output.ToArray();
        }

        private static void WriteQuadVertexes(MemoryStream stream, float x0)
        {
            WriteVertex(stream, x0 + 0f, 0f, 0f);
            WriteVertex(stream, x0 + 64f, 0f, 0f);
            WriteVertex(stream, x0 + 64f, 64f, 0f);
            WriteVertex(stream, x0 + 0f, 64f, 0f);
        }

        private static void WriteVertex(MemoryStream stream, float x, float y, float z)
        {
            void F(float v) => stream.Write(BitConverter.GetBytes(v), 0, 4);
            F(x); F(y); F(z);      // position
            F(0f); F(0f);          // surface uv
            F(0f); F(0f);          // lightmap uv
            F(0f); F(0f); F(1f);   // normal
            stream.WriteByte(255); stream.WriteByte(255); stream.WriteByte(255); stream.WriteByte(255); // color
        }

        private static byte[] PackFace(int texture, int effect, int type, int vertex, int numVertexes, int meshVert, int numMeshVerts, int lmIndex)
        {
            var buf = new byte[26 * 4];
            int[] ints =
            {
                texture, effect, type, vertex, numVertexes, meshVert, numMeshVerts, lmIndex,
                0, 0,        // lm_start[2]
                0, 0,        // lm_size[2]
                0, 0, 0,     // lm_origin
                0, 0, 0, 0, 0, 0, // lm_vecs[2][3]
                0, 0, 1,     // normal
                0, 0,        // patch_w, patch_h (not a patch face)
            };
            for (int i = 0; i < ints.Length; i++) BitConverter.GetBytes(ints[i]).CopyTo(buf, i * 4);
            return buf;
        }

        private static byte[] PackModel(Vector3 mins, Vector3 maxs, int face, int numFaces)
        {
            var buf = new byte[40];
            float[] floats = { mins.x, mins.y, mins.z, maxs.x, maxs.y, maxs.z };
            for (int i = 0; i < floats.Length; i++) BitConverter.GetBytes(floats[i]).CopyTo(buf, i * 4);
            int[] ints = { face, numFaces, 0, 0 };
            for (int i = 0; i < ints.Length; i++) BitConverter.GetBytes(ints[i]).CopyTo(buf, 24 + i * 4);
            return buf;
        }
    }
}
