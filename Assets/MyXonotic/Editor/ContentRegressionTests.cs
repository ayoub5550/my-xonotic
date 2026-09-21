using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Read-back checks over persisted full-game scenes, not transient import
    /// objects. Run after prepare-maps or an all-maps build. No rendering claim.
    /// </summary>
    public static class ContentRegressionTests
    {
        static readonly List<string> Passed = new List<string>();

        public static void Run()
        {
            Passed.Clear();
            Check(!BspImportPipeline.StageCarvesAlpha(new MaterialScript.StageInfo { BlendFunc = "FILTER" }),
                "filter blend does not carve alpha");
            Check(!BspImportPipeline.StageCarvesAlpha(new MaterialScript.StageInfo { BlendFunc = "ADD" }),
                "additive glow does not carve alpha");
            Check(BspImportPipeline.StageCarvesAlpha(new MaterialScript.StageInfo { AlphaFunc = "GE128" }),
                "explicit alpha test carves alpha");
            Check(BspImportPipeline.StageCarvesAlpha(new MaterialScript.StageInfo { BlendFunc = "GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA" }),
                "source alpha blend uses coverage approximation");

            var npot = new Texture2D(600, 600, TextureFormat.RGBA32, false);
            var final = ImportedTexturePolicy.Finalize(npot, false);
            Check(final.format == TextureFormat.RGBA32 && final.mipmapCount > 1,
                "NPOT texture retains valid raw mip chain without ETC compressor failure");
            UnityEngine.Object.DestroyImmediate(final);

            foreach (var name in CharacterModels.Names)
            {
                var actor = new GameObject("CharacterTest");
                GameObject body;
                Check(CharacterModels.TryAttach(actor.transform, name, out body), name + " runtime resource attachment");
                var mesh = body.GetComponent<MeshFilter>().sharedMesh;
                var materials = body.GetComponent<MeshRenderer>().sharedMaterials;
                Check(mesh.vertexCount > 0 && mesh.bounds.size.y > 0.5f && mesh.bounds.size.y < 5f &&
                      mesh.vertices.All(v => Finite(v.x) && Finite(v.y) && Finite(v.z)),
                    name + " finite nonempty character geometry");
                Check(materials.Length == mesh.subMeshCount &&
                      materials.All(m => m != null && m.mainTexture != null), name + " all skins persisted");
                UnityEngine.Object.DestroyImmediate(actor);
            }

            var scenes = Directory.GetFiles(FullGameBuild.MapsSceneFolder, "map_*.unity");
            Check(scenes.Length == 29, "all 29 official map scenes available");
            int totalPickups = 0, totalModels = 0, totalSubmodels = 0, totalDecorations = 0;
            int originalPickups = 0, placeholders = 0, untexturedModelMaterials = 0;
            foreach (var path in scenes.OrderBy(s => s, StringComparer.Ordinal))
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var arena = UnityEngine.Object.FindObjectOfType<ImportedArena>();
                Check(arena != null, Path.GetFileName(path) + " imported arena deserialized");
                var pickups = arena.GetComponentsInChildren<Pickup>(true);
                bool validPickupArt = true;
                foreach (var pickup in pickups)
                {
                    var mesh = pickup.GetComponent<MeshFilter>().sharedMesh;
                    var materials = pickup.GetComponent<MeshRenderer>().sharedMaterials;
                    string asset = AssetDatabase.GetAssetPath(mesh);
                    bool original = asset.StartsWith("Assets/MyXonotic/Generated/MapModels/", StringComparison.Ordinal);
                    if (original) originalPickups++; else placeholders++;
                    validPickupArt &= original && mesh.subMeshCount == materials.Length &&
                        materials.All(m => m != null &&
                            AssetDatabase.GetAssetPath(m).StartsWith("Assets/MyXonotic/Generated/MapModels/", StringComparison.Ordinal));
                }
                Check(validPickupArt, Path.GetFileName(path) + " pickup meshes/materials remain original after serialization");
                var decorations = arena.GetComponentsInChildren<PickupDecoration>(true);
                Check(decorations.All(d => d.GetComponent<Pickup>() == null &&
                      d.GetComponent<MeshFilter>()?.sharedMesh != null),
                    Path.GetFileName(path) + " unsupported items are visual-only with persisted meshes");
                var bsp = BspReader.Read(File.ReadAllBytes(Path.Combine(
                    Environment.GetEnvironmentVariable("XONOTIC_MAPS_ROOT") ?? "ExternalContent/maps",
                    "maps", arena.sourceName)));
                bool originsMatch = true;
                foreach (var sub in arena.GetComponentsInChildren<ImportedSubmodel>(true))
                {
                    var entity = bsp.Entities.First(e => e.Get("model") == "*" + sub.modelIndex);
                    var xyz = entity.Get("origin", "0 0 0").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var q = new BspVec3(float.Parse(xyz[0], CultureInfo.InvariantCulture),
                        float.Parse(xyz[1], CultureInfo.InvariantCulture), float.Parse(xyz[2], CultureInfo.InvariantCulture));
                    var u = BspCoordinateSpace.QuakeToUnity(q);
                    originsMatch &= Vector3.Distance(sub.transform.localPosition, new Vector3(u.X, u.Y, u.Z)) < 0.0001f;
                }
                Check(originsMatch, Path.GetFileName(path) + " inline model pivots match BSP entity origins");
                totalPickups += pickups.Length;
                totalDecorations += decorations.Length;
                totalModels += arena.mapModelCount;
                totalSubmodels += arena.GetComponentsInChildren<ImportedSubmodel>(true).Length;
                foreach (var r in arena.GetComponentsInChildren<MeshRenderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && AssetDatabase.GetAssetPath(m).StartsWith("Assets/MyXonotic/Generated/MapModels/") &&
                            m.mainTexture == null) untexturedModelMaterials++;
            }
            // dev.6 shipped 1562 pickups + 577 visual-only items = 2139 item/weapon
            // entities with original art; dev.8 promotes ammo and the nine core
            // weapons to real pickups, so the split moves but the total must not.
            Check(totalPickups + totalDecorations == 2139 && originalPickups == totalPickups && placeholders == 0,
                "2139 persisted original item/weapon meshes (" + totalPickups + " pickups + " + totalDecorations + " visual-only); zero placeholder spheres");
            Check(totalPickups > 1562, "ammo and weapon entities are now live pickups (" + totalPickups + " > 1562)");
            Check(totalModels == 304 && totalSubmodels == 117,
                "304 props / 117 static submodels persisted");
            Passed.Add("INFO pickups=" + totalPickups + " decorations=" + totalDecorations);
            Passed.Add("INFO untextured map/item material references: " + untexturedModelMaterials +
                       " (reported, not a texture-parity pass)");
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/content-regression-tests.txt", string.Join("\n", Passed));
            Debug.Log("[my-xonotic] CONTENT TESTS PASS " + Passed.Count(p => p.StartsWith("PASS")));
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("[my-xonotic] CONTENT TEST FAIL: " + name);
            Passed.Add("PASS " + name);
        }
    }
}
