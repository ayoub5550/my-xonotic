using System;
using System.Collections.Generic;
using System.IO;
using MyXonotic.Content;
using MyXonotic.Content.Bsp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using MyXonotic.Menu;

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
            MainMenuGeometry();
            WeaponRigs();
            LocalBuild.CreateDevelopmentScene();
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/editor-tests.txt", string.Join("\n", Passed));
            Debug.Log("[my-xonotic] EDITOR TESTS PASS " + Passed.Count);
        }

        /// dev.10 regression: the dev.5-dev.9 menu had header/quit/preview
        /// rects with offsetMin.y > offsetMax.y (negative height -> Text culled)
        /// and a stencil Mask on an alpha-0.001 Image (all map cards invisible).
        /// Builds the real MainMenu in an empty scene and checks the laid-out
        /// geometry headlessly.
        static void MainMenuGeometry()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("MainMenu", typeof(MainMenu));
            var start = typeof(MainMenu).GetMethod("Start",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            start.Invoke(go.GetComponent<MainMenu>(), null);
            Canvas.ForceUpdateCanvases();
            foreach (var rt in go.GetComponentsInChildren<RectTransform>(true))
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            Canvas.ForceUpdateCanvases();

            var texts = go.GetComponentsInChildren<Text>(true);
            Check(texts.Length > 0, "menu builds text elements");
            foreach (var t in texts)
            {
                var r = t.rectTransform.rect;
                Check(r.width > 0f && r.height > 0f,
                    "menu text '" + t.name + "' has a positive rect (" + r.width.ToString("0") + "x" + r.height.ToString("0") + ")");
            }
            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
            {
                var r = g.rectTransform.rect;
                Check(r.width > 0f && r.height > 0f, "menu graphic '" + g.name + "' has a positive rect");
            }
            Text Find(string n) { foreach (var t in texts) if (t.name == n) return t; return null; }
            Check(Find("Title") != null && Find("Title").text == "MY XONOTIC", "menu title text present");
            Check(Find("Subtitle") != null && Find("Subtitle").text.Contains("maps"), "menu subtitle text present");
            var minus = go.transform.Find("MenuCanvas/SafeArea/BotsMinus/Label");
            var plus = go.transform.Find("MenuCanvas/SafeArea/BotsPlus/Label");
            Check(minus != null && minus.GetComponent<Text>().text == "-", "bots minus label set");
            Check(plus != null && plus.GetComponent<Text>().text == "+", "bots plus label set");
            var quit = go.transform.Find("MenuCanvas/SafeArea/Quit");
            Check(quit != null && quit.GetComponent<RectTransform>().rect.height > 0f, "quit button visible");
            Check(go.GetComponentsInChildren<Mask>(true).Length == 0, "menu uses no stencil Mask");
            var scroll = go.transform.Find("MenuCanvas/SafeArea/MapScroll");
            Check(scroll != null && scroll.GetComponent<RectMask2D>() != null, "map scroll clips with RectMask2D");
            var catalog = MapCatalog.Load();
            int cards = go.GetComponentsInChildren<Button>(true).Length;
            int expected = catalog != null ? catalog.maps.Count : 0;
            Check(cards >= expected, "one card button per catalog map (" + expected + " maps, " + cards + " buttons)");
            var content = scroll != null ? scroll.Find("Content") as RectTransform : null;
            if (expected > 0)
                Check(content != null && content.rect.height > 0f, "map grid content has positive height after layout");
            UnityEngine.Object.DestroyImmediate(go);
        }

        /// dev.11: every weapon ships an animated first-person rig generated
        /// from Xonotic's h_ model (IQM skeleton-only or DarkPlaces DPM with
        /// its own skinned mesh). Checks are geometric because the sandbox
        /// cannot render: clip set, joint presence, and a plausible muzzle
        /// position in the view's frame after the -90 deg yaw WeaponView applies.
        static void WeaponRigs()
        {
            int rigged = 0;
            var yaw = Quaternion.Euler(0f, -90f, 0f);
            for (int i = 0; i < WeaponController.WeaponCount; i++)
            {
                var type = (WeaponType)i;
                var info = Resources.Load<WeaponRigInfo>("Weapons/" + type + "WeaponRig");
                if (info == null) { Debug.LogWarning("[my-xonotic] no rig for " + type + " (static fallback)"); continue; }
                rigged++;
                Check(info.Rig != null && info.Rig.JointCount > 0, type + " rig has joints");
                Check(info.Rig.FindClip("fire") >= 0 && info.Rig.FindClip("idle") >= 0, type + " rig has fire+idle clips");
                Check(info.Rig.Clips[info.Rig.FindClip("idle")].Loop, type + " idle loops");
                Check(!info.Rig.Clips[info.Rig.FindClip("fire")].Loop, type + " fire is one-shot");
                Check(info.Rig.Poses != null && info.Rig.Poses.Length == info.Rig.FrameCount * info.Rig.JointCount * 10,
                    type + " pose table complete (" + info.Rig.FrameCount + " frames)");
                if (info.SourceFormat == "IQM")
                    Check(info.WeaponJoint >= 0, type + " IQM rig exposes the 'weapon' joint");
                else
                {
                    Check(info.SkinnedMesh != null && info.SkinnedMesh.vertexCount > 0, type + " DPM rig has a skinned mesh");
                    Check(info.SkinnedMesh.boneWeights.Length == info.SkinnedMesh.vertexCount, type + " DPM bone weights per vertex");
                    Check(info.SkinnedMesh.bindposes.Length == info.Rig.JointCount, type + " DPM bindposes per joint");
                    Check(info.Materials != null && info.Materials.Length == info.SkinnedMesh.subMeshCount, type + " DPM material per submesh");
                }
                Check(info.ShotJoint >= 0, type + " rig exposes a muzzle joint");
                Vector3 shot = yaw * WorldPosition(info.Rig, info.ShotJoint);
                Check(shot.z > -0.3f && shot.z < 2.2f && shot.x > -0.3f && shot.x < 0.9f && shot.y > -0.9f && shot.y < 0.4f,
                    type + " muzzle joint in a plausible view-space box (" + shot.ToString("0.00") + ")");
            }
            Check(rigged == WeaponController.WeaponCount, "all " + WeaponController.WeaponCount + " weapons have an animated rig (" + rigged + ")");
        }

        static Vector3 WorldPosition(CharacterRig rig, int joint)
        {
            Matrix4x4 m = Matrix4x4.identity;
            for (int j = joint; j >= 0; j = rig.JointParents[j])
            {
                rig.GetBind(j, out var t, out var r, out var s);
                m = Matrix4x4.TRS(t, r, s) * m;
            }
            return m.MultiplyPoint3x4(Vector3.zero);
        }

        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("[my-xonotic] TEST FAIL: " + name);
            Passed.Add("PASS " + name);
        }
    }
}
