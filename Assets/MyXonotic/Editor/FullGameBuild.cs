using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyXonotic.Content;
using MyXonotic.Gameplay;
using MyXonotic.Menu;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MyXonotic.EditorTools
{
    /// <summary>
    /// Packages every official map into one player: imports each .bsp into
    /// its own scene (geometry, textures, lightmaps, sky, spawns, triggers,
    /// pickups), attaches the upstream cdtrack, generates the MapCatalog and
    /// the MainMenu scene, and registers all scenes for the build.
    ///
    /// Inputs (env): XONOTIC_MAPS_ROOT (default ExternalContent/maps),
    /// XONOTIC_MUSIC_ROOT (default ExternalContent/music),
    /// XONOTIC_MAP_FILTER (comma list of map names; default all),
    /// XONOTIC_CONTENT_ROOTS (see XonoticContentResolver).
    /// One failing map is reported and skipped; it never aborts the others.
    /// </summary>
    public static class FullGameBuild
    {
        public const string MapsSceneFolder = "Assets/MyXonotic/Generated/Maps";
        public const string MenuScenePath = "Assets/MyXonotic/Generated/MainMenu.unity";
        public const string CatalogPath = "Assets/MyXonotic/Generated/Resources/MapCatalog.asset";
        public const string MusicFolder = "Assets/MyXonotic/Generated/Music";
        public const string PreviewFolder = "Assets/MyXonotic/Generated/MapPreviews";
        public const string ReportPath = "Artifacts/full-game-maps.json";

        [Serializable]
        public sealed class MapReport
        {
            public string map, scene, title, status, error;
            public int spawnPoints, warnings, pickups, triggers, mapModels, submodels, decorations;
            public float seconds;
            public string[] warningSamples;
        }

        [Serializable]
        public class CharacterReport
        {
            public string name;
            public bool ok;
            public string error;
            public int vertices;
            public int triangles;
            public int joints;
            public string poseAnim;
            public int poseFrame;
            public string[] materials;
            public string[] notes;
        }

        [Serializable]
        public sealed class Report
        {
            public int charactersImported;
            public int hudArtImported, hudArtMissing;
            public List<CharacterReport> characters = new List<CharacterReport>();
            public string utc, mapsRoot, contentRoots, textureFormat;
            public int requested, imported, failed;
            public List<MapReport> maps = new List<MapReport>();
        }

        static string MapsRoot => Environment.GetEnvironmentVariable("XONOTIC_MAPS_ROOT") ?? "ExternalContent/maps";
        static string MusicRoot => Environment.GetEnvironmentVariable("XONOTIC_MUSIC_ROOT") ?? "ExternalContent/music";

        [MenuItem("My Xonotic/5 - Prepare ALL maps + menu (full game)")]
        public static void PrepareFullGame()
        {
            var started = DateTime.UtcNow;
            var bsps = ListMaps();
            if (bsps.Count == 0)
                throw new BuildFailedException("No .bsp maps under " + MapsRoot + "/maps (restore content first: tools/content/publish_resources.py restore).");

            var report = new Report
            {
                utc = started.ToString("O"),
                mapsRoot = MapsRoot,
                contentRoots = Environment.GetEnvironmentVariable("XONOTIC_CONTENT_ROOTS"),
                textureFormat = ImportedTexturePolicy.FormatName,
                requested = bsps.Count
            };

            EnsureFolder(MapsSceneFolder);
            EnsureFolder(MusicFolder);
            EnsureFolder(PreviewFolder);
            EnsureFolder(Path.GetDirectoryName(CatalogPath).Replace('\\', '/'));

            var music = new MusicLibrary(MusicRoot);
            // Persist the catalog asset up front: an in-memory ScriptableObject
            // would be collected by UnloadUnusedAssetsImmediate() between maps.
            var catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MapCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var entries = new List<MapCatalog.Entry>();

            // dev.13: original HUD/menu art (luma icons, luminos background).
            var hudArt = HudArtImporter.Generate(new XonoticContentResolver());
            report.hudArtImported = hudArt.imported;
            report.hudArtMissing = hudArt.missing;

            // Original player models (static idle pose) for bots, shared by all maps.
            var characterWarnings = new List<string>();
            var characterResults = IqmCharacterImporter.ImportAll(new XonoticContentResolver(), characterWarnings);
            foreach (var cr in characterResults)
            {
                report.characters.Add(new CharacterReport
                {
                    name = cr.Name, ok = cr.Ok, error = cr.Error, vertices = cr.Vertices, triangles = cr.Triangles,
                    joints = cr.Joints, poseAnim = cr.PoseAnim, poseFrame = cr.PoseFrame,
                    materials = cr.Materials.ToArray(), notes = cr.Notes.ToArray()
                });
                if (cr.Ok) report.charactersImported++;
                Debug.Log("[FullGameBuild] character " + cr.Name + " -> " + (cr.Ok ? "ok" : "FAILED: " + cr.Error));
            }
            BspMapModelImporter.ClearCache();

            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var bsp in bsps)
            {
                string map = Path.GetFileNameWithoutExtension(bsp);
                var mr = new MapReport { map = map };
                var t0 = DateTime.UtcNow;
                try
                {
                    var info = MapInfo.Read(Path.ChangeExtension(bsp, ".mapinfo"));
                    var entry = ImportMapScene(bsp, info, music, mr);
                    entries.Add(entry);
                    scenes.Add(new EditorBuildSettingsScene(MapsSceneFolder + "/" + entry.sceneName + ".unity", true));
                    mr.status = "imported";
                    report.imported++;
                }
                catch (Exception e)
                {
                    mr.status = "failed";
                    mr.error = e.GetType().Name + ": " + e.Message;
                    report.failed++;
                    Debug.LogWarning("[FullGameBuild] map '" + map + "' skipped: " + mr.error);
                }
                mr.seconds = (float)(DateTime.UtcNow - t0).TotalSeconds;
                report.maps.Add(mr);
                Debug.Log("[FullGameBuild] " + map + " -> " + mr.status + " (" + mr.seconds.ToString("0.0") + "s)");
                // Keep the Editor's memory bounded across 30 imports.
                EditorUtility.UnloadUnusedAssetsImmediate();
            }

            entries.Sort((a, b) => string.Compare(a.title ?? a.mapName, b.title ?? b.mapName, StringComparison.OrdinalIgnoreCase));
            // UnloadUnusedAssetsImmediate may have dropped the managed wrapper; reload from disk.
            catalog = AssetDatabase.LoadAssetAtPath<MapCatalog>(CatalogPath);
            if (catalog == null) throw new BuildFailedException("MapCatalog asset vanished at " + CatalogPath);
            catalog.maps = entries;
            catalog.buildVersion = File.Exists("VERSION") ? File.ReadAllText("VERSION").Trim() : Application.version;
            catalog.buildUtc = report.utc;
            EditorUtility.SetDirty(catalog);

            CreateMainMenuScene();
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Directory.CreateDirectory("Artifacts");
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            Debug.Log(string.Format("[FullGameBuild] {0}/{1} maps imported, {2} failed, {3:0}s total. Report: {4}",
                report.imported, report.requested, report.failed, (DateTime.UtcNow - started).TotalSeconds, ReportPath));
            if (report.imported == 0)
                throw new BuildFailedException("Every map import failed; see " + ReportPath);
        }

        static List<string> ListMaps()
        {
            string dir = Path.Combine(MapsRoot, "maps");
            if (!Directory.Exists(dir)) return new List<string>();
            var filter = (Environment.GetEnvironmentVariable("XONOTIC_MAP_FILTER") ?? "")
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()).ToList();
            return Directory.GetFiles(dir, "*.bsp")
                .Where(f => !Path.GetFileName(f).StartsWith("_")) // _init/_hudsetup are engine-internal, not maps
                .Where(f => filter.Count == 0 || filter.Contains(Path.GetFileNameWithoutExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        static MapCatalog.Entry ImportMapScene(string bspPath, MapInfo info, MusicLibrary music, MapReport mr)
        {
            string map = Path.GetFileNameWithoutExtension(bspPath);
            string sceneName = "map_" + BspSafeName(map);
            string scenePath = MapsSceneFolder + "/" + sceneName + ".unity";

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BspImportPipeline.Import(bspPath);
            var arena = root.GetComponent<ImportedArena>();
            mr.warnings = arena != null ? arena.warnings.Length : 0;
            mr.warningSamples = arena != null ? arena.warnings.Take(12).ToArray() : Array.Empty<string>();
            mr.spawnPoints = root.GetComponentsInChildren<BspSpawnPoint>(true).Length;
            mr.pickups = root.GetComponentsInChildren<Pickup>(true).Length;
            mr.triggers = root.GetComponentsInChildren<MapTrigger>(true).Length;
            mr.mapModels = arena != null ? arena.mapModelCount : 0;
            mr.submodels = root.GetComponentsInChildren<MyXonotic.Content.ImportedSubmodel>(true).Length;
            mr.decorations = root.GetComponentsInChildren<PickupDecoration>(true).Length;
            if (mr.spawnPoints == 0)
                Debug.LogWarning("[FullGameBuild] '" + map + "' has no info_player_* spawn; ArenaBootstrap will use its origin fallback.");

            var rules = new GameObject("MatchRules");
            rules.AddComponent<ArenaBootstrap>();
            var clip = music.ClipForTrack(info.cdtrack);
            if (clip != null)
            {
                var mm = rules.AddComponent<MapMusic>();
                mm.clip = clip;
            }

            EditorSceneManager.SaveScene(scene, scenePath);

            var entry = new MapCatalog.Entry
            {
                mapName = map,
                sceneName = sceneName,
                title = string.IsNullOrEmpty(info.title) ? map : info.title,
                author = info.author,
                description = info.description,
                gametypes = info.gametypes.ToArray(),
                cdtrack = info.cdtrack,
                musicName = clip != null ? clip.name : null,
                preview = ImportPreview(Path.ChangeExtension(bspPath, ".jpg"), map),
                spawnPoints = mr.spawnPoints,
                warnings = mr.warnings
            };
            mr.scene = sceneName;
            mr.title = entry.title;
            return entry;
        }

        static Texture2D ImportPreview(string jpg, string map)
        {
            if (!File.Exists(jpg)) return null;
            string dest = PreviewFolder + "/" + BspSafeName(map) + ".jpg";
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(jpg).Length)
            {
                File.Copy(jpg, dest, true);
                AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
            }
            var importer = AssetImporter.GetAtPath(dest) as TextureImporter;
            if (importer != null && (importer.maxTextureSize != 512 || importer.mipmapEnabled || importer.textureType != TextureImporterType.Default))
            {
                importer.textureType = TextureImporterType.Default;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.isReadable = false;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dest);
        }

        static void CreateMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("MainMenu").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        static string BspSafeName(string name)
        {
            var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
            return new string(chars);
        }

        static void EnsureFolder(string assetFolder)
        {
            var parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // ------------------------------------------------------------------ mapinfo

        /// <summary>Plain-text reader for upstream maps/&lt;name&gt;.mapinfo. Data only.</summary>
        public sealed class MapInfo
        {
            public string title, author, description;
            public int cdtrack;
            public readonly List<string> gametypes = new List<string>();

            public static MapInfo Read(string path)
            {
                var info = new MapInfo();
                if (!File.Exists(path)) return info;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("//")) continue;
                    int space = line.IndexOf(' ');
                    string key = (space < 0 ? line : line.Substring(0, space)).ToLowerInvariant();
                    string value = space < 0 ? "" : line.Substring(space + 1).Trim();
                    switch (key)
                    {
                        case "title": info.title = value; break;
                        case "author": info.author = value; break;
                        case "description": info.description = value; break;
                        case "cdtrack":
                            int n;
                            if (int.TryParse(value.Split(' ')[0], out n)) info.cdtrack = n;
                            break;
                        case "gametype":
                            var gt = value.Split(' ')[0].ToLowerInvariant();
                            if (gt.Length > 0 && !info.gametypes.Contains(gt)) info.gametypes.Add(gt);
                            break;
                    }
                }
                return info;
            }
        }

        // ------------------------------------------------------------------ music

        /// <summary>
        /// Maps cdtrack numbers to the original OGG files of the upstream music
        /// pack (music-manifest.json written by publish_resources.py) and
        /// imports each used track once as a streaming Vorbis AudioClip.
        /// </summary>
        public sealed class MusicLibrary
        {
            readonly Dictionary<int, string> _tracks = new Dictionary<int, string>();
            readonly Dictionary<int, AudioClip> _clips = new Dictionary<int, AudioClip>();
            readonly string _root;

            [Serializable] sealed class Manifest { public Track[] tracks; }
            [Serializable] sealed class Track { public int number; public string name; public string entry; }

            public MusicLibrary(string root)
            {
                _root = root;
                string manifest = Path.Combine(root, "music-manifest.json");
                if (!File.Exists(manifest))
                {
                    Debug.LogWarning("[FullGameBuild] no music manifest at " + manifest + "; maps get no music.");
                    return;
                }
                var m = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifest));
                if (m == null || m.tracks == null) return;
                foreach (var t in m.tracks)
                {
                    string file = Path.Combine(root, t.entry);
                    if (File.Exists(file)) _tracks[t.number] = file;
                }
            }

            public AudioClip ClipForTrack(int number)
            {
                if (number <= 0) return null;
                AudioClip clip;
                if (_clips.TryGetValue(number, out clip)) return clip;
                string source;
                if (!_tracks.TryGetValue(number, out source))
                {
                    Debug.LogWarning("[FullGameBuild] cdtrack " + number + " not in music pack; map plays no music.");
                    _clips[number] = null;
                    return null;
                }
                string dest = MusicFolder + "/" + Path.GetFileName(source);
                if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(source).Length)
                {
                    File.Copy(source, dest, true);
                    AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
                }
                var importer = AssetImporter.GetAtPath(dest) as AudioImporter;
                if (importer != null)
                {
                    var settings = importer.defaultSampleSettings;
                    if (settings.loadType != AudioClipLoadType.Streaming || settings.compressionFormat != AudioCompressionFormat.Vorbis)
                    {
                        settings.loadType = AudioClipLoadType.Streaming;
                        settings.compressionFormat = AudioCompressionFormat.Vorbis;
                        settings.quality = 0.6f;
                        importer.defaultSampleSettings = settings;
                        importer.forceToMono = false;
                        importer.SaveAndReimport();
                    }
                }
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(dest);
                if (clip == null) Debug.LogWarning("[FullGameBuild] music import produced no AudioClip for " + dest);
                _clips[number] = clip;
                return clip;
            }
        }
    }
}
