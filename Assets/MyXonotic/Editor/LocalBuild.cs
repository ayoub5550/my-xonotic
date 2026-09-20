using System;
using System.IO;
using System.Linq;
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

        [MenuItem("My Xonotic/1 - Configure local project")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Ayoub";
            PlayerSettings.productName = "my-xonotic Development";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.ayoub.myxonotic");
            PlayerSettings.bundleVersion = File.ReadAllText("VERSION").Trim();
            PlayerSettings.Android.bundleVersionCode = 1;
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
            // Public development APK must contain only our original fixture.
            if (Environment.GetEnvironmentVariable("XONOTIC_INCLUDE_EXTERNAL") == "1")
                throw new BuildFailedException("External-content APK distribution is gated pending per-asset licence review.");
            CreateDevelopmentScene();
            Build(BuildTarget.Android, "my-xonotic-development.apk");
        }

        [MenuItem("My Xonotic/Build local Linux development player")]
        public static void BuildLinux()
        {
            Configure();
            CreateDevelopmentScene();
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
                revision = Environment.GetEnvironmentVariable("XONOTIC_REVISION") ?? "unrecorded",
                content = "original development fixture only; not the complete Xonotic game"
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
            public int errors, warnings;
            public ulong bytes;
        }
    }
}
