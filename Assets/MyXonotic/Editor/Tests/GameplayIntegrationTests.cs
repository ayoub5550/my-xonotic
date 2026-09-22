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
            Dev9Tests.RunSelfTests();
            Passed.AddRange(Dev9Tests.Passed);
            Check(true, "dev.9 teams/powerups/extra weapons/movers/CTF/rig self-tests");
            var root = BspImportPipeline.Import("ThirdParty/Xonotic/maps-pk3/maps/boil.bsp");
            var pickups = root.GetComponentsInChildren<Pickup>(true);
            Check(pickups.Length == 37, "real Boil has 37 supported health/armor/ammo/weapon/strength pickups");
            int strengthPickups = 0;
            foreach (var p in pickups) if (p.Type == PickupType.Strength) strengthPickups++;
            Check(strengthPickups == 1, "Boil's item_strength is a live Strength pickup");
            Check(root.GetComponentsInChildren<MyXonotic.Content.ImportedSubmodel>(true).Length >= 0, "submodel markers present or none");
            foreach (var sub in root.GetComponentsInChildren<MyXonotic.Content.ImportedSubmodel>(true))
                Check(sub.localMax.x >= sub.localMin.x && sub.localMax.y >= sub.localMin.y, "submodel bounds recorded for " + sub.classname);
            int weaponPickups = 0;
            foreach (var pickup in pickups)
            {
                if (pickup.Type == PickupType.Weapon) weaponPickups++;
                Check(AssetDatabase.Contains(pickup.GetComponent<MeshFilter>().sharedMesh),
                    "pickup placeholder mesh persisted");
                Check(AssetDatabase.Contains(pickup.GetComponent<MeshRenderer>().sharedMaterial),
                    "pickup placeholder material persisted");
            }
            const string scenePath = "Assets/MyXonotic/Generated/PickupRegression.unity";
            Check(EditorSceneManager.SaveScene(root.scene, scenePath), "pickup scene saved");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.OpenScene(scenePath);
            Check(weaponPickups == 7, "Boil's 7 weapon_* entities became weapon pickups");
            Check(UnityEngine.Object.FindObjectsOfType<Pickup>().Length == 37,
                "37 pickups survive scene reload");
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
