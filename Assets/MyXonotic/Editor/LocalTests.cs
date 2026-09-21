using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content;
using MyXonotic.Content.Bsp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>Dependency-free Editor checks; throws on any failed assertion.</summary>
    public static class LocalTests
    {
        static readonly List<string> Passed = new List<string>();

        [MenuItem("My Xonotic/Tests - Editor checks")]
        public static void Run()
        {
            Passed.Clear();
            BspGameplayImporter.RunSelfTests();
            Check(true, "trigger import bounds and launch math self-tests");
            Check(ArenaMath.ApplyGroundFriction(new Vector3(10, 7, 0), 6, 3, 0.1f).y == 7,
                "friction preserves vertical speed");
            int armor = 100;
            Check(ArenaMath.ApplyArmor(50, ref armor, 0.6f) == 20 && armor == 70,
                "armor conservation");
            Check(ArenaMath.SplashDamage(5, 5, 90) == 0, "splash radius cutoff");
            LocalBuild.CreateDevelopmentScene();
            var bootstrap = UnityEngine.Object.FindObjectOfType<ArenaBootstrap>();
            Check(bootstrap != null, "serialized bootstrap");
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(bootstrap.gameObject) == 0,
                "no missing bootstrap script");
            Check(Shader.Find("MyXonotic/VertexColor") != null, "development shader present");

            string fixture = Path.GetFullPath("tests/fixtures/generated/good.bsp");
            if (!File.Exists(fixture)) throw new Exception("Generate synthetic BSP fixtures before Editor tests.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BspImportPipeline.Import(fixture);
            Check(root.GetComponent<ImportedArena>() != null, "imported arena marker");
            Check(root.GetComponentsInChildren<BspSpawnPoint>().Length == 1, "fixture spawn marker");
            Check(root.GetComponentsInChildren<MeshCollider>().Length == 1, "fixture collision mesh");
            Check(root.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount > 4, "patch tessellation");

            var fixtureMesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            Check(fixtureMesh.uv.Length == fixtureMesh.vertexCount, "surface UV channel populated");
            Check(fixtureMesh.uv2.Length == fixtureMesh.vertexCount, "lightmap UV channel populated");
            var fixtureRenderer = root.GetComponentInChildren<MeshRenderer>();
            Check(fixtureRenderer.sharedMaterials.Length == fixtureMesh.subMeshCount, "one material per submesh");
            Check(fixtureRenderer.sharedMaterials.Length >= 1 && fixtureRenderer.sharedMaterials[0] != null,
                "fixture shader with no resolvable content root still gets a usable (fallback) material");
            Check(fixtureRenderer.sharedMaterials[0].shader != null &&
                fixtureRenderer.sharedMaterials[0].shader.name == "MyXonotic/VertexColor",
                "unresolvable fixture texture degrades to the vertex-colour fallback material, not a broken/pink one");

            string manifestPath = "Assets/MyXonotic/Generated/Imported/" + root.name + "/import-manifest.json";
            Check(File.Exists(manifestPath), "import writes a provenance manifest JSON next to the generated assets");

            var previousMesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
            var previousGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previousMesh));
            var previousMaterial = fixtureRenderer.sharedMaterials[0];
            var previousMaterialGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(previousMaterial));
            UnityEngine.Object.DestroyImmediate(root);
            root = BspImportPipeline.Import(fixture);
            Check(root.GetComponentInChildren<MeshFilter>().sharedMesh != null, "repeat import");
            Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(
                root.GetComponentInChildren<MeshFilter>().sharedMesh)) == previousGuid, "repeat import preserves GUID");
            Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(
                root.GetComponentInChildren<MeshRenderer>().sharedMaterials[0])) == previousMaterialGuid,
                "repeat import preserves the generated material's GUID (deterministic asset path)");
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>())
                Check(behaviour != null, "imported component serializable");
            LocalBuild.CreateDevelopmentScene();
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/editor-tests.txt", string.Join("\n", Passed));
            Debug.Log("[my-xonotic] EDITOR TESTS PASS " + Passed.Count);
        }

        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("[my-xonotic] TEST FAIL: " + name);
            Passed.Add("PASS " + name);
        }
    }
}
