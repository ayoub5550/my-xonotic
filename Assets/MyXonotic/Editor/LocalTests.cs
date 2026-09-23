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
            Dev14PhysicsTests.Run(Check);
            Dev15WeaponTests.Run(Check);
            Dev16BotTests.Run(Check);
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
            WeaponPlacement();
            HudGeometry();
            TouchLayoutSettings();
            DevCaptureReport();
            HudArtAssets();
            BotSpawnGeometry();
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
            HudArt.ClearCache();
            var go = new GameObject("MainMenu", typeof(MainMenu));
            var menu = go.GetComponent<MainMenu>();
            var start = typeof(MainMenu).GetMethod("Start",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            start.Invoke(menu, null);
            var catalog = MapCatalog.Load();
            int expected = catalog != null ? catalog.maps.Count : 0;

            // dev.13: three screens; each is laid out and checked while active.
            foreach (MenuScreen screen in Enum.GetValues(typeof(MenuScreen)))
            {
                menu.ShowScreen(screen);
                Canvas.ForceUpdateCanvases();
                foreach (var rt in go.GetComponentsInChildren<RectTransform>(false))
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                Canvas.ForceUpdateCanvases();
                var texts = go.GetComponentsInChildren<Text>(false);
                Check(texts.Length > 0, "menu " + screen + " builds text elements");
                foreach (var t in texts)
                {
                    var r = t.rectTransform.rect;
                    Check(r.width > 0f && r.height > 0f,
                        "menu " + screen + " text '" + t.name + "' has a positive rect (" + r.width.ToString("0") + "x" + r.height.ToString("0") + ")");
                }
                foreach (var g in go.GetComponentsInChildren<Graphic>(false))
                {
                    var r = g.rectTransform.rect;
                    Check(r.width > 0f && r.height > 0f, "menu " + screen + " graphic '" + g.name + "' has a positive rect");
                }
                foreach (var b in go.GetComponentsInChildren<Button>(false))
                {
                    var r = b.GetComponent<RectTransform>().rect;
                    if (b.name.StartsWith("Card_")) continue; // grid cells are 290x210
                    Check(r.height >= 44f, "menu " + screen + " button '" + b.name + "' is thumb-sized (" + r.height.ToString("0") + " px high)");
                }
            }
            Check(go.GetComponentsInChildren<Mask>(true).Length == 0, "menu uses no stencil Mask");

            menu.ShowScreen(MenuScreen.Home);
            Text Find(string n) { foreach (var t in go.GetComponentsInChildren<Text>(true)) if (t.name == n) return t; return null; }
            Check(Find("Title") != null && Find("Title").text == "MY XONOTIC", "menu title text present");
            Check(Find("Subtitle") != null && Find("Subtitle").text.Contains("maps"), "menu subtitle text present");
            var quit = go.transform.Find("MenuCanvas/SafeArea/Home/Quit");
            Check(quit != null && quit.GetComponent<RectTransform>().rect.height > 0f, "quit button visible");
            Check(go.transform.Find("MenuCanvas/SafeArea/Home/Play") != null, "home has PLAY");
            Check(go.transform.Find("MenuCanvas/SafeArea/Home/Settings") != null, "home has SETTINGS");

            menu.ShowScreen(MenuScreen.Play);
            var minus = go.transform.Find("MenuCanvas/SafeArea/Play/BotsMinus/Label");
            var plus = go.transform.Find("MenuCanvas/SafeArea/Play/BotsPlus/Label");
            Check(minus != null && minus.GetComponent<Text>().text == "-", "bots minus label set");
            Check(plus != null && plus.GetComponent<Text>().text == "+", "bots plus label set");
            var scroll = go.transform.Find("MenuCanvas/SafeArea/Play/MapScroll");
            Check(scroll != null && scroll.GetComponent<RectMask2D>() != null, "map scroll clips with RectMask2D");
            int cards = 0, activeCards = 0;
            foreach (var b in go.GetComponentsInChildren<Button>(true)) if (b.name.StartsWith("Card_")) { cards++; if (b.gameObject.activeSelf) activeCards++; }
            Check(cards == expected, "one card per catalog map (" + expected + " maps, " + cards + " cards)");
            int visible = MainMenu.VisibleMapCount(catalog, MatchSettings.Mode, false);
            Check(activeCards == visible, "map grid filtered to maps supporting " + MatchSettings.Mode + " (" + activeCards + " shown, " + visible + " expected)");
            if (expected > 0) Check(visible > 0, "at least one map supports the default mode");
            var content = scroll != null ? scroll.Find("Content") as RectTransform : null;
            if (expected > 0)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                Check(content != null && content.rect.height > 0f, "map grid content has positive height after layout");
            }

            menu.ShowScreen(MenuScreen.Settings);
            Check(go.transform.Find("MenuCanvas/SafeArea/Settings/TouchPanel/ButtonScaleValue") != null, "settings has button-size stepper");
            Check(go.transform.Find("MenuCanvas/SafeArea/Settings/TouchPanel/SensitivityValue") != null, "settings has sensitivity stepper");
            Check(go.transform.Find("MenuCanvas/SafeArea/Settings/TouchPanel/InvertY") != null, "settings has invert toggle");
            Check(go.transform.Find("MenuCanvas/SafeArea/Settings/TouchPanel/LeftHanded") != null, "settings has left-handed toggle");
            Check(go.transform.Find("MenuCanvas/SafeArea/Settings/DevPanel/ShareLog") != null, "settings has DevCapture SHARE LOG");
            UnityEngine.Object.DestroyImmediate(go);
        }

        /// dev.12 regression: the dev.11 device video showed the bottom HUD texts
        /// (FRAGS/DEATHS, weapon name) cut off below the screen: centre pivots
        /// with edge anchors. Builds the real HUD and checks every Text rect lies
        /// inside its canvas.
        static void HudGeometry()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("HudHost");
            var hud = Hud.Build(host.transform);
            Canvas.ForceUpdateCanvases();
            var canvasRt = hud.GetComponent<RectTransform>();
            var canvasRect = canvasRt.rect;
            var corners = new Vector3[4];
            var texts = hud.GetComponentsInChildren<Text>(true);
            Check(texts.Length >= 5, "hud builds its texts (" + texts.Length + ")");
            // dev.13 bottom panel: health/armor/ammo numbers + weapon name exist
            Check(Array.Exists(texts, t => t.name == "Health"), "hud has health number");
            Check(Array.Exists(texts, t => t.name == "Armor"), "hud has armor number");
            Check(Array.Exists(texts, t => t.name == "AmmoCount"), "hud has ammo number");
            foreach (var img in hud.GetComponentsInChildren<Image>(true))
            {
                if (img.transform.IsChildOf(hud.transform.Find("SafeArea/PausePanel"))) continue;
                img.rectTransform.GetWorldCorners(corners);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var c in corners)
                {
                    var l = canvasRt.InverseTransformPoint(c);
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x);
                    minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
                }
                Check(minX >= canvasRect.xMin - 0.5f && maxX <= canvasRect.xMax + 0.5f && minY >= canvasRect.yMin - 0.5f && maxY <= canvasRect.yMax + 0.5f,
                    "hud image '" + img.name + "' inside the screen");
                Check(img.rectTransform.rect.width > 0f && img.rectTransform.rect.height > 0f, "hud image '" + img.name + "' has a positive rect");
            }
            foreach (var t in texts)
            {
                if (t.name == "PauseText" || t.name == "PauseDevText") continue; // inside the pause panel, laid out by its parent
                t.rectTransform.GetWorldCorners(corners);
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var c in corners)
                {
                    var l = canvasRt.InverseTransformPoint(c);
                    minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x);
                    minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
                }
                bool inside = minX >= canvasRect.xMin - 0.5f && maxX <= canvasRect.xMax + 0.5f &&
                              minY >= canvasRect.yMin - 0.5f && maxY <= canvasRect.yMax + 0.5f;
                Check(inside, "hud text '" + t.name + "' inside the screen (x " + minX.ToString("0") + ".." + maxX.ToString("0") +
                    ", y " + minY.ToString("0") + ".." + maxY.ToString("0") + " of " + canvasRect.width.ToString("0") + "x" + canvasRect.height.ToString("0") + ")");
                Check(t.rectTransform.rect.width > 0f && t.rectTransform.rect.height > 0f, "hud text '" + t.name + "' has a positive rect");
            }
            UnityEngine.Object.DestroyImmediate(host);
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


        /// <summary>
        /// dev.12: device video showed several rigged weapons pointing the wrong
        /// way. Pose every rig at idle exactly as WeaponView does (root yaw −90°),
        /// skin it on the CPU and report the view-space bounds of what the player
        /// would see: the gun must sit right/below the eye and extend forward
        /// (its +Z extent longer than its +X extent).
        /// </summary>
        static void WeaponPlacement()
        {
            var host = new GameObject("WeaponPlacementTest");
            try
            {
                for (int i = 0; i < WeaponController.WeaponCount; i++)
                {
                    var type = (WeaponType)i;
                    var info = Resources.Load<WeaponRigInfo>("Weapons/" + type + "WeaponRig");
                    if (info == null || info.Rig == null || info.Rig.JointCount == 0) continue;
                    var root = new GameObject("Rig_" + type);
                    root.transform.SetParent(host.transform, false);
                    root.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                    var anim = CharacterAnimator.Create(root.transform, info.Rig, info.SkinnedMesh, info.Materials);
                    anim.Play("idle", true);
                    anim.Step(0.05f);
                    Bounds b = default; bool any = false;
                    var obj = new System.Text.StringBuilder(); int vbase = 0;
                    void Add(Vector3 p) { if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
                    void Dump(Mesh m, Matrix4x4 l2w, string group)
                    {
                        obj.Append("g ").Append(group).Append('\n');
                        var verts = m.vertices;
                        foreach (var v in verts)
                        {
                            var p = host.transform.InverseTransformPoint(l2w.MultiplyPoint3x4(v));
                            Add(p);
                            obj.Append("v ").Append(p.x.ToString("0.0000")).Append(' ').Append(p.y.ToString("0.0000")).Append(' ').Append(p.z.ToString("0.0000")).Append('\n');
                        }
                        var tris = m.triangles;
                        for (int t = 0; t + 2 < tris.Length; t += 3)
                            obj.Append("f ").Append(vbase + tris[t] + 1).Append(' ').Append(vbase + tris[t + 1] + 1).Append(' ').Append(vbase + tris[t + 2] + 1).Append('\n');
                        vbase += verts.Length;
                    }
                    if (anim.Skin != null)
                    {
                        var baked = new Mesh();
                        anim.Skin.BakeMesh(baked);
                        Dump(baked, anim.Skin.transform.localToWorldMatrix, "skin");
                        UnityEngine.Object.DestroyImmediate(baked);
                    }
                    else if (info.WeaponJoint >= 0)
                    {
                        var prefab = Resources.Load<GameObject>("Weapons/" + type + "WeaponVisual");
                        var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
                        Check(mf != null && mf.sharedMesh != null, type + " static v_ mesh available for the IQM rig");
                        var bone = anim.Bones[info.WeaponJoint];
                        // prefab root rotation is reset to identity by WeaponView; the MeshFilter may sit on a child with its own transform
                        var childLocal = mf.transform.localToWorldMatrix * prefab.transform.worldToLocalMatrix;
                        Dump(mf.sharedMesh, bone.localToWorldMatrix * childLocal, "v_");
                    }
                    // Bone chain as OBJ lines for the wireframe snapshot.
                    obj.Append("g bones\n");
                    for (int j = 0; j < anim.Bones.Length; j++)
                    {
                        var p = host.transform.InverseTransformPoint(anim.Bones[j].position);
                        obj.Append("v ").Append(p.x.ToString("0.0000")).Append(' ').Append(p.y.ToString("0.0000")).Append(' ').Append(p.z.ToString("0.0000")).Append('\n');
                    }
                    for (int j = 0; j < anim.Bones.Length; j++)
                    {
                        int par = info.Rig.JointParents[j];
                        if (par >= 0) obj.Append("l ").Append(vbase + par + 1).Append(' ').Append(vbase + j + 1).Append('\n');
                    }
                    Directory.CreateDirectory("Artifacts/weapons");
                    File.WriteAllText("Artifacts/weapons/" + type + ".obj", obj.ToString());
                    Check(any, type + " rig produced geometry");
                    Debug.Log("[my-xonotic] weapon placement " + type + " (" + info.SourceFormat + "): min " + b.min.ToString("0.00") + " max " + b.max.ToString("0.00"));
                    Check(b.size.z > b.size.x && b.size.z > b.size.y, type + " extends forward more than sideways/up (" + b.size.ToString("0.00") + ")");
                    Check(b.max.z > 0.3f && b.min.z > -0.9f && b.max.z < 2.6f, type + " gun in front of the eye (z " + b.min.z.ToString("0.00") + ".." + b.max.z.ToString("0.00") + ")");
                    Check(b.max.y < 0.35f && b.min.y > -1.4f, type + " gun below the eye line (y " + b.min.y.ToString("0.00") + ".." + b.max.y.ToString("0.00") + ")");
                    Check(b.min.x > -0.7f && b.max.x < 1.0f, type + " gun near the right hand (x " + b.min.x.ToString("0.00") + ".." + b.max.x.ToString("0.00") + ")");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        /// dev.13: every touch control stays inside the safe area at the extreme
        /// button scales and in the left-handed mirror; the mirror really swaps sides.
        static void TouchLayoutSettings()
        {
            try
            {
                foreach (var scale in new[] { TouchSettings.MinButtonScale, 1f, TouchSettings.MaxButtonScale })
                foreach (var left in new[] { false, true })
                {
                    TouchSettings.OverrideForTest(scale, 1f, false, left);
                    var safe = TouchLayout.Safe;
                    var rects = new (string, Rect)[]
                    {
                        ("Fire", TouchLayout.Fire), ("Jump", TouchLayout.Jump), ("Alt", TouchLayout.Alt),
                        ("WpnPlus", TouchLayout.WpnPlus), ("WpnMinus", TouchLayout.WpnMinus), ("Pause", TouchLayout.Pause),
                        ("Restart", TouchLayout.Restart), ("MainMenu", TouchLayout.MainMenu), ("ShareLog", TouchLayout.ShareLog), ("CopyLog", TouchLayout.CopyLog)
                    };
                    string tag = " (scale " + scale.ToString("0.0") + (left ? ", left-handed)" : ")");
                    foreach (var (name, r) in rects)
                        Check(r.xMin >= safe.xMin - 0.5f && r.xMax <= safe.xMax + 0.5f && r.yMin >= safe.yMin - 0.5f && r.yMax <= safe.yMax + 0.5f && r.width > 0f,
                            "touch control " + name + " inside the safe area" + tag);
                    Check(!TouchLayout.Fire.Overlaps(TouchLayout.Jump), "FIRE and JUMP do not overlap" + tag);
                    Check(!TouchLayout.ShareLog.Overlaps(TouchLayout.CopyLog) && !TouchLayout.ShareLog.Overlaps(TouchLayout.MainMenu), "pause dev buttons do not overlap" + tag);
                    bool fireRight = TouchLayout.Fire.center.x > safe.center.x;
                    Check(fireRight != left, "FIRE is on the " + (left ? "left" : "right") + tag);
                    Check(TouchLayout.MoveZone.Contains(new Vector2(left ? safe.xMax - 1f : safe.xMin + 1f, safe.center.y)), "joystick zone on the " + (left ? "right" : "left") + tag);
                }
                TouchSettings.OverrideForTest(TouchSettings.MaxButtonScale, 1f, false, false);
                float big = TouchLayout.Fire.width;
                TouchSettings.OverrideForTest(TouchSettings.MinButtonScale, 1f, false, false);
                Check(big > TouchLayout.Fire.width * 1.5f, "button scale changes the FIRE diameter");
            }
            finally { TouchSettings.OverrideForTest(1f, 1f, false, false); }
        }

        /// dev.13: the DevCapture report is producible headlessly and carries the
        /// device header, a sample and the recent log lines.
        static void DevCaptureReport()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var dc = DevCapture.Ensure();
            Check(dc != null, "DevCapture singleton exists");
            string sample = dc.TakeSample();
            Check(sample.Contains("fps") && sample.Contains("no arena"), "DevCapture sample without an arena reports fps and 'no arena' (" + sample + ")");
            RuntimeErrorLog.Note("test note " + Guid.NewGuid().ToString("N").Substring(0, 8));
            string report = DevCapture.BuildReport(false);
            Check(report.Contains("my-xonotic") && report.Contains("errors ") && report.Contains("NOTE: test note"), "DevCapture report has header, counters and the recent NOTE line");
            Check(RuntimeErrorLog.Recent.Count > 0 && RuntimeErrorLog.Recent.Count <= RuntimeErrorLog.RecentCapacity, "RuntimeErrorLog keeps a bounded recent buffer");
            UnityEngine.Object.DestroyImmediate(dc.gameObject);
        }

        /// dev.13: the luma/luminos art generated by HudArtImporter loads through HudArt
        /// with alpha merged (icons are not opaque squares) and compressed mips.
        static void HudArtAssets()
        {
            HudArt.ClearCache();
            var manifestPath = HudArtImporter.ManifestPath;
            if (!File.Exists(manifestPath))
            {
                Passed.Add("SKIP hud art manifest absent (prepare-maps not run) - runtime falls back to text");
                return;
            }
            var manifest = JsonUtility.FromJson<HudArtImporter.Manifest>(File.ReadAllText(manifestPath));
            Check(manifest.imported >= 20, "hud art imported at least 20 images (" + manifest.imported + ", missing " + manifest.missing + ")");
            foreach (var name in new[] { "health", "armor", "ammo_rockets", "gametype_dm", "menu_background" })
                Check(HudArt.Texture(name) != null, "hud art '" + name + "' loads from Resources");
            for (int i = 0; i < WeaponController.WeaponCount; i++)
                Check(HudArt.Texture(HudArt.WeaponIconName((WeaponType)i)) != null, "weapon icon for " + (WeaponType)i + " loads");
            var health = HudArt.Texture("health");
            Check(health.mipmapCount > 1, "hud art has mipmaps");
            Check(health.format != TextureFormat.RGBA32 || !Mathf.IsPowerOfTwo(health.width), "hud art is GPU-compressed when power-of-two (" + health.format + ")");
            Check(health.width <= 256 && health.height <= 256, "hud icons are small (" + health.width + "x" + health.height + ")");
            var bg = HudArt.Texture("menu_background");
            Check(bg.width <= HudArtImporter.BackgroundMaxSize && bg.height <= HudArtImporter.BackgroundMaxSize, "menu background downscaled to <= " + HudArtImporter.BackgroundMaxSize);
            HudArt.ClearCache();
        }

        [Serializable] sealed class SpawnMapReport { public string map; public int spawns, grounded, snappable, floating; }
        [Serializable] sealed class SpawnReport { public string utc; public int maps; public List<SpawnMapReport> perMap = new List<SpawnMapReport>(); }

        /// dev.13 (bots invisible, hypothesis 3): every packaged map's spawn points
        /// must stand on a floor. Opens each generated map scene and probes downward
        /// exactly like ArenaBootstrap.ValidateSpawns does at runtime; a spawn with
        /// no floor within SpawnGroundProbe but one within SpawnSnapProbe is
        /// "snappable" (the arena moves it down), one with no floor at all is
        /// "floating" (the arena drops it). Every map must keep at least one usable
        /// spawn and floating spawns must stay rare. Report: Artifacts/bot-spawn-geometry.json.
        static void BotSpawnGeometry()
        {
            if (!Directory.Exists(FullGameBuild.MapsSceneFolder))
            {
                Passed.Add("SKIP bot spawn geometry (no generated map scenes)");
                return;
            }
            var scenes = Directory.GetFiles(FullGameBuild.MapsSceneFolder, "map_*.unity");
            Array.Sort(scenes, StringComparer.Ordinal);
            var report = new SpawnReport { utc = DateTime.UtcNow.ToString("O"), maps = scenes.Length };
            int totalSpawns = 0, totalFloating = 0;
            foreach (var path in scenes)
            {
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Physics.SyncTransforms();
                var mr = new SpawnMapReport { map = Path.GetFileNameWithoutExtension(path) };
                foreach (var marker in UnityEngine.Object.FindObjectsOfType<BspSpawnPoint>())
                {
                    mr.spawns++;
                    // ContentBridge: feet = origin - 24 qu.
                    Vector3 feet = marker.transform.position - Vector3.up * (24f / 32f);
                    RaycastHit hit;
                    if (ArenaBootstrap.SpawnHasFloor(feet, ArenaBootstrap.SpawnGroundProbe, out hit)) mr.grounded++;
                    else if (ArenaBootstrap.SpawnHasFloor(feet, ArenaBootstrap.SpawnSnapProbe, out hit)) mr.snappable++;
                    else mr.floating++;
                }
                report.perMap.Add(mr);
                totalSpawns += mr.spawns;
                totalFloating += mr.floating;
                if (mr.spawns > 0)
                    Check(mr.grounded + mr.snappable >= 1, mr.map + ": at least one spawn has a floor (" + mr.grounded + " grounded, " + mr.snappable + " snappable, " + mr.floating + " floating of " + mr.spawns + ")");
                EditorUtility.UnloadUnusedAssetsImmediate();
            }
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/bot-spawn-geometry.json", JsonUtility.ToJson(report, true));
            Check(totalSpawns > 0, "bot spawn geometry probed " + totalSpawns + " spawns across " + scenes.Length + " maps");
            Check(totalFloating * 10 <= totalSpawns, "floating spawns are rare (" + totalFloating + " of " + totalSpawns + ")");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
