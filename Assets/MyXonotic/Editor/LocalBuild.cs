using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyXonotic.EditorTools
{
    /// <summary>Local-only entry points. No cloud services or account settings are embedded.</summary>
    public static class LocalBuild
    {
        public const string ScenePath = "Assets/MyXonotic/Scenes/DevelopmentArena.unity";

        /// XONOTIC_ALL_MAPS=1 selects the full-game package (every map + menu).
        public static bool FullGame => Environment.GetEnvironmentVariable("XONOTIC_ALL_MAPS") == "1";

        public static void ValidateCompilation()
        {
            // Reaching an Editor executeMethod proves the project assemblies
            // compiled and loaded. A zero-exit licensing/early-exit path cannot
            // generate this invocation-specific completion marker.
            Directory.CreateDirectory("Artifacts");
            File.WriteAllText("Artifacts/compile-result.json", JsonUtility.ToJson(
                new CompileReceipt
                {
                    passed = true,
                    invocation = Environment.GetEnvironmentVariable("XONOTIC_BUILD_INVOCATION"),
                    unity = Application.unityVersion
                }, true));
            Debug.Log("[my-xonotic] EDITOR COMPILATION VERIFIED");
        }

        [Serializable]
        sealed class CompileReceipt
        {
            public bool passed;
            public string invocation, unity;
        }

        [MenuItem("My Xonotic/1 - Configure local project")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Ayoub";
            PlayerSettings.productName = FullGame ? "my-xonotic" : "my-xonotic Development";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.ayoub.myxonotic");
            PlayerSettings.bundleVersion = File.ReadAllText("VERSION").Trim();
            int versionCode;
            if (!int.TryParse(Environment.GetEnvironmentVariable("XONOTIC_VERSION_CODE"), out versionCode)) versionCode = 5;
            PlayerSettings.Android.bundleVersionCode = versionCode;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.Android.useCustomKeystore = false;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 0;
            QualitySettings.shadows = ShadowQuality.Disable;
            Time.fixedDeltaTime = 1f / 60f;
            PinShader("MyXonotic/VertexColor");
            PinShader("MyXonotic/Lightmapped");
            UnpinShader("Standard"); // fallback only; pinning it forces ~24k variants through the shader compiler
            AssetDatabase.SaveAssets();
            Debug.Log("[my-xonotic] Local settings configured: Android ARM64 IL2CPP, no cloud build.");
        }

        static void PinShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new BuildFailedException("Required shader missing: " + name);
            var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphics.Length == 0) throw new BuildFailedException("GraphicsSettings asset unavailable.");
            var so = new SerializedObject(graphics[0]);
            var array = so.FindProperty("m_AlwaysIncludedShaders");
            for (var i = 0; i < array.arraySize; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            array.InsertArrayElementAtIndex(array.arraySize);
            array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void UnpinShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) return;
            var graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphics.Length == 0) return;
            var so = new SerializedObject(graphics[0]);
            var array = so.FindProperty("m_AlwaysIncludedShaders");
            for (var i = array.arraySize - 1; i >= 0; i--)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    array.DeleteArrayElementAtIndex(i);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("My Xonotic/2 - Create development arena scene")]
        public static void CreateDevelopmentScene()
        {
            Configure();
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("DevelopmentArena").AddComponent<ArenaBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[my-xonotic] Development scene generated; this is not an upstream Xonotic map.");
        }

        [MenuItem("My Xonotic/3 - Import external BSP as scene")]
        public static void ImportExternalBsp()
        {
            var path = Environment.GetEnvironmentVariable("XONOTIC_BSP");
            if (string.IsNullOrWhiteSpace(path) && !Application.isBatchMode)
                path = EditorUtility.OpenFilePanel("Select locally licensed IBSP v46 map", "", "bsp");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new BuildFailedException("Set XONOTIC_BSP to an existing IBSP v46 file.");
            Configure();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BspImportPipeline.Import(path);
            new GameObject("DevelopmentRules").AddComponent<ArenaBootstrap>();
            Directory.CreateDirectory("Assets/MyXonotic/Generated");
            const string output = "Assets/MyXonotic/Generated/ImportedArena.unity";
            EditorSceneManager.SaveScene(scene, output);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(output, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[my-xonotic] External geometry imported with development rules; NOT full Xonotic compatibility.");
        }

        [MenuItem("My Xonotic/4 - Build local Android development APK")]
        public static void BuildAndroid()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                throw new BuildFailedException("Android Build Support is missing. Install it locally via Unity Hub.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException("Select Android before invoking this method (CLI: -buildTarget Android).");
            Configure();
            if (FullGame)
            {
                PrepareFullGamePackage();
                Build(BuildTarget.Android, "my-xonotic-full.apk");
                return;
            }
            bool imported = Environment.GetEnvironmentVariable("XONOTIC_INCLUDE_EXTERNAL") == "1";
            if (imported) PrepareOriginalMap();
            else CreateDevelopmentScene();
            Build(BuildTarget.Android, imported ? "my-xonotic-unity-boil.apk" : "my-xonotic-development.apk");
        }

        /// <summary>Full package: weapons, every official map scene, menu, notices.</summary>
        public static void PrepareFullGamePackage()
        {
            IqmWeaponImporter.GenerateWeaponAssets();
            FullGameBuild.PrepareFullGame();
            WriteNotices("Unofficial Unity reimplementation packaging every official Xonotic 0.8.6 map; " +
                         "gameplay is an independent approximation and still incomplete.\n" +
                         "Original maps, textures, models, sounds and music retain their upstream (GPL) licences and authors.\n");
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }

        public static void PrepareOriginalMap()
        {
            // Explicit owner-authorized content build; upstream art retains its licences.
            // This is not an assertion that Unity/upstream-content legal review is complete.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XONOTIC_BSP")))
                Environment.SetEnvironmentVariable("XONOTIC_BSP", Path.GetFullPath(
                    "ThirdParty/Xonotic/maps-pk3/maps/boil.bsp"));
            ImportExternalBsp();
            IqmWeaponImporter.GenerateWeaponAssets();
            WriteNotices("Unofficial Unity reimplementation, experimental Boil slice; NOT complete Xonotic.\n" +
                "Boil: kuniu the frogg, Mirio. Original artwork retains upstream licences.\n");
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
        }

        static void WriteNotices(string scope)
        {
            string notices = "Assets/StreamingAssets/Xonotic/Notices";
            Directory.CreateDirectory(notices);
            foreach (var stale in Directory.GetFiles(notices, "*manifest*.json"))
                File.Delete(stale);
            foreach (var file in Directory.GetFiles("ThirdParty/Xonotic/LICENSES"))
                File.Copy(file, Path.Combine(notices, Path.GetFileName(file)), true);
            File.Copy("ThirdParty/Xonotic/README.md", Path.Combine(notices, "UPSTREAM-RESOURCES.md"), true);
            File.Copy("ThirdParty/Xonotic/resource-index.json", Path.Combine(notices, "resource-index.json"), true);
            File.WriteAllText(Path.Combine(notices, "BUILD-SCOPE.txt"),
                scope +
                "Upstream: https://xonotic.org and https://gitlab.com/xonotic\n" +
                "Port/source/provenance: https://github.com/ayoub5550/my-xonotic/tree/feat/unity-android-continuation\n" +
                "No DarkPlaces engine or QuakeC implementation included. No download required at runtime.\n");
            const string recoveryNotices = "ExternalContent/notices/Xonotic";
            if (Directory.Exists(recoveryNotices))
                foreach (var name in new[] { "COPYING", "GPL-2", "GPL-3" })
                {
                    string source = Path.Combine(recoveryNotices, name);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(notices, "upstream-" + name + ".txt"), true);
                }
            const string conversions = "ExternalContent/decoded/conversion-manifest.json";
            if (File.Exists(conversions))
                CopyPublicManifest(conversions, Path.Combine(notices, "texture-conversion-manifest.json"));
            const string continuation = "docs/unity-continuation-content.json";
            if (File.Exists(continuation))
                File.Copy(continuation, Path.Combine(notices, "unity-continuation-content.json"), true);
            foreach (var manifest in Directory.GetFiles("Assets/MyXonotic/Generated", "*manifest*.json", SearchOption.AllDirectories))
            {
                string owner = Path.GetFileName(Path.GetDirectoryName(manifest));
                string map = Path.GetFileNameWithoutExtension(Environment.GetEnvironmentVariable("XONOTIC_BSP") ?? "");
                if (!FullGame && owner != "Weapons" && owner != map) continue;
                CopyPublicManifest(manifest, Path.Combine(notices, owner+"-"+Path.GetFileName(manifest)));
            }
            if (FullGame && File.Exists("ThirdParty/Xonotic-0.8.6/publish-manifest.json"))
                File.Copy("ThirdParty/Xonotic-0.8.6/publish-manifest.json", Path.Combine(notices, "xonotic-0.8.6-publish-manifest.json"), true);
        }

        static void CopyPublicManifest(string source, string destination)
        {
            // Preserve content-relative paths and hashes, not local build-machine directories.
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            string json = File.ReadAllText(source).Replace(project, "");
            // A symlinked project root can have a different physical spelling.
            json = System.Text.RegularExpressions.Regex.Replace(json,
                @"/[^""\s]*?/(?=ExternalContent/|ThirdParty/|Assets/)", "");
            File.WriteAllText(destination, json);
        }

        [MenuItem("My Xonotic/Build local Linux development player")]
        public static void BuildLinux()
        {
            Configure();
            if (FullGame) PrepareFullGamePackage();
            else if (Environment.GetEnvironmentVariable("XONOTIC_INCLUDE_EXTERNAL") == "1") PrepareOriginalMap();
            else CreateDevelopmentScene();
            Build(BuildTarget.StandaloneLinux64, "my-xonotic.x86_64");
        }

        static void Build(BuildTarget target, string name)
        {
            Directory.CreateDirectory("Builds");
            EditorUserBuildSettings.buildAppBundle = false;
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine("Builds", name),
                target = target,
                options = BuildOptions.Development | BuildOptions.StrictMode
            });
            var summary = result.summary;
            string output = Path.Combine("Builds", name);
            long artifactBytes = 0;
            string artifactHash = null;
            if (summary.result == BuildResult.Succeeded && File.Exists(output))
            {
                artifactBytes = new FileInfo(output).Length;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(output))
                    artifactHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            var stamp = new BuildReceipt
            {
                unity = Application.unityVersion,
                version = PlayerSettings.bundleVersion,
                utc = DateTime.UtcNow.ToString("O"),
                target = target.ToString(),
                result = summary.result.ToString(),
                errors = summary.totalErrors,
                warnings = summary.totalWarnings,
                bytes = summary.totalSize,
                output = name,
                artifactBytes = artifactBytes,
                sha256 = artifactHash,
                invocation = Environment.GetEnvironmentVariable("XONOTIC_BUILD_INVOCATION") ?? "editor-menu",
                revision = Environment.GetEnvironmentVariable("XONOTIC_REVISION") ?? "unrecorded",
                content = FullGame
                    ? "All official Xonotic 0.8.6 maps + menu + music with approximate Unity gameplay; weapons/characters/modes/network incomplete; Android device unverified"
                    : Environment.GetEnvironmentVariable("XONOTIC_INCLUDE_EXTERNAL") == "1"
                    ? "Original Boil geometry/art with approximate Unity development rules; NOT full Xonotic; Android device unverified"
                    : "original development fixture only; not the complete Xonotic game"
            };
            File.WriteAllText("Builds/build-receipt.json", JsonUtility.ToJson(stamp, true));
            Debug.Log("[my-xonotic] BUILD RESULT " + summary.result);
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Local player build failed; inspect the build report.");
        }

        [Serializable]
        sealed class BuildReceipt
        {
            public string unity, version, utc, target, result, revision, content;
            public string output, sha256, invocation;
            public long artifactBytes;
            public int errors, warnings;
            public ulong bytes;
        }
    }
}
