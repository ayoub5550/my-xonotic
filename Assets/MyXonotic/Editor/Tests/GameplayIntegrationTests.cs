using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content.Bsp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    public static class GameplayIntegrationTests
    {
        static readonly List<string> Passed = new List<string>();
        public static void Run()
        {
            Passed.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BspPickupImporter.RunSelfTests();
            Check(true, "pickup mapping and coordinate self-tests");
            PickupTests.RunSelfTests();
            Check(true, "pickup actor/grant/rejection/respawn self-tests");
            MatchSessionRegressionTests.RunSelfTests();
            Check(true, "real Actor scoring and MatchSession self-tests");
            var root = BspImportPipeline.Import("ThirdParty/Xonotic/maps-pk3/maps/boil.bsp");
            var pickups = root.GetComponentsInChildren<Pickup>(true);
            Check(pickups.Length == 25, "real Boil has 25 supported health/armor/rocket pickups");
            foreach (var pickup in pickups)
            {
                Check(pickup.Type != PickupType.AmmoRifle, "no bullet-to-practice-rifle substitution");
                Check(AssetDatabase.Contains(pickup.GetComponent<MeshFilter>().sharedMesh),
                    "pickup placeholder mesh persisted");
                Check(AssetDatabase.Contains(pickup.GetComponent<MeshRenderer>().sharedMaterial),
                    "pickup placeholder material persisted");
            }
            const string scenePath = "Assets/MyXonotic/Generated/PickupRegression.unity";
            Check(EditorSceneManager.SaveScene(root.scene, scenePath), "pickup scene saved");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.OpenScene(scenePath);
            Check(UnityEngine.Object.FindObjectsOfType<Pickup>().Length == 25,
                "25 pickups survive scene reload");
            foreach (var pickup in UnityEngine.Object.FindObjectsOfType<Pickup>())
            {
                Check(pickup.GetComponent<MeshFilter>().sharedMesh != null, "serialized pickup mesh retained");
                Check(pickup.GetComponent<Collider>().isTrigger, "serialized pickup collider remains trigger");
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/gameplay-integration-tests.txt", string.Join("\n", Passed));
            Debug.Log("GAMEPLAY INTEGRATION PASS " + Passed.Count);
        }
        static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception(label);
            Passed.Add("PASS " + label);
        }
    }
}
